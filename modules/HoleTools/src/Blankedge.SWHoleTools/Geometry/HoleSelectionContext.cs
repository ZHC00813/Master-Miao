using System;
using System.Linq;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Blankedge.SWHoleTools.Utilities;
namespace Blankedge.SWHoleTools.Geometry
{
    public sealed class HoleSelectionContext
    {
        public IModelDoc2 Owner;
        public IComponent2 Component;
        public string Configuration;
        public readonly List<object> Items = new List<object>();
        public void Validate()
        {
            if (Component != null && (Component.IsSuppressed() || Component.ReferencedConfiguration != Configuration))
                throw new InvalidOperationException("零部件已压缩或切换配置，请重新扫描。");
            if (Owner.ConfigurationManager.ActiveConfiguration.Name != Configuration)
                throw new InvalidOperationException("零件配置与扫描时不一致，请先激活零件对应配置后重新扫描。");
        }
        public static List<HoleSelectionContext> Read(IModelDoc2 host)
        {
            if (!(host is IPartDoc) && !(host is IAssemblyDoc)) throw new InvalidOperationException("请打开零件或装配体。");
            var selection = (ISelectionMgr)host.SelectionManager;
            var result = new List<HoleSelectionContext>();
            for (int i = 1; i <= selection.GetSelectedObjectCount2(-1); i++)
            {
                object item = selection.GetSelectedObject6(i, -1);
                var component = host is IAssemblyDoc ? (item as IComponent2 ?? selection.GetSelectedObjectsComponent4(i, -1) as IComponent2) : null;
                if (host is IAssemblyDoc && component == null) throw new InvalidOperationException("请选择装配体内的零件、实体或面。");
                if (component != null && component.GetModelDoc2() == null)
                    component.SetSuppression2((int)swComponentSuppressionState_e.swComponentResolved);
                var owner = component == null ? host : component.GetModelDoc2() as IModelDoc2;
                if (!(owner is IPartDoc)) throw new InvalidOperationException("请选择具体零件；轻化零件请先设为还原，子装配体请展开后选择零件。");
                var context = result.FirstOrDefault(c => c.Component == null && component == null || c.Component != null && component != null && SwMath.GetComIdentity(c.Component) == SwMath.GetComIdentity(component));
                if (context == null)
                {
                    context = new HoleSelectionContext { Owner = owner, Component = component,
                        Configuration = component == null ? owner.ConfigurationManager.ActiveConfiguration.Name : component.ReferencedConfiguration };
                    context.Validate();
                    result.Add(context);
                }
                if (item is IComponent2)
                    context.Items.AddRange(SwMath.ToComList<IBody2>(((IPartDoc)owner).GetBodies2(0, false)).Cast<object>());
                else
                {
                    object native = component == null ? item : owner.Extension.GetCorresponding(item);
                    if (!(native is IFace2) && !(native is IBody2)) throw new InvalidOperationException("无法映射所选面或实体到原零件，请重新选择零件或面。");
                    context.Items.Add(native);
                }
            }
            if (result.Count == 0) throw new InvalidOperationException("请先选择零件、实体或孔面，再扫描。");
            return result;
        }
    }
}
