using System;
using System.Linq;
using System.Collections.Generic;
using Blankedge.SWHoleTools.Attributes;
using Blankedge.SWHoleTools.Appearance;
using Blankedge.SWHoleTools.Model;
using Blankedge.SWHoleTools.Tags;
using Blankedge.SWHoleTools.Utilities;
using SolidWorks.Interop.sldworks;
namespace Blankedge.SWHoleTools.Geometry
{
    public sealed class ManualHoleService
    {
        public static bool IsManual(HoleDefinition hole) => hole != null && (hole.HoleID ?? "").StartsWith("MH-", StringComparison.Ordinal);
        public static HoleGroup Group(IEnumerable<IFace2> faces)
        {
            var list = faces.GroupBy(SwMath.GetComIdentity).Select(g => g.First()).ToList();
            if (list.Count == 0) throw new InvalidOperationException("请先选择要合为一个孔的面。");
            var body = list[0].GetBody() as IBody2;
            if (body == null || list.Any(f => SwMath.GetComIdentity(f.GetBody()) != SwMath.GetComIdentity(body)))
                throw new InvalidOperationException("一个手动孔的所有面必须属于同一实体，请分次添加。");
            var group = new HoleGroup { Body = body, SelectedFace = list[0], PrimaryCylinderFace = list[0] };
            foreach (var face in list) group.ManualFaces.Add(face);
            return group;
        }
        public static HoleDefinition Create(ISldWorks app, IModelDoc2 doc, IEnumerable<IFace2> faces, HoleTagDefinition tag)
        {
            if (!(doc is IPartDoc)) throw new InvalidOperationException("请在目标零件文档中添加手动孔。");
            if (tag == null) throw new InvalidOperationException("请先选择标签。");
            var group = Group(faces);
            var attributes = new HoleAttributeManager(app);
            if (group.ManualFaces.Any(f => attributes.Read(f) != null))
                throw new InvalidOperationException("所选面已有孔标注。为保留原标注，请从孔列表修改标签，或选择未标注的面。");
            var refs = new PersistentReferenceManager();
            if (group.ManualFaces.Any(f => string.IsNullOrEmpty(refs.Create(doc, f))))
                throw new InvalidOperationException("无法保存所选面的引用，请在所属零件中重新选择。");
            var previousColors = group.ManualFaces.Select(f => f.MaterialPropertyValues as double[]).ToArray();
            var hole = new HoleDefinition { HoleID = "MH-" + Guid.NewGuid().ToString("N"), BodyID = refs.Create(doc, group.Body), FacePersistentReference = refs.Create(doc, group.SelectedFace) };
            tag.ApplyTo(hole);
            if (string.IsNullOrWhiteSpace(hole.AnnotationText)) hole.AnnotationText = tag.Name;
            try
            {
                var saved = attributes.Save(doc, group, hole);
                if (!saved.Success || group.ManualFaces.Any(f => attributes.Read(f)?.HoleID != hole.HoleID))
                    throw new InvalidOperationException(saved.Error ?? "部分面未能保存为同一个孔。");
                if (!new HoleColorManager().ApplyRgb(group, tag.Red, tag.Green, tag.Blue, doc))
                    throw new InvalidOperationException("手动孔已写入，但上色失败，请重新应用标签。");
                foreach (var face in group.ManualFaces)
                {
                    var rgb = face.MaterialPropertyValues as double[];
                    if (rgb == null || rgb.Length < 3 || Math.Abs(rgb[0] - tag.Red / 255.0) > 1e-6 ||
                        Math.Abs(rgb[1] - tag.Green / 255.0) > 1e-6 || Math.Abs(rgb[2] - tag.Blue / 255.0) > 1e-6)
                        throw new InvalidOperationException("部分面上色失败，已撤销本次手动添加。");
                }
                return hole;
            }
            catch
            {
                attributes.Clear(group);
                for (int i = 0; i < group.ManualFaces.Count; i++)
                {
                    try
                    {
                        var color = previousColors[i];
                        if (color == null || color.Length < 3 || color[0] < 0)
                            group.ManualFaces[i].RemoveMaterialProperty2((int)SolidWorks.Interop.swconst.swInConfigurationOpts_e.swThisConfiguration, null);
                        else group.ManualFaces[i].SetMaterialPropertyValues2(color, (int)SolidWorks.Interop.swconst.swInConfigurationOpts_e.swThisConfiguration, null);
                    }
                    catch (Exception ex) { Logger.Error("恢复手动孔原面颜色失败。", ex); }
                }
                doc.GraphicsRedraw2();
                throw;
            }
        }
    }
}
