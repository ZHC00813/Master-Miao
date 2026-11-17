"""Bounded cosmetic-detail cleanup before PCB bodies are fused.

Never changes the board. Physical components remain present. A native CAD
geometry check is still required after neutral-file translation.
"""

def clean_body(body, kernel, allow_thin_decoration=False):
    m = kernel
    from OCP.BRepAlgoAPI import BRepAlgoAPI_Defeaturing
    from OCP.TopTools import TopTools_ListOfShape, TopTools_IndexedMapOfShape
    from OCP.TopAbs import TopAbs_EDGE
    from OCP.BRepAdaptor import BRepAdaptor_Surface
    from OCP.BRep import BRep_Tool
    from OCP.gp import gp_Vec
    from OCP.GeomAbs import GeomAbs_BSplineSurface, GeomAbs_Cylinder
    from OCP.Bnd import Bnd_OBB
    bounds=m.bbox(body,True); before=m.volume(body)
    if allow_thin_decoration and before < .001 and max(bounds[k+3]-bounds[k] for k in range(3)) < 1.0:
        obb=Bnd_OBB();m.BRepBndLib.AddOBB_s(body,obb,True,False,True)
        if 2*min(obb.XHSize(),obb.YHSize(),obb.ZHSize()) < .003:
            return None,dict(kind='thin-decoration',before_volume_mm3=before,volume_change_mm3=-before,bounds_mm=bounds)
    faces=m.parts(body,m.TopAbs_FACE); boxes=[m.bbox(f,True) for f in faces]
    eligible={i for i,b in enumerate(boxes) if max(b[k+3]-b[k] for k in range(3)) <= .5}
    def is_detail(index):
        face=m.TopoDS.Face_s(faces[index])
        adaptor=BRepAdaptor_Surface(face)
        if adaptor.GetType()==GeomAbs_Cylinder:
            return 1e-6 < abs(adaptor.LastVParameter()-adaptor.FirstVParameter()) < .003
        if adaptor.GetType()!=GeomAbs_BSplineSurface: return False
        # Small size alone does not make a face decorative: electrical leads
        # and ceramic/metal transitions also contain tiny curved faces.
        # Embossed lettering can be wider than 0.03 mm, but its extrusion is
        # only a few microns deep. Identify that depth rather than flattening
        # arbitrary larger spline faces on the connector body.
        surface=BRep_Tool.Surface_s(face)
        if surface.VDegree()==1 and surface.NbVPoles()==2:
            vectors=[gp_Vec(surface.Pole(i,1),surface.Pole(i,2)) for i in range(1,surface.NbUPoles()+1)]
        elif surface.UDegree()==1 and surface.NbUPoles()==2:
            vectors=[gp_Vec(surface.Pole(1,i),surface.Pole(2,i)) for i in range(1,surface.NbVPoles()+1)]
        else: return False
        return (1e-6 < vectors[0].Magnitude() < .003
                and all(v.Subtracted(vectors[0]).Magnitude()<1e-8 for v in vectors[1:]))
    seeds=[i for i in eligible if is_detail(i)]
    if not seeds: return body,None
    edge_map=TopTools_IndexedMapOfShape(); owners={}
    for i,face in enumerate(faces):
        for edge in m.parts(face,TopAbs_EDGE): owners.setdefault(edge_map.Add(edge),[]).append(i)
    adjacent={i:set() for i in eligible}
    for indices in owners.values():
        for i in indices:
            if i in eligible: adjacent[i].update(j for j in indices if j!=i and j in eligible)
    selected=set(); visited=set()
    for seed in seeds:
        if seed in visited: continue
        group={seed}; pending=[seed]
        while pending:
            for other in adjacent[pending.pop()]:
                if other not in group: group.add(other); pending.append(other)
        visited.update(group)
        if max(max(boxes[i][k+3] for i in group)-min(boxes[i][k] for i in group) for k in range(3)) <= 1.0:
            detail_box=Bnd_OBB()
            m.BRepBndLib.AddOBB_s(m.compound([faces[i] for i in group]),detail_box,True,True,False)
            # The whole connected feature must be shallow, not just its seed.
            # Otherwise adjoining lead/ceramic faces could be removed with text.
            if 2*min(detail_box.XHSize(),detail_box.YHSize(),detail_box.ZHSize()) <= .0031:
                selected.update(group)
    if not selected or len(selected)==len(faces): return body,None
    removable=TopTools_ListOfShape()
    for i in selected: removable.Append(faces[i])
    operation=BRepAlgoAPI_Defeaturing();operation.SetShape(body);operation.AddFacesToRemove(removable)
    operation.SetRunParallel(True);operation.Build()
    if not operation.IsDone(): return body,None
    result=operation.Shape(); delta=m.volume(result)-before
    if (not m.BRepCheck_Analyzer(result).IsValid() or len(m.parts(result,m.TopAbs_SOLID))!=1
            or abs(delta)>=.001): return body,None
    if len(m.parts(result,m.TopAbs_FACE))==len(faces) and abs(delta)<1e-12: return body,None
    return result,dict(kind='micro-detail-group',seed_faces=len(seeds),selected_faces=len(selected),
                       before_faces=len(faces),after_faces=len(m.parts(result,m.TopAbs_FACE)),
                       before_volume_mm3=before,volume_change_mm3=delta)
