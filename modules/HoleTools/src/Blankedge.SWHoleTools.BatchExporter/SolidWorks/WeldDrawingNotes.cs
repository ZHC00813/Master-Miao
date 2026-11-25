using System;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Blankedge.SWHoleTools.Utilities;
namespace Blankedge.SWHoleTools.BatchExporter.SolidWorks
{
    internal static class WeldDrawingNotes
    {
        public static void CreateLabels(IModelDoc2 drawing, IView view)
        {
            var outline = (double[])view.GetOutline();
            int index = 0;
            foreach (var group in SwMath.ToComList<IComponent2>(view.GetVisibleComponents()).GroupBy(c => c.GetPathName()+"|"+c.ReferencedConfiguration))
            {
                bool attached = false;
                foreach (var component in group)
                {
                    foreach (var edge in SwMath.ToComList<IEntity>(view.GetVisibleEntities2((Component2)component,(int)swViewEntityType_e.swViewEntityType_Edge)))
                    {
                        drawing.ClearSelection2(true);
                        if (!view.SelectEntity(edge,false)) continue;
                        var note = drawing.InsertNote(Path.GetFileNameWithoutExtension(component.GetPathName())) as INote;
                        var annotation = note?.GetAnnotation() as IAnnotation;
                        if (annotation == null) throw new InvalidOperationException("焊接零件名称标注失败。");
                        bool left = index % 2 == 0;
                        annotation.SetLeader3((int)swLeaderStyle_e.swBENT,(int)swLeaderSide_e.swLS_SMART,true,false,false,false);
                        annotation.SetPosition2(left ? Math.Max(.035,outline[0]-.06) : outline[2]+.015, outline[3]-(index/2)*.018,0);
                        index++;
                        attached = true;
                        break;
                    }
                    if (attached) break;
                }
                if (!attached) throw new InvalidOperationException("无法为焊接组成零件附着名称引线："+group.Key);
            }
            drawing.ClearSelection2(true);
            if (index == 0) throw new InvalidOperationException("焊接视图没有可标注的组成零件。");
        }
    }
}
