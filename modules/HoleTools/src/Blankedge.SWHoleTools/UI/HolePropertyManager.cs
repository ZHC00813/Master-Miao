using System;
using System.Linq;
using System.Windows.Forms;
using Blankedge.SWHoleTools.Appearance;
using Blankedge.SWHoleTools.Attributes;
using Blankedge.SWHoleTools.Drawing;
using Blankedge.SWHoleTools.Model;
using Blankedge.SWHoleTools.Standards;
using Blankedge.SWHoleTools.Tags;
using Blankedge.SWHoleTools.Utilities;
using SolidWorks.Interop.sldworks;

namespace Blankedge.SWHoleTools.UI
{
    public sealed class HolePropertyManager
    {
        private readonly ISldWorks _application;
        private readonly HoleAttributeManager _attributes;
        private readonly HoleColorManager _colors;
        private readonly HoleStandardDatabase _standards;
        private readonly HoleTagCatalog _catalog;
        private readonly HoleAnnotationGenerator _generator;
        private readonly Action<string> _notify;

        public HolePropertyManager(ISldWorks application, HoleAttributeManager attributes, HoleColorManager colors,
            HoleStandardDatabase standards, HoleTagCatalog catalog, HoleAnnotationGenerator generator, Action<string> notify)
        {
            _application = application;
            _attributes = attributes;
            _colors = colors;
            _standards = standards;
            _catalog = catalog;
            _generator = generator;
            _notify = notify;
        }

        public bool Show(IModelDoc2 model, HoleAnalysis analysis, HoleDefinition existing)
        {
            try
            {
                var hole = analysis.Definition;
                if (existing != null)
                    MergeUserSemantics(existing, hole);
                var owner = new WindowOwner(new IntPtr(_application.IFrameObject().GetHWndx64()));
                using (var dialog = new HoleMarkDialog(hole, analysis.Evidence, _catalog, _standards))
                {
                    dialog.Shown += (sender, args) => Logger.Info("独立孔标注窗口已显示。");
                    if (dialog.ShowDialog(owner) != DialogResult.OK)
                    {
                        Logger.Info("关闭独立孔标注窗口，已取消。");
                        return true;
                    }
                }
                if (string.IsNullOrWhiteSpace(hole.AnnotationText))
                    hole.AnnotationText = _generator.Generate(hole);
                Logger.Info("开始写入孔标记。");
                var saved = _attributes.Save(model, analysis.Group, hole);
                if (!saved.Success)
                {
                    _notify(saved.Error);
                    return true;
                }
                bool colored = string.IsNullOrEmpty(hole.TagId)
                    ? _colors.Apply(analysis.Group, hole.ColorCode, model)
                    : _colors.ApplyRgb(analysis.Group, hole.ColorRed, hole.ColorGreen, hole.ColorBlue, model);
                Logger.Info("孔标记 " + hole.HoleID + "，面颜色写入：" + colored);
                if (!colored) _notify("孔属性已写入，但孔面上色失败。");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("打开或应用孔标注失败。", ex);
                return false;
            }
        }

        private sealed class WindowOwner : IWin32Window
        {
            public WindowOwner(IntPtr handle) { Handle = handle; }
            public IntPtr Handle { get; }
        }

        private static void MergeUserSemantics(HoleDefinition source, HoleDefinition target)
        {
            target.HoleID = source.HoleID;
            target.HoleType = source.HoleType;
            target.ThreadStandard = source.ThreadStandard;
            target.ThreadSize = source.ThreadSize;
            target.Pitch = source.Pitch;
            target.Tolerance = source.Tolerance;
            target.ThreadDepth = source.ThreadDepth;
            target.AnnotationText = source.AnnotationText;
            target.ColorCode = source.ColorCode;
            target.TagId = source.TagId;
            target.TagName = source.TagName;
            target.ColorRed = source.ColorRed;
            target.ColorGreen = source.ColorGreen;
            target.ColorBlue = source.ColorBlue;
        }
    }
}
