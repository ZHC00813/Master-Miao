import sys,pathlib,math,subprocess,json,argparse,tempfile,os
parser=argparse.ArgumentParser(description='Portable PCB module behavioral smoke test')
parser.add_argument('--ocp-path',type=pathlib.Path)
args=parser.parse_args()
root=pathlib.Path(__file__).resolve().parents[1]
if args.ocp_path: sys.path.insert(0,str(args.ocp_path.resolve()))
os.environ['PYTHONUTF8']='1'
os.environ['PYTHONIOENCODING']='utf-8'
from OCP.TDocStd import TDocStd_Document
from OCP.TCollection import TCollection_ExtendedString
from OCP.XCAFDoc import XCAFDoc_DocumentTool
from OCP.TDataStd import TDataStd_Name
from OCP.TopLoc import TopLoc_Location
from OCP.BRepPrimAPI import BRepPrimAPI_MakeBox
from OCP.BRepPrimAPI import BRepPrimAPI_MakeCylinder
from OCP.BRepAlgoAPI import BRepAlgoAPI_Cut,BRepAlgoAPI_Common
from OCP.BRepGProp import BRepGProp
from OCP.GProp import GProp_GProps
from OCP.BRepTools import BRepTools
from OCP.BRep import BRep_Builder
from OCP.TopoDS import TopoDS_Shape
from OCP.gp import gp_Trsf,gp_Ax1,gp_Pnt,gp_Dir,gp_Vec
from OCP.STEPCAFControl import STEPCAFControl_Writer
from OCP.STEPControl import STEPControl_AsIs
from OCP.IFSelect import IFSelect_RetDone
from OCP.BRepBndLib import BRepBndLib
from OCP.Bnd import Bnd_Box

temporary=tempfile.TemporaryDirectory(prefix='pcb-simplifier-test-')
task=pathlib.Path(temporary.name)
doc=TDocStd_Document(TCollection_ExtendedString('Test'))
st=XCAFDoc_DocumentTool.ShapeTool_s(doc.Main())
inner=st.NewShape(); TDataStd_Name.Set_s(inner,TCollection_ExtendedString('PCB assembly'))
expected={}
global_tr=gp_Trsf(); global_tr.SetRotation(gp_Ax1(gp_Pnt(0,0,0),gp_Dir(1,1,1)),math.radians(31)); global_tr.SetTranslationPart(gp_Vec(100,-50,20))
def bnd(s):
 b=Bnd_Box(); BRepBndLib.AddOptimal_s(s,b,False,False); p,q=b.CornerMin(),b.CornerMax(); return [p.X(),p.Y(),p.Z(),q.X(),q.Y(),q.Z()]
for name,dim,pos,angle in [('Board',(40,30,1.6),(0,0,0),0),('USB1',(5,4,2),(2,10,1.6),0),('C1',(4,4,3),(25,20,1.6),0),('R1',(1.6,.8,.8),(15,15,1.6),43)]:
 shape=BRepPrimAPI_MakeBox(*dim).Shape()
 if name=='Board':
  from OCP.gp import gp_Ax2
  hole=BRepPrimAPI_MakeCylinder(gp_Ax2(gp_Pnt(5,5,-1),gp_Dir(0,0,1)),1.5,4).Shape()
  shape=BRepAlgoAPI_Cut(shape,hole).Shape()
 definition=st.AddShape(shape,False); TDataStd_Name.Set_s(definition,TCollection_ExtendedString(name))
 tr=gp_Trsf(); tr.SetRotation(gp_Ax1(gp_Pnt(0,0,0),gp_Dir(0,0,1)),math.radians(angle)); tr.SetTranslationPart(gp_Vec(*pos))
 instance=st.AddComponent(inner,definition,TopLoc_Location(tr)); TDataStd_Name.Set_s(instance,TCollection_ExtendedString(name))
 expected[name]=bnd(shape.Moved(TopLoc_Location(global_tr.Multiplied(tr))))
outer=st.NewShape(); TDataStd_Name.Set_s(outer,TCollection_ExtendedString('Rotated wrapper'))
st.AddComponent(outer,inner,TopLoc_Location(global_tr)); st.UpdateAssemblies()
w=STEPCAFControl_Writer(); w.SetNameMode(True); assert w.Transfer(doc,STEPControl_AsIs); fixture=task/'rotated.step'; assert w.Write(str(fixture))==IFSelect_RetDone
script=root/'scripts/pcb_simplify.py'
base=[sys.executable,str(script)]
if args.ocp_path: base += ['--ocp-path',str(args.ocp_path.resolve())]
analysis=task/'analysis'
subprocess.run(base+['analyze',str(fixture),'--work',str(analysis),'--keep-largest','1'],check=True)
plan=json.loads((analysis/'plan.json').read_text(encoding='utf-8'))
for row in plan['components']:
 name=row['name']; assert max(abs(a-b) for a,b in zip(row['bbox_mm'],expected[name]))<1e-5, (name,row['bbox_mm'],expected[name])
 assert not row['review']
assert {r['name']:r['action'] for r in plan['components']}=={'Board':'keep','USB1':'keep','C1':'keep','R1':'envelope'}
# A rotated small part must keep its local dimensions instead of using world AABB.
res=next(r for r in plan['components'] if r['name']=='R1'); assert max(abs(a-b) for a,b in zip(res['dimensions_mm'],[1.6,.8,.8]))<1e-7
output=task/'rotated_light.stp'; subprocess.run(base+['build','--work',str(analysis),'--output',str(output)],check=True)
report=json.loads((analysis/'report.json').read_text()); assert report['solid_count']==1 and report['valid']
final=TopoDS_Shape(); BRepTools.Read_s(final,str(analysis/'result.brep'),BRep_Builder())
check=BRepAlgoAPI_Common(final,hole.Moved(TopLoc_Location(global_tr))).Shape(); p=GProp_GProps(); BRepGProp.VolumeProperties_s(check,p); assert abs(p.Mass())<1e-7, 'Mounting hole was filled'
# Unknown/reviewed geometry must block export; no final file may be produced.
plan['components'][1]['review']=True; (analysis/'plan.json').write_text(json.dumps(plan),encoding='utf-8')
blocked=task/'must_not_exist.stp'; result=subprocess.run(base+['build','--work',str(analysis),'--output',str(blocked)])
assert result.returncode!=0 and not blocked.exists()
print('PASS: nested transforms, local rotated envelopes, connector protection, size ranking, mounting-hole preservation, compact STEP single-solid round trip, unresolved-review export gate',flush=True)

# Exercise the portable host-module adapter independently.
sys.path.insert(0,str(root/'examples'))
from module_adapter import analyze,build
adapter_work=task/'adapter'
adapter_plan=analyze(fixture,adapter_work,keep_largest=1,ocp_path=args.ocp_path)
assert all(not r['review'] for r in adapter_plan['components'])
adapter_report=build(adapter_work,task/'adapter_result.stp',ocp_path=args.ocp_path)
assert adapter_report['valid'] and adapter_report['solid_count']==1
print('PASS: module_adapter analyze/build integration',flush=True)
temporary.cleanup()
