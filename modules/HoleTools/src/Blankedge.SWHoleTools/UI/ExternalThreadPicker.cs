using System;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using Blankedge.SWHoleTools.Model;
using Blankedge.SWHoleTools.Tags;

namespace Blankedge.SWHoleTools.UI
{
    internal static class ExternalThreadPicker
    {
        public static HoleTagDefinition Choose(Control owner, HoleTagDefinition selected)
        {
            var library = HoleTagCatalog.Load();
            var options = library.Items.Where(t => t.HoleType == HoleSemanticType.ExternalThread).OrderBy(t => t.Name).ToArray();
            using (var form = new Form { Text = "指定外螺纹标签", StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(440,135) })
            {
                var label = new Label { Left = 12, Top = 12, AutoSize = true, Text = "选择已有标签，或输入完整外螺纹规格（例如 M8×1-6g）。" };
                var choice = new ComboBox { Left = 12, Top = 42, Width = 416, DisplayMember = "Name" };
                choice.Items.AddRange(options);
                choice.SelectedItem = options.FirstOrDefault(t => t.Id == selected?.Id) ?? options.FirstOrDefault();
                var ok = new Button { Text = "应用", Left = 352, Top = 86, DialogResult = DialogResult.OK };
                form.Controls.AddRange(new Control[] { label, choice, ok });
                form.AcceptButton = ok;
                if (form.ShowDialog(owner) != DialogResult.OK) return null;
                string spec = choice.Text.Trim();
                if (spec.Length == 0) throw new InvalidOperationException("请输入规格或选择标签。");
                string name = spec.Contains("外螺纹") ? spec : spec + " 外螺纹";
                var match = options.FirstOrDefault(t => string.Equals(t.Name, spec, StringComparison.OrdinalIgnoreCase) || string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
                if (library.Items.Any(t => string.Equals(t.Name,name,StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("名称已被孔标签使用，请修改名称。");
                var tag = new HoleTagDefinition { Name = name, GroupName = "外螺纹", HoleType = HoleSemanticType.ExternalThread, ThreadSize = spec, ThreadStandard = "", Tolerance = "", AnnotationTemplate = "{Tag}" };
                library.AssignAvailableColor(tag);
                library.Items.Add(tag);
                library.Save();
                return tag;
            }
        }
    }
}
