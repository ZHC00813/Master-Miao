"""PCB assembly simplification. All lengths are OCCT-normalized millimetres.

analyze -> inspect/edit plan.json -> build. No shell execution or GUI automation.
"""
import argparse
import hashlib
import json
import math
import pathlib
import re
import sys

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--ocp-path', type=pathlib.Path)
commands = parser.add_subparsers(dest='command', required=True)
an = commands.add_parser('analyze')
an.add_argument('input', type=pathlib.Path)
an.add_argument('--work', type=pathlib.Path, required=True)
an.add_argument('--keep-largest', type=int, default=5)
an.add_argument('--keep-pattern', default='')
an.add_argument('--board-pattern', default='')
bu = commands.add_parser('build')
bu.add_argument('--multi-solid', action='store_true', help='Keep disconnected solids without adding connections')
bu.add_argument('--work', type=pathlib.Path, required=True)
bu.add_argument('--output', type=pathlib.Path, required=True)
bu.add_argument('--max-gap', type=float, default=0.2)
bu.add_argument('--bridge-radius', type=float, default=0.03)
bu.add_argument('--pcurves', action='store_true', help='Include redundant surface parameter curves for a compatibility retry')


def init_ocp(path):
    if path:
        sys.path.insert(0, str(path.resolve()))
    # Imports are deferred so --help works without the CAD runtime.
    global TopoDS, TopoDS_Shape, TopoDS_Compound, TopoDS_Iterator
    global TopExp_Explorer, TopAbs_SOLID, TopAbs_FACE
    global BRepTools, BRep_Builder, BRepBndLib, Bnd_Box, BRepGProp, GProp_GProps
    global BRepCheck_Analyzer, gp_Pnt, gp_Vec, gp_Dir, gp_Ax2
    from OCP.TopoDS import TopoDS, TopoDS_Shape, TopoDS_Compound, TopoDS_Iterator
    from OCP.TopExp import TopExp_Explorer
    from OCP.TopAbs import TopAbs_SOLID, TopAbs_FACE
    from OCP.BRepTools import BRepTools
    from OCP.BRep import BRep_Builder
    from OCP.BRepBndLib import BRepBndLib
    from OCP.Bnd import Bnd_Box
    from OCP.BRepGProp import BRepGProp
    from OCP.GProp import GProp_GProps
    from OCP.BRepCheck import BRepCheck_Analyzer
    from OCP.gp import gp_Pnt, gp_Vec, gp_Dir, gp_Ax2


def log(*args):
    print(*args, flush=True)


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')


def sha256(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        for block in iter(lambda: f.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def parts(shape, kind):
    ex = TopExp_Explorer(shape, kind)
    result = []
    while ex.More():
        result.append(ex.Current())
        ex.Next()
    return result


def volume(shape):
    p = GProp_GProps()
    BRepGProp.VolumeProperties_s(shape, p)
    return p.Mass()


def bbox(shape, optimal=False):
    b = Bnd_Box()
    if optimal:
        BRepBndLib.AddOptimal_s(shape, b, False, False)
    else:
        BRepBndLib.Add_s(shape, b)
    if b.IsVoid():
        return None
    a, z = b.CornerMin(), b.CornerMax()
    return [a.X(), a.Y(), a.Z(), z.X(), z.Y(), z.Z()]


def compound(shapes):
    c = TopoDS_Compound()
    b = BRep_Builder()
    b.MakeCompound(c)
    for s in shapes:
        b.Add(c, s)
    return c


def read_brep(path):
    s = TopoDS_Shape()
    ok = BRepTools.Read_s(s, str(path), BRep_Builder())
    if not ok or s.IsNull():
        raise ValueError(f'Cannot read geometry cache: {path}')
    return s


PORT = re.compile(r'USB|HDMI|\bF[FP]C\b|FFC|RJ[-_ ]?\d|CONNECTOR|HEADER|SOCKET|TERMINAL|JST|MOLEX|TYPE[-_ ]?C|\bSMA[-_ ]?(?:JACK|CONNECTOR)|插座|连接器|排针|排母|端子', re.I)
MECHANICAL = re.compile(r'SWITCH|BUTTON|SW[-_]SMD|STANDOFF|MOUNTING|HEATSINK|屏蔽罩|散热|支柱|开关|按键', re.I)
REFDES = re.compile(r'^(?:J|CN|CON|SW|P)\d+(?:[~ _-]|$)', re.I)
NAMED_COMPONENT = re.compile(r'^(?:[RCLDUQYFTJPK]|SW|CN|CON)\d+(?:[~ _-]|$)', re.I)
BOARD = re.compile(r'^(?:Board|PCB|PCBBoard|CircuitBoard)(?:[~ _-]|$)|电路板|板体', re.I)
DECORATION = re.compile(r'SILKSCREEN|TOPCOPPER|BOTTOMCOPPER|丝印', re.I)


def cylinder_envelope(local, bounds):
    """Choose a local analytic axis; validate containment before suggesting it."""
    from OCP.BRepAdaptor import BRepAdaptor_Surface
    from OCP.GeomAbs import GeomAbs_Cylinder
    from OCP.BRepPrimAPI import BRepPrimAPI_MakeCylinder
    from OCP.BRepAlgoAPI import BRepAlgoAPI_Cut
    dims = [bounds[i+3]-bounds[i] for i in range(3)]
    axes = []
    for face in parts(local, TopAbs_FACE):
        surf = BRepAdaptor_Surface(TopoDS.Face_s(face))
        if surf.GetType() == GeomAbs_Cylinder:
            direction = surf.Cylinder().Axis().Direction()
            coords = [abs(direction.X()), abs(direction.Y()), abs(direction.Z())]
            axis = max(range(3), key=lambda i: coords[i])
            if coords[axis] > .99999 and axis not in axes:
                axes.append(axis)
    axis = axes[0] if axes else min(range(3), key=lambda i:dims[i])
    transverse = [i for i in range(3) if i != axis]
    center = [(bounds[i]+bounds[i+3])/2 for i in range(3)]
    center[axis] = bounds[axis]
    direction = [0.,0.,0.]; direction[axis] = 1.
    def make(radius):
        return BRepPrimAPI_MakeCylinder(gp_Ax2(gp_Pnt(*center),gp_Dir(*direction)),radius,dims[axis]).Shape()
    candidate = make(max(dims[i] for i in transverse)/2 + 1e-7)
    # A cylindrical surface alone may be a hole in a box: check roundness AND containment.
    roundish = bool(axes) and min(dims[i] for i in transverse)/max(dims[i] for i in transverse) > .90
    if roundish:
        cut = BRepAlgoAPI_Cut(local,candidate)
        if cut.IsDone() and sum(abs(volume(x)) for x in parts(cut.Shape(),TopAbs_SOLID)) < 1e-6:
            return candidate, 'cylinder'
    # Explicit cylinder mode conservatively encloses the entire local bounding box.
    return make(math.hypot(*(dims[i]/2 for i in transverse))+1e-7), 'box'


def analyze(args):
    from OCP.STEPCAFControl import STEPCAFControl_Reader
    from OCP.TDocStd import TDocStd_Document
    from OCP.TCollection import TCollection_ExtendedString
    from OCP.XCAFDoc import XCAFDoc_DocumentTool
    from OCP.TDF import TDF_LabelSequence, TDF_Label
    from OCP.TDataStd import TDataStd_Name
    from OCP.TopLoc import TopLoc_Location
    from OCP.BRepPrimAPI import BRepPrimAPI_MakeBox
    from OCP.IFSelect import IFSelect_RetDone
    if args.keep_largest < 0:
        raise ValueError('--keep-largest must be nonnegative')
    source = args.input.resolve()
    work = args.work.resolve()
    if (work / 'plan.json').exists():
        raise ValueError('Use a new work directory; an existing reviewed plan will not be overwritten')
    work.mkdir(parents=True, exist_ok=True)
    (work / 'geometry').mkdir(exist_ok=True)
    r = STEPCAFControl_Reader()
    r.SetNameMode(True)
    if r.ReadFile(str(source)) != IFSelect_RetDone:
        raise ValueError('STEP reader failed')
    d = TDocStd_Document(TCollection_ExtendedString('PCB'))
    if not r.Transfer(d):
        raise ValueError('STEP assembly transfer failed')
    tool = XCAFDoc_DocumentTool.ShapeTool_s(d.Main())

    def name(label):
        attr = TDataStd_Name()
        return attr.Get().ToExtString() if label.FindAttribute(TDataStd_Name.GetID_s(), attr) else ''

    def resolve(label, parent):
        definition = label
        location = parent
        if tool.IsReference_s(label):
            definition = TDF_Label()
            tool.GetReferredShape_s(label, definition)
            location = parent.Multiplied(tool.GetLocation_s(label))
        return definition, location

    def children(label):
        seq = TDF_LabelSequence()
        tool.GetComponents_s(label, seq, False)
        return [seq.Value(i) for i in range(1, seq.Length() + 1)]

    roots = TDF_LabelSequence()
    tool.GetFreeShapes(roots)
    nodes = [(roots.Value(i), TopLoc_Location(), str(i)) for i in range(1, roots.Length() + 1)]
    # Unwrap assembly-only wrappers, stopping at the physical PCB component level.
    for _ in range(8):
        if len(nodes) != 1:
            break
        label, parent, path = nodes[0]
        definition, loc = resolve(label, parent)
        cs = children(definition)
        if not cs:
            break
        nodes = [(c, loc, path + '.' + str(i)) for i, c in enumerate(cs, 1)]
    if len(nodes) < 2:
        raise ValueError('Component-level assembly structure unavailable; do not simplify the whole PCB as a box')

    rows = []
    for k, (label, parent, tree_path) in enumerate(nodes):
        definition, loc = resolve(label, parent)
        local = tool.GetShape_s(definition)
        if local.IsNull():
            continue
        world = local.Moved(loc)
        ss = parts(world, TopAbs_SOLID)
        faces = len(parts(world, TopAbs_FACE))
        names = [x for x in [name(label), name(definition)] if x and not x.startswith('=>')]
        title = ' | '.join(dict.fromkeys(names)) or f'Unnamed component {k}'
        cid = f'c{k:04d}'
        row = dict(id=cid, tree_path=tree_path, name=title, solid_count=len(ss), faces=faces,
                   volume_mm3=sum(abs(volume(s)) for s in ss), action='keep', reason='', review=False)
        if not ss:
            row.update(action='omit', reason='empty' if not faces else 'zero-thickness decoration')
            if faces and not DECORATION.search(title):
                row.update(reason='unidentified non-solid geometry', review=True)
            rows.append(row)
            continue
        local_bounds = bbox(local, True)
        row['local_bbox_mm'] = local_bounds
        row['bbox_mm'] = bbox(world)
        row['dimensions_mm'] = [local_bounds[j + 3] - local_bounds[j] for j in range(3)]
        row['transform'] = [[loc.Transformation().Value(i, j) for j in range(1, 5)] for i in range(1, 4)]
        solid_compound = compound(ss)
        BRepTools.Write_s(solid_compound, str(work / 'geometry' / (cid + '.brep')))
        env = BRepPrimAPI_MakeBox(gp_Pnt(*local_bounds[:3]), gp_Pnt(*local_bounds[3:])).Shape().Moved(loc)
        BRepTools.Write_s(env, str(work / 'geometry' / (cid + '_envelope.brep')))
        cyl, suggested = cylinder_envelope(local, local_bounds)
        BRepTools.Write_s(cyl.Moved(loc), str(work / 'geometry' / (cid + '_cylinder.brep')))
        row['suggested_shape'] = suggested
        row['simplify_shape'] = 'auto'
        rows.append(row)
        log(cid, title, 'solids', len(ss), 'faces', faces)

    solids_rows = [x for x in rows if x['solid_count']]
    board_pattern = re.compile(args.board_pattern, re.I) if args.board_pattern else BOARD
    boards = [x for x in solids_rows if board_pattern.search(x['name'])]
    if len(boards) != 1:
        raise ValueError('Board is missing or ambiguous. Inspect assembly and supply --board-pattern; no geometry was simplified')
    board = boards[0]
    if board['solid_count'] != 1:
        raise ValueError('Board group includes multiple solids; inspect hierarchy before proceeding')
    extra_keep = re.compile(args.keep_pattern, re.I) if args.keep_pattern else None
    candidates = []
    for row in solids_rows:
        title = row['name']
        if row is board:
            row['reason'] = 'board: preserve all original holes and outline'
        elif PORT.search(title) or REFDES.search(title):
            row['reason'] = 'interface'
        elif MECHANICAL.search(title):
            row['reason'] = 'switch or mechanical feature'
        elif extra_keep and extra_keep.search(title):
            row['reason'] = 'explicit keep pattern'
        elif not NAMED_COMPONENT.search(title):
            row.update(reason='unclassified component: inspect before simplifying', review=True)
        else:
            candidates.append(row)
    candidates.sort(key=lambda x: x['volume_mm3'], reverse=True)
    for i, row in enumerate(candidates):
        row.update(action='keep' if i < args.keep_largest else 'envelope',
                   reason=f'large component rank {i+1}' if i < args.keep_largest else 'small component envelope')
    for row in rows:
        row['category'] = ('board' if row is board else 'interface' if row['reason'] in ['interface', 'switch or mechanical feature'] else 'other')
    for i, row in enumerate(sorted([r for r in solids_rows if r is not board and r['category'] != 'interface'], key=lambda r:r['volume_mm3'], reverse=True)):
        row['rank'] = i+1
    plan = dict(version=2, input=str(source), source_sha256=sha256(source), input_bytes=source.stat().st_size,
                unit='mm', board_id=board['id'], keep_largest=args.keep_largest, components=rows,
                solidworks_verified=False)
    write_json(work / 'plan.json', plan)
    log('Plan saved:', work / 'plan.json')
    log('Preserved:', [(x['id'], x['name'], x['reason']) for x in rows if x['solid_count'] and x['action']=='keep'])
    log('Envelopes:', sum(x['action']=='envelope' for x in rows), 'review items:', sum(x['review'] for x in rows))


def fuse(shapes):
    from OCP.BRepAlgoAPI import BRepAlgoAPI_Fuse
    from OCP.TopTools import TopTools_ListOfShape
    if len(shapes) == 1:
        return shapes[0]
    a, t = TopTools_ListOfShape(), TopTools_ListOfShape()
    a.Append(shapes[0])
    for s in shapes[1:]:
        t.Append(s)
    f = BRepAlgoAPI_Fuse()
    f.SetArguments(a)
    f.SetTools(t)
    f.SetRunParallel(True)
    f.SetFuzzyValue(1e-6)
    f.Build()
    if not f.IsDone():
        raise ValueError('Boolean fuse failed')
    return f.Shape()


def heal(shape):
    from OCP.ShapeFix import ShapeFix_Shape
    if BRepCheck_Analyzer(shape).IsValid():
        return shape
    before = volume(shape)
    fix = ShapeFix_Shape(shape)
    fix.SetPrecision(1e-6)
    fix.SetMaxTolerance(0.001)
    fix.Perform()
    result = fix.Shape()
    if not BRepCheck_Analyzer(result).IsValid() or abs(volume(result)-before) > max(0.001, abs(before)*1e-6):
        raise ValueError('Limited healing failed or changed volume beyond budget')
    return result


def connect(shape, max_gap, radius):
    from OCP.BRepExtrema import BRepExtrema_DistShapeShape
    from OCP.BRepPrimAPI import BRepPrimAPI_MakeSphere, BRepPrimAPI_MakeCylinder
    additions = []
    for attempt in range(2):
        ss = parts(shape, TopAbs_SOLID)
        if len(ss) == 1:
            return shape, additions
        if len(ss) > 30:
            raise ValueError(f'{len(ss)} disconnected solids: inspect layout instead of adding many bridges')
        ss.sort(key=lambda s: abs(volume(s)), reverse=True)
        main = ss[0]
        fs = [(f, bbox(f)) for f in parts(main, TopAbs_FACE)]
        bridges = []
        for small in ss[1:]:
            bounds = bbox(small)
            near = [f for f,b in fs if all(b[j] <= bounds[j+3]+max_gap+radius and b[j+3]>=bounds[j]-max_gap-radius for j in range(3))]
            if not near:
                raise ValueError('Disconnected component has no nearby support within bridge limit')
            ds = BRepExtrema_DistShapeShape()
            ds.LoadS1(compound(near))
            ds.LoadS2(small)
            ds.SetMultiThread(True)
            ds.Perform()
            if not ds.IsDone() or ds.Value() > max_gap:
                raise ValueError('Gap exceeds authorized small-connection budget')
            p, q = ds.PointOnShape1(1), ds.PointOnShape2(1)
            distance = p.Distance(q)
            bridges.append(BRepPrimAPI_MakeSphere(p, radius).Shape())
            if distance > 1e-7:
                bridges.extend([BRepPrimAPI_MakeSphere(q, radius).Shape(), BRepPrimAPI_MakeCylinder(gp_Ax2(p,gp_Dir(gp_Vec(p,q))),radius,distance).Shape()])
            additions.append(dict(gap_mm=distance, radius_mm=radius, start=[p.X(),p.Y(),p.Z()], end=[q.X(),q.Y(),q.Z()]))
        shape = heal(fuse(ss+bridges))
    if len(parts(shape, TopAbs_SOLID)) != 1:
        raise ValueError('Could not create one valid solid within two bounded connection passes')
    return shape, additions


def build(args):
    from OCP.STEPControl import STEPControl_Writer, STEPControl_Reader, STEPControl_AsIs
    from OCP.Interface import Interface_Static
    from OCP.IFSelect import IFSelect_RetDone
    work, output = args.work.resolve(), args.output.resolve()
    plan = json.loads((work / 'plan.json').read_text(encoding='utf-8'))
    if output.exists():
        raise ValueError('Output exists; choose a new file name')
    if not 0 < args.bridge_radius <= 0.1 or not 0 <= args.max_gap <= 0.2:
        raise ValueError('Bridge parameters exceed this workflow budget; review the geometry explicitly')
    if sha256(pathlib.Path(plan['input'])) != plan['source_sha256']:
        raise ValueError('Input changed after analysis')
    rows = plan['components']
    if any(x['review'] for x in rows):
        raise ValueError('Unresolved review items in plan.json')
    board = next(x for x in rows if x['id']==plan['board_id'])
    if board['action'] != 'keep':
        raise ValueError('The board may not be simplified or omitted')
    shapes, keep, simplified, omitted = [], [], [], []
    for row in rows:
        action = row['action']
        if action not in ['keep','envelope','omit']:
            raise ValueError(f'Unknown action {action}')
        if action=='omit':
            if row['solid_count']:
                raise ValueError('Solid components must be preserved or simplified, not silently omitted')
            omitted.append(row['name'])
            continue
        if not row['solid_count']:
            raise ValueError('Non-solid feature requires manual treatment')
        kind = row.get('simplify_shape','auto')
        if kind not in ['auto','box','cylinder']: raise ValueError('Unknown simplification shape')
        if kind == 'auto': kind = row.get('suggested_shape','box')
        suffix = ('_cylinder' if kind == 'cylinder' else '_envelope') if action == 'envelope' else ''
        path = work / 'geometry' / (row['id'] + suffix + '.brep')
        shapes.extend(parts(read_brep(path),TopAbs_SOLID))
        (keep if action=='keep' else simplified).append(row['name'])
    log('Fusing', len(shapes), 'shapes; protected',len(keep),'simplified',len(simplified))
    shape = compound(shapes) if args.multi_solid else heal(fuse(shapes))
    base_volume = volume(shape)
    bridges = []
    if not args.multi_solid:
        shape, bridges = connect(shape,args.max_gap,args.bridge_radius)
    added_volume = volume(shape)-base_volume
    if added_volume < -0.001 or added_volume > max(0.01,abs(base_volume)*0.0001):
        raise ValueError('Connection volume budget exceeded')
    shape = heal(shape)
    ss = parts(shape,TopAbs_SOLID)
    if not ss or (not args.multi_solid and len(ss)!=1):
        raise ValueError('Unexpected solid count')
    if not args.multi_solid: shape = ss[0]
    expected_solids = len(ss)
    before_volume, before_bounds = volume(shape), bbox(shape)
    BRepTools.Write_s(shape,str(work/'result.brep'))
    output.parent.mkdir(parents=True,exist_ok=True)
    candidate = output.with_name(output.stem+'.candidate.stp')
    if candidate.exists():
        raise ValueError('Candidate exists; inspect it or choose another output path')
    # Describe disconnected solids as one named multi-body part. The plain
    # writer expands compounds into anonymous assembly products in SolidWorks.
    from OCP.STEPCAFControl import STEPCAFControl_Writer
    from OCP.TDocStd import TDocStd_Document
    from OCP.TCollection import TCollection_ExtendedString
    from OCP.TDataStd import TDataStd_Name
    from OCP.XCAFDoc import XCAFDoc_DocumentTool
    doc = TDocStd_Document(TCollection_ExtendedString('PCB'))
    shape_tool = XCAFDoc_DocumentTool.ShapeTool_s(doc.Main())
    label = shape_tool.AddShape(shape, False)
    product_name = 'PCB_LIGHT_' + hashlib.sha256(str(output).encode('utf-8')).hexdigest()[:12]
    TDataStd_Name.Set_s(label, TCollection_ExtendedString(product_name))
    w=STEPCAFControl_Writer()
    w.SetNameMode(True)
    # Writer initialization registers exchange parameters in a fresh process.
    if not Interface_Static.SetCVal_s('write.step.schema','AP214IS'):
        raise ValueError('Could not configure STEP AP214 export')
    if not Interface_Static.SetCVal_s('write.step.unit','MM'):
        raise ValueError('Could not configure millimetre export units')
    if not Interface_Static.SetIVal_s('write.surfacecurve.mode',1 if args.pcurves else 0):
        raise ValueError('Could not configure STEP parameter-curve export')
    if not w.Transfer(doc,STEPControl_AsIs) or w.Write(str(candidate))!=IFSelect_RetDone:
        raise ValueError('STEP export failed')
    r=STEPControl_Reader()
    if r.ReadFile(str(candidate))!=IFSelect_RetDone or not r.TransferRoots():
        raise ValueError('STEP read-back failed')
    final=r.OneShape()
    fs=parts(final,TopAbs_SOLID)
    valid=BRepCheck_Analyzer(final).IsValid()
    after_volume, after_bounds=volume(final),bbox(final)
    if len(fs)!=expected_solids or not valid or len(parts(final,TopAbs_FACE))!=sum(len(parts(x,TopAbs_FACE)) for x in fs):
        raise ValueError('Round-trip solid/geometry validation failed')
    if abs(after_volume-before_volume)>max(0.001,abs(before_volume)*1e-6) or max(abs(a-b) for a,b in zip(before_bounds,after_bounds))>0.01:
        raise ValueError('Export altered geometry beyond round-trip budget')
    candidate.rename(output)
    report=dict(input=plan['input'],output=str(output),source_sha256=plan['source_sha256'],input_bytes=plan['input_bytes'],output_bytes=output.stat().st_size,
                original_faces=sum(x['faces'] for x in rows),final_faces=len(parts(final,TopAbs_FACE)),solid_count=len(fs),valid=valid,
                volume_mm3=after_volume,bbox_mm=after_bounds,preserved=keep,simplified=simplified,omitted_non_solids=omitted,
                bridges=bridges,bridge_added_volume_mm3=added_volume,include_pcurves=args.pcurves,solidworks_verified=False)
    write_json(work/'report.json',report)
    log('SUCCESS',json.dumps({k:v for k,v in report.items() if k not in ['preserved','simplified','omitted_non_solids','bridges']},ensure_ascii=False))


if __name__=='__main__':
    args=parser.parse_args()
    init_ocp(args.ocp_path)
    try:
        (analyze if args.command=='analyze' else build)(args)
    except Exception as e:
        log('STOP:',str(e))
        raise SystemExit(1)
