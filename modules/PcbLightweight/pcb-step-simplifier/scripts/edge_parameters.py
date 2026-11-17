"""Recompute inconsistent edge parameters without replacing component geometry."""
def normalize(body, m):
    from OCP.BRepBuilderAPI import BRepBuilderAPI_Copy
    from OCP.BRepLib import BRepLib
    from OCP.BRep import BRep_Tool
    from OCP.TopAbs import TopAbs_EDGE
    if m.BRepCheck_Analyzer(body, True, False, True).IsValid():
        return body, None
    candidate=BRepBuilderAPI_Copy(body,True,False).Shape()
    before=m.volume(body)
    BRepLib.SameParameter_s(candidate,1e-6,True)
    tolerance=max(BRep_Tool.Tolerance_s(m.TopoDS.Edge_s(e)) for e in m.parts(candidate,TopAbs_EDGE))
    delta=m.volume(candidate)-before
    if (not m.BRepCheck_Analyzer(candidate,True,False,True).IsValid() or tolerance>.001
        or abs(delta)>max(.001,abs(before)*1e-6)
        or max(abs(a-b) for a,b in zip(m.bbox(body,True),m.bbox(candidate,True)))>.001):
        return body, None
    return candidate,dict(kind='edge-parameterization',volume_change_mm3=delta,max_edge_tolerance_mm=tolerance)
