"""Prepare bounded geometry for the SolidWorks native PCB union worker."""
import argparse, itertools, json, math, pathlib, sys
import pcb_simplify as m

def read_step(path):
    from OCP.STEPControl import STEPControl_Reader
    from OCP.IFSelect import IFSelect_RetDone
    reader=STEPControl_Reader()
    if reader.ReadFile(str(path))!=IFSelect_RetDone or not reader.TransferRoots():
        raise ValueError('STEP read failed: '+str(path))
    return reader.OneShape()

def write_part(shapes,path):
    from OCP.STEPCAFControl import STEPCAFControl_Writer
    from OCP.STEPControl import STEPControl_AsIs
    from OCP.TDocStd import TDocStd_Document
    from OCP.TCollection import TCollection_ExtendedString
    from OCP.XCAFDoc import XCAFDoc_DocumentTool
    from OCP.TDataStd import TDataStd_Name
    from OCP.Interface import Interface_Static
    from OCP.IFSelect import IFSelect_RetDone
    doc=TDocStd_Document(TCollection_ExtendedString('BinXCAF'))
    st=XCAFDoc_DocumentTool.ShapeTool_s(doc.Main())
    label=st.AddShape(m.compound(shapes),False)
    TDataStd_Name.Set_s(label,TCollection_ExtendedString('PCB_NATIVE_GEOMETRY'))
    writer=STEPCAFControl_Writer()
    Interface_Static.SetCVal_s('write.step.schema','AP214IS')
    Interface_Static.SetCVal_s('write.step.unit','MM')
    Interface_Static.SetIVal_s('write.surfacecurve.mode',1)
    if not writer.Transfer(doc,STEPControl_AsIs) or writer.Write(str(path))!=IFSelect_RetDone:
        raise ValueError('STEP preparation failed')

def witnesses(body):
    from OCP.BRepClass3d import BRepClass3d_SolidClassifier
    from OCP.TopAbs import TopAbs_IN
    from OCP.BRepAdaptor import BRepAdaptor_Surface
    from OCP.GeomLProp import GeomLProp_SLProps
    from OCP.BRep import BRep_Tool
    check=BRepClass3d_SolidClassifier(body); box=m.bbox(body,True); result=[]
    def add(point):
        check.Perform(point,1e-7)
        if check.State()==TopAbs_IN:
            xyz=[point.X(),point.Y(),point.Z()]
            if not any(sum((a-b)**2 for a,b in zip(xyz,q))<1e-10 for q in result):result.append(xyz)
    for fractions in itertools.product([.2,.5,.8],repeat=3):
        add(m.gp_Pnt(*[box[k]+(box[k+3]-box[k])*fractions[k] for k in range(3)]))
        if len(result)>=9:return result
    for f in m.parts(body,m.TopAbs_FACE):
        face=m.TopoDS.Face_s(f); adaptor=BRepAdaptor_Surface(face)
        u=(adaptor.FirstUParameter()+adaptor.LastUParameter())/2
        v=(adaptor.FirstVParameter()+adaptor.LastVParameter())/2
        if not math.isfinite(u+v):continue
        surface=BRep_Tool.Surface_s(face)
        props=GeomLProp_SLProps(surface,u,v,1,1e-9)
        if not props.IsNormalDefined():continue
        point=surface.Value(u,v); normal=props.Normal()
        for sign in [-1,1]:
            add(m.gp_Pnt(point.X()+sign*normal.X()*1e-4,point.Y()+sign*normal.Y()*1e-4,point.Z()+sign*normal.Z()*1e-4))
        if len(result)>=9:return result
    if not result:raise ValueError('Cannot establish an interior witness for a retained body')
    return result

def reconstruct(body):
    from OCP.BOPAlgo import BOPAlgo_MakerVolume
    from OCP.TopTools import TopTools_ListOfShape
    args=TopTools_ListOfShape()
    for face in m.parts(body,m.TopAbs_FACE):args.Append(face)
    op=BOPAlgo_MakerVolume();op.SetArguments(args);op.SetIntersect(True)
    op.SetAvoidInternalShapes(True);op.SetRunParallel(True);op.Perform()
    if op.HasErrors():return body
    solids=m.parts(op.Shape(),m.TopAbs_SOLID)
    if not solids:return body
    candidate=max(solids,key=m.volume)
    if (not m.BRepCheck_Analyzer(candidate).IsValid()
        or abs(m.volume(candidate)-m.volume(body))>max(.0001,abs(m.volume(body))*1e-6)
        or max(abs(a-b) for a,b in zip(m.bbox(candidate,True),m.bbox(body,True)))>1e-5):return body
    return candidate

def cavity_tools(body):
    from OCP.TopAbs import TopAbs_SHELL
    from OCP.BRepBuilderAPI import BRepBuilderAPI_MakeSolid
    from OCP.BRepLib import BRepLib
    shells=m.parts(body,TopAbs_SHELL)
    if len(shells)==1:return None
    solids=[]
    for shell in shells:
        solid=BRepBuilderAPI_MakeSolid(m.TopoDS.Shell_s(shell)).Solid()
        BRepLib.OrientClosedSolid_s(solid);solids.append(solid)
    solids.sort(key=m.volume,reverse=True)
    if abs(m.volume(solids[0])-sum(m.volume(s) for s in solids[1:])-m.volume(body))>.001:
        raise ValueError('Unsupported nested shell structure; original preserved')
    return solids

def chunks(shapes,folder,prefix):
    records=[]
    for offset in range(0,len(shapes),180):
        batch=shapes[offset:offset+180];path=folder/(prefix+'-'+str(offset//180)+'.stp')
        write_part(batch,path)
        records.append(dict(path=str(path.resolve()),count=len(batch)))
    return records

def prepare(args):
    from micro_details import clean_body
    from edge_parameters import normalize
    plan=json.loads((args.work/'plan.json').read_text(encoding='utf-8-sig'))
    if any(row['review'] for row in plan['components']):raise ValueError('Unresolved component review')
    if m.sha256(pathlib.Path(plan['input']))!=plan['source_sha256']:raise ValueError('Source file has changed; analyze again')
    extreme=m.validate_extreme_plan(plan)
    omitted=[]
    folder=args.work/'native';folder.mkdir(exist_ok=True)
    groups=[];plain=[];evidence=[];details=[];all_bounds=[];kept=[];simplified=[]
    for row in plan['components']:
        action=row['action']
        if action=='omit':
            if row['solid_count'] and not extreme:raise ValueError('Cannot omit a physical component')
            if row['solid_count']:omitted.append(row['name'])
            continue
        if action not in ['keep','envelope'] or (row['id']==plan['board_id'] and action!='keep'):
            raise ValueError('Invalid component action')
        kind=row.get('simplify_shape','auto')
        if kind=='auto':kind=row.get('suggested_shape','box')
        if kind not in ['box','cylinder']:raise ValueError('Invalid simplification shape')
        suffix=('_cylinder' if kind=='cylinder' else '_envelope') if action=='envelope' else ''
        bodies=m.parts(m.read_brep(args.work/'geometry'/(row['id']+suffix+'.brep')),m.TopAbs_SOLID)
        retained=0
        for index,body in enumerate(bodies):
            if args.clean and action=='keep' and row['id']!=plan['board_id']:
                body,_=normalize(body,m)
                body,record=clean_body(body,m,len(bodies)>1)
                if record:details.append(dict(record,component_id=row['id'],body_index=index))
                if body is None:continue
            retained+=1
            evidence.append(dict(component=row['name'],points=witnesses(body)))
            all_bounds.append(m.bbox(body,True));cutters=cavity_tools(body)
            if cutters:
                path=folder/('cavities-'+row['id']+'-'+str(index)+'.stp');write_part(cutters,path)
                groups.append(dict(path=str(path.resolve()),count=len(cutters),kind='cut',volume_mm3=m.volume(body)))
            else:
                if action=='keep':body=reconstruct(body)
                plain.append(body)
        if not retained:raise ValueError('No physical bodies remain for '+row['name'])
        (kept if action=='keep' else simplified).append(row['name'])
    groups.extend(dict(record,kind='bodies') for record in chunks(plain,folder,'components'))
    bounds=[min(b[k] for b in all_bounds) for k in range(3)]+[max(b[k+3] for b in all_bounds) for k in range(3)]
    manifest=dict(groups=groups,witnesses=evidence,bounds_mm=bounds,output=str(args.output.resolve()),
                  input_sha256=plan['source_sha256'],simplification_mode='extreme' if extreme else 'standard',omitted_components=omitted,kept=kept,simplified=simplified,detail_cleanup=details,
                  radius_mm=.03,max_gap_mm=.2,folder=str(folder.resolve()))
    m.write_json(folder/'manifest.json',manifest)
    print('PREPARED',len(evidence),'bodies',flush=True)

def contacts(args):
    from OCP.BRepAlgoAPI import BRepAlgoAPI_Section
    from OCP.BRepAdaptor import BRepAdaptor_Curve
    from OCP.BRepPrimAPI import BRepPrimAPI_MakeSphere,BRepPrimAPI_MakeCylinder
    from OCP.TopAbs import TopAbs_EDGE
    from OCP.GeomAbs import GeomAbs_Line
    bodies=sorted(m.parts(read_step(args.input),m.TopAbs_SOLID),key=m.volume,reverse=True)
    links=[];records=[];radius=.03
    for index,body in enumerate(bodies[1:]):
        section=BRepAlgoAPI_Section(bodies[0],body,False);section.Approximation(True);section.Build()
        if not section.IsDone():raise ValueError('Contact boundary analysis failed')
        edges=m.parts(section.Shape(),TopAbs_EDGE);pieces=[];vertices=set()
        for edge in edges:
            curve=BRepAdaptor_Curve(m.TopoDS.Edge_s(edge));start,end=curve.FirstParameter(),curve.LastParameter()
            count=1 if curve.GetType()==GeomAbs_Line else 8
            points=[curve.Value(start+(end-start)*i/count) for i in range(count+1)]
            for a,b in zip(points,points[1:]):
                if a.Distance(b)<1e-7:continue
                pieces.append(BRepPrimAPI_MakeCylinder(m.gp_Ax2(a,m.gp_Dir(m.gp_Vec(a,b))),radius,a.Distance(b)).Shape())
                for point in [a,b]:
                    key=tuple(round(x,7) for x in (point.X(),point.Y(),point.Z()))
                    if key not in vertices:pieces.append(BRepPrimAPI_MakeSphere(point,radius).Shape());vertices.add(key)
        if pieces:
            if not all(m.BRepCheck_Analyzer(p).IsValid() for p in pieces):raise ValueError('Invalid contact connector')
            links.extend(pieces);records.append(dict(body=index,contact_edges=len(edges),radius_mm=radius))
    # Also connect disconnected components by the bounded nearest-contact rule.
    extra,nearest=m.micro_connections(bodies,.2,radius)
    links.extend(extra);records.extend(nearest)
    if not links or len(links)>4000:raise ValueError('Contact repair cannot be safely bounded')
    args.work.mkdir(exist_ok=True)
    manifest=dict(groups=chunks(links,args.work,'links'),connections=records,
                  upper_added_volume_mm3=sum(abs(m.volume(s)) for s in links))
    m.write_json(args.work/'contacts.json',manifest)
    print('CONTACT CONNECTORS',len(links),flush=True)

def audit(args):
    from OCP.BRepClass3d import BRepClass3d_SolidClassifier
    from OCP.TopAbs import TopAbs_IN,TopAbs_ON
    manifest=json.loads(args.manifest.read_text(encoding='utf-8-sig'));shape=read_step(args.input)
    solids=m.parts(shape,m.TopAbs_SOLID)
    if len(solids)!=1 or not m.BRepCheck_Analyzer(shape).IsValid():raise ValueError('Final STEP is not a valid single solid')
    check=BRepClass3d_SolidClassifier(solids[0]);count=0
    for record in manifest['witnesses']:
        for point in record['points']:
            check.Perform(m.gp_Pnt(*point),1e-5);count+=1
            if check.State() not in [TopAbs_IN,TopAbs_ON]:raise ValueError('Lost material in component: '+record['component'])
    actual=m.bbox(shape,True)
    if max(abs(a-b) for a,b in zip(actual,manifest['bounds_mm']))>.061:
        raise ValueError('Final bounds exceed the micro-connector allowance')
    report=dict(solid_count=1,valid=True,volume_mm3=m.volume(shape),bbox_mm=actual,final_faces=len(m.parts(shape,m.TopAbs_FACE)),
                output_bytes=args.input.stat().st_size,preservation_samples=count,sha256=m.sha256(args.input))
    m.write_json(args.work/'geometry-audit.json',report);print('PASS RETENTION',count,flush=True)

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--ocp-path',type=pathlib.Path)
    sub=parser.add_subparsers(dest='command',required=True)
    p=sub.add_parser('prepare');p.add_argument('--work',type=pathlib.Path,required=True);p.add_argument('--output',type=pathlib.Path,required=True);p.add_argument('--clean',action='store_true')
    p=sub.add_parser('contacts');p.add_argument('--input',type=pathlib.Path,required=True);p.add_argument('--work',type=pathlib.Path,required=True)
    p=sub.add_parser('audit');p.add_argument('--input',type=pathlib.Path,required=True);p.add_argument('--manifest',type=pathlib.Path,required=True);p.add_argument('--work',type=pathlib.Path,required=True)
    args=parser.parse_args();m.init_ocp(args.ocp_path);globals()[args.command](args)

if __name__=='__main__':main()
