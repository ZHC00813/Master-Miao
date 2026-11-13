using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Blankedge.SWHoleTools.Model;
using Blankedge.SWHoleTools.Standards;
using Blankedge.SWHoleTools.Tags;

namespace Blankedge.SWHoleTools.UI
{
    public sealed class HoleMarkDialog : Form
    {
        private readonly HoleDefinition _hole;
        private HoleTagDefinition[] _tags;
        private readonly ComboBox _tag = Choices();
        private readonly ComboBox _type = Choices();
        private readonly ComboBox _color = Choices();
        private readonly ComboBox _standard = new ComboBox();
        private readonly ComboBox _size = new ComboBox();
        private readonly TextBox _tolerance = new TextBox();
        private readonly TextBox _annotation = new TextBox();
        private readonly NumericUpDown _pitch = LengthField();
        private readonly NumericUpDown _threadDepth = LengthField();
        private bool _keepExistingTag;

        public HoleMarkDialog(HoleDefinition hole, string evidence, HoleTagCatalog catalog, HoleStandardDatabase standards)
        {
            _hole = hole;
            Text = "喵师傅孔标注";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.Font;
            MinimumSize = new Size(430, 480);
            ClientSize = new Size(520, 530);

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12), ColumnCount = 2 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(grid);
            var summary = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Height = 82, Text = hole.ToDisplaySummary() + Environment.NewLine + evidence };
            AddRow(grid, "孔识别结果", summary);

            var group=Choices();
            group.Items.AddRange(catalog.GroupNames);
            group.SelectedItem=catalog.Find(hole.TagId)?.Group ?? catalog.Items.FirstOrDefault()?.Group ?? "公制";
            AddRow(grid,"标签分组",group);
            _tags = catalog.Items.Where(t=>t.InGroup(group.Text)).OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            _tag.Items.Add("不使用标签");
            _tag.Items.AddRange(_tags.Select(t => t.DisplayName).ToArray());
            int index = Array.FindIndex(_tags, t => string.Equals(t.Id, hole.TagId, StringComparison.OrdinalIgnoreCase));
            if (index < 0 && !string.IsNullOrEmpty(hole.TagId))
            {
                _keepExistingTag = true;
                _tag.Items.Add("保留已有标签 · " + hole.TagName);
                _tag.SelectedIndex = _tag.Items.Count - 1;
            }
            else _tag.SelectedIndex = index + 1;
            group.SelectionChangeCommitted+=(s,e)=>
            {
                _tags=catalog.Items.Where(t=>t.InGroup(group.Text)).OrderBy(t=>t.Name).ToArray();
                _tag.Items.Clear();_tag.Items.Add("不使用标签");_tag.Items.AddRange(_tags.Select(t=>t.DisplayName).ToArray());
                int current=Array.FindIndex(_tags,t=>t.Id==hole.TagId);
                _keepExistingTag=current<0 && !string.IsNullOrEmpty(hole.TagId);
                if(_keepExistingTag) _tag.Items.Add("保留已有标签 · "+hole.TagName);
                _tag.SelectedIndex=_keepExistingTag?_tag.Items.Count-1:current+1;
                LoadFields(hole);
            };
            _type.DataSource = Enum.GetValues(typeof(HoleSemanticType));
            _color.DataSource = Enum.GetValues(typeof(HoleColorCode));
            _standard.Items.Add("ISO Metric");
            _size.Items.AddRange(standards.Metric.Select(t => t.Size).ToArray());
            _size.SelectionChangeCommitted += (sender, args) =>
            {
                var selected = standards.FindMetric(_size.SelectedItem as string);
                if (selected != null) SetLength(_pitch, selected.StandardPitchMm);
            };
            AddRow(grid, "标签", _tag);
            AddRow(grid, "孔类型", _type);
            AddRow(grid, "螺纹标准", _standard);
            AddRow(grid, "螺纹规格", _size);
            AddRow(grid, "螺距 (mm)", _pitch);
            AddRow(grid, "公差", _tolerance);
            AddRow(grid, "螺纹深度 (mm)", _threadDepth);
            AddRow(grid, "标注文字", _annotation);
            AddRow(grid, "颜色", _color);

            LoadFields(hole);
            _tag.SelectionChangeCommitted += (sender, args) =>
            {
                var selected = SelectedTag();
                if (selected != null)
                {
                    var values = new HoleDefinition { TerminationType = _hole.TerminationType, ThreadDepth = (double)_threadDepth.Value / 1000 };
                    selected.ApplyTo(values);
                    LoadFields(values);
                }
                else if (_keepExistingTag && _tag.SelectedIndex > _tags.Length) LoadFields(_hole);
                _color.Enabled = _tag.SelectedIndex == 0;
            };

            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var apply = new Button { Text = "应用标记", AutoSize = true, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(apply);
            buttons.Controls.Add(cancel);
            grid.Controls.Add(buttons, 0, grid.RowCount++);
            grid.SetColumnSpan(buttons, 2);
            AcceptButton = apply;
            CancelButton = cancel;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel || DialogResult != DialogResult.OK) return;
            SelectedTag()?.ApplyTo(_hole);
            if (_tag.SelectedIndex == 0)
            {
                _hole.TagId = string.Empty;
                _hole.TagName = string.Empty;
            }
            _hole.HoleType = (HoleSemanticType)_type.SelectedItem;
            _hole.ColorCode = (HoleColorCode)_color.SelectedItem;
            _hole.ThreadStandard = _standard.Text.Trim();
            _hole.ThreadSize = _size.Text.Trim();
            _hole.Pitch = (double)_pitch.Value / 1000;
            _hole.ThreadDepth = (double)_threadDepth.Value / 1000;
            _hole.Tolerance = _tolerance.Text.Trim();
            _hole.AnnotationText = _annotation.Text.Trim();
        }

        private HoleTagDefinition SelectedTag() =>
            _tag.SelectedIndex > 0 && _tag.SelectedIndex <= _tags.Length ? _tags[_tag.SelectedIndex - 1] : null;

        private void LoadFields(HoleDefinition values)
        {
            _type.SelectedItem = values.HoleType;
            _color.SelectedItem = values.ColorCode;
            _color.Enabled = _tag.SelectedIndex == 0;
            _standard.Text = values.ThreadStandard;
            _size.Text = values.ThreadSize;
            _tolerance.Text = values.Tolerance;
            _annotation.Text = values.AnnotationText;
            SetLength(_pitch, values.Pitch * 1000);
            SetLength(_threadDepth, values.ThreadDepth * 1000);
        }

        private static NumericUpDown LengthField() =>
            new NumericUpDown { DecimalPlaces = 4, Increment = 0.05m, Maximum = 1000000 };

        private static void SetLength(NumericUpDown control, double value) =>
            control.Value = (decimal)Math.Min((double)control.Maximum, Math.Max(0, value));

        private static ComboBox Choices() => new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };

        private static void AddRow(TableLayoutPanel grid, string title, Control field)
        {
            int row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(new Label { Text = title, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            field.Dock = DockStyle.Fill;
            grid.Controls.Add(field, 1, row);
        }
    }
}
