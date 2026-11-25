using System;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Blankedge.SWHoleTools.Utilities;

namespace Blankedge.SWHoleTools.BatchExporter.SolidWorks
{
    internal static class WoodDrawingPreparation
    {
        public static void AlignCopy(IModelDoc2 model)
        {
            var part = (IPartDoc)model;
            var bodies = SwMath.ToComList<IBody2>(part.GetBodies2(0, false)).ToList();
            if (bodies.Count != 1) throw new InvalidOperationException("木板生产方向整理要求单实体零件。");
            var face = SwMath.ToComList<IFace2>(bodies[0].GetFaces()).Where(f => ((ISurface)f.GetSurface()).IsPlane()).OrderByDescending(f => f.GetArea()).FirstOrDefault();
            if (face == null) throw new InvalidOperationException("没有找到木板基准平面。");
            var n = (double[])face.Normal;
            if (n[1] < 0) n = n.Select(x => -x).ToArray();
            double rx = -Math.Atan2(n[2], n[1]);
            Rotate(model, rx, 0, 0);
            Rotate(model, 0, 0, Math.Atan2(n[0], Math.Sqrt(n[1]*n[1]+n[2]*n[2])));
            var box = (double[])part.GetPartBox(true);
            if (box[5]-box[2] > box[3]-box[0]) Rotate(model, 0, Math.PI/2, 0);
            model.EditRebuild3();
        }
        private static void Rotate(IModelDoc2 model, double x, double y, double z)
        {
            if (Math.Abs(x)+Math.Abs(y)+Math.Abs(z) < 1e-8) return;
            model.ClearSelection2(true);
            var data = ((ISelectionMgr)model.SelectionManager).CreateSelectData();
            data.Mark = 1;
            foreach (var body in SwMath.ToComList<IBody2>(((IPartDoc)model).GetBodies2(0,false)))
                if (!body.Select2(true, data)) throw new InvalidOperationException("不能选择木板副本实体。");
            var feature = model.FeatureManager.InsertMoveCopyBody2(0,0,0,0,0,0,0,x,y,z,false,1);
            model.ClearSelection2(true);
            if (feature == null) throw new InvalidOperationException("木板生产方向整理失败。");
            var definition = feature.GetDefinition() as IMoveCopyBodyFeatureData;
            if (definition == null || !definition.AccessSelections((ModelDoc2)model, null)) throw new InvalidOperationException("无法核对木板旋转参数。");
            definition.TransformType = 2;
            definition.TransformX = x;
            definition.TransformY = y;
            definition.TransformZ = z;
            if (!feature.ModifyDefinition(definition, model, null))
            {
                definition.ReleaseSelectionAccess();
                throw new InvalidOperationException("木板旋转参数写入失败。");
            }
            model.EditRebuild3();
        }
    }
}
