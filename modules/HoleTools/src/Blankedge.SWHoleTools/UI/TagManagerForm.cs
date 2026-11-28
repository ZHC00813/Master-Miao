using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Blankedge.SWHoleTools.Model;
using Blankedge.SWHoleTools.Tags;

namespace Blankedge.SWHoleTools.UI
{
    public sealed class TagManagerForm : Form
    {
        private readonly HoleTagCatalog _catalog;
        private readonly ListBox _list = new ListBox();
        private readonly ComboBox _filter=new ComboBox {Dock=DockStyle.Top, DropDownStyle=ComboBoxStyle.DropDownList};
        private readonly ComboBox _group=new ComboBox {Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};
        private readonly TextBox _name = new TextBox();
        private readonly ComboBox _type = new ComboBox();
        private readonly TextBox _standard = new TextBox();
        private readonly TextBox _size = new TextBox();
        private readonly NumericUpDown _pitch = new NumericUpDown();
        private readonly TextBox _tolerance = new TextBox();
        private readonly TextBox _annotation = new TextBox();
        private readonly Panel _color = new Panel();
        private readonly ToolTip _colorTip = new ToolTip();
        private bool _loading;

        public TagManagerForm(HoleTagCatalog catalog) : this(catalog, null) { }

        internal TagManagerForm(HoleTagCatalog catalog, string initialGroup)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Text = "Blankedge 孔标签管理";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(760, 470);
            Size = new Size(820, 520);
            Font = new Font("Microsoft YaHei UI", 9F);
            BuildUi();
            RefreshList(null, initialGroup);
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(12) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 235));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(root);

            var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 2 };
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            _list.Dock = DockStyle.Fill;
            _list.DisplayMember = nameof(HoleTagDefinition.DisplayName);
            _list.SelectedIndexChanged += (sender, args) => LoadSelection();
            var listPanel=new Panel { Dock=DockStyle.Fill };
            var newGroup = new Button {Text="＋ 新建分组",Dock=DockStyle.Top};
            newGroup.Click+=(s,e)=> { var name = PromptGroupName(this); if (name != null) RefreshList(null, _catalog.AddGroup(name)); };
            listPanel.Controls.Add(_list); listPanel.Controls.Add(newGroup); listPanel.Controls.Add(_filter);
            left.Controls.Add(listPanel,0,0);
            _filter.SelectionChangeCommitted+=(s,e)=>RefreshList();
            left.SetColumnSpan(listPanel, 2);
            var add = new Button { Text = "新建", Dock = DockStyle.Fill };
            add.Click += (sender, args) => AddTag();
            var delete = new Button { Text = "删除", Dock = DockStyle.Fill };
            delete.Click += (sender, args) => DeleteTag();
            left.Controls.Add(add, 0, 1);
            left.Controls.Add(delete, 1, 1);
            var location = new Label { Text = "配置文件：" + _catalog.FilePath, Dock = DockStyle.Fill, AutoEllipsis = true };
            left.Controls.Add(location, 0, 2);
            left.SetColumnSpan(location, 2);
            root.Controls.Add(left, 0, 0);

            var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 11, AutoScroll=true, Padding = new Padding(15, 0, 0, 0) };
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _type.DropDownStyle = ComboBoxStyle.DropDownList;
            _type.Items.AddRange(Enum.GetNames(typeof(HoleSemanticType)));
            _pitch.DecimalPlaces = 3;
            _pitch.Maximum = 100;
            _pitch.Increment = 0.05M;
            _color.Height = 28;
            _color.BorderStyle = BorderStyle.FixedSingle;
            var chooseColor = new Button { Text = "选择颜色…", Dock = DockStyle.Right, Width = 110 };
            chooseColor.Click += (sender, args) => ChooseColor();
            var autoColor = new Button { Text = "自动分配", Dock = DockStyle.Right, Width = 90 };
            autoColor.Click += (sender, args) =>
            {
                if (!(_list.SelectedItem is HoleTagDefinition tag)) return;
                _catalog.AssignAvailableColor(tag);
                LoadSelection();
            };
            var colorHost = new Panel { Dock = DockStyle.Fill, Height = 30 };
            _color.Dock = DockStyle.Fill;
            colorHost.Controls.Add(_color);
            colorHost.Controls.Add(chooseColor);
            colorHost.Controls.Add(autoColor);
            chooseColor.BringToFront();
            AddRow(editor, 0, "标签名称", _name);
            AddRow(editor, 1, "孔类型", _type);
            AddRow(editor, 2, "螺纹标准", _standard);
            AddRow(editor, 3, "螺纹规格", _size);
            AddRow(editor, 4, "螺距 (mm)", _pitch);
            AddRow(editor, 5, "公差", _tolerance);
            AddRow(editor, 6, "颜色", colorHost);
            AddRow(editor, 7, "标注模板", _annotation);
            AddRow(editor,8,"所属分组",_group);
            _group.SelectionChangeCommitted+=(s,e)=>
            {
                if (_list.SelectedItem is HoleTagDefinition tag && _group.SelectedItem is string group)
                {
                    tag.GroupName=group;
                    RefreshList(tag);
                }
            };
            editor.Controls.Add(new Label { Text = "可用变量：{Tag} {Thread} {Tolerance} {Termination}", AutoSize = true, ForeColor = Color.DimGray }, 1, 9);
            var save = new Button { Text = "保存全部标签", Dock = DockStyle.Right, Width = 145, Height = 34 };
            save.Click += (sender, args) => SaveCatalog();
            editor.Controls.Add(save, 1, 10);
            root.Controls.Add(editor, 1, 0);

            foreach (Control control in new Control[] { _name, _type, _standard, _size, _pitch, _tolerance, _annotation })
            {
                if (control is TextBox box) box.TextChanged += (sender, args) => StoreSelection();
                if (control is ComboBox combo) combo.SelectedIndexChanged += (sender, args) => StoreSelection();
                if (control is NumericUpDown number) number.ValueChanged += (sender, args) => StoreSelection();
            }
        }

        private static void AddRow(TableLayoutPanel panel, int row, string label, Control control)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            panel.Controls.Add(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
            control.Dock = DockStyle.Fill;
            panel.Controls.Add(control, 1, row);
        }

        internal static string PromptGroupName(Control owner)
        {
            using(var dialog=new Form { Text="新建分组",StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,
                ClientSize=new Size(340,100),Font=owner.Font,MinimizeBox=false,MaximizeBox=false })
            {
                var name=new TextBox {Left=15,Top=15,Width=310,AccessibleName="组名"};
                var create=new Button {Text="创建",Left=155,Top=58,DialogResult=DialogResult.OK,Enabled=false};
                var cancel=new Button {Text="取消",Left=248,Top=58,DialogResult=DialogResult.Cancel};
                name.TextChanged+=(s,e)=>create.Enabled=!string.IsNullOrWhiteSpace(name.Text);
                dialog.Controls.AddRange(new Control[]{name,create,cancel});
                dialog.AcceptButton=create;dialog.CancelButton=cancel;
                return dialog.ShowDialog(owner)==DialogResult.OK ? name.Text.Trim() : null;
            }
        }

        private void RefreshList(HoleTagDefinition selected = null,string requestedGroup=null)
        {
            _loading = true;
            string current=requestedGroup ?? selected?.Group ?? _filter.SelectedItem as string ?? _catalog.Items.FirstOrDefault()?.Group ?? "公制";
            var groups=_catalog.GroupNames;
            _filter.Items.Clear();_filter.Items.AddRange(groups);_filter.SelectedItem=groups.FirstOrDefault(g=>string.Equals(g,current,StringComparison.OrdinalIgnoreCase)) ?? groups[0];
            _group.Items.Clear();_group.Items.AddRange(groups);
            _list.Items.Clear();
            foreach (HoleTagDefinition item in _catalog.Items.Where(t=>t.InGroup(_filter.Text)).OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                _list.Items.Add(item);
            }
            _list.SelectedItem = selected ?? _list.Items.Cast<object>().FirstOrDefault();
            _loading = false;
            LoadSelection();
        }

        private void LoadSelection()
        {
            if (_loading) return;
            var tag=_list.SelectedItem as HoleTagDefinition;
            foreach(Control control in new Control[]{_name,_type,_standard,_size,_pitch,_tolerance,_annotation,_group,_color.Parent})
                control.Enabled=tag!=null;
            if(tag==null)
            {
                _name.Clear();_standard.Clear();_size.Clear();_annotation.Clear();_tolerance.Clear();
                _type.SelectedIndex=-1;_pitch.Value=0;_group.SelectedIndex=-1;_color.BackColor=SystemColors.Control;
                return;
            }
            _loading = true;
            _group.Text=tag.Group;
            _name.Text = tag.Name ?? string.Empty;
            _type.SelectedItem = tag.HoleType.ToString();
            _standard.Text = tag.ThreadStandard ?? string.Empty;
            _size.Text = tag.ThreadSize ?? string.Empty;
            _pitch.Value = Math.Max(_pitch.Minimum, Math.Min(_pitch.Maximum, (decimal)tag.PitchMillimeters));
            _tolerance.Text = tag.Tolerance ?? string.Empty;
            _annotation.Text = tag.AnnotationTemplate ?? string.Empty;
            _color.BackColor = Color.FromArgb(Clamp(tag.Red), Clamp(tag.Green), Clamp(tag.Blue));
            _colorTip.SetToolTip(_color, string.Format("RGB {0}, {1}, {2}", tag.Red, tag.Green, tag.Blue));
            _loading = false;
        }

        private void StoreSelection()
        {
            if (_loading || !(_list.SelectedItem is HoleTagDefinition tag)) return;
            tag.Name = _name.Text.Trim();
            if (Enum.TryParse(_type.Text, out HoleSemanticType parsed)) tag.HoleType = parsed;
            tag.ThreadStandard = _standard.Text.Trim();
            tag.ThreadSize = _size.Text.Trim();
            tag.PitchMillimeters = (double)_pitch.Value;
            tag.Tolerance = _tolerance.Text.Trim();
            tag.AnnotationTemplate = _annotation.Text;
        }

        private void AddTag()
        {
            StoreSelection();
            var tag = new HoleTagDefinition { GroupName=_filter.Text, Name = "新标签 " + (_catalog.Items.Count + 1).ToString(CultureInfo.InvariantCulture) };
            _catalog.AssignAvailableColor(tag);
            _catalog.Items.Add(tag);
            RefreshList(tag);
        }

        private void DeleteTag()
        {
            if (!(_list.SelectedItem is HoleTagDefinition tag)) return;
            _catalog.Items.Remove(tag);
            RefreshList();
        }

        private void ChooseColor()
        {
            if (!(_list.SelectedItem is HoleTagDefinition tag)) return;
            using (var dialog = new ColorDialog { Color = _color.BackColor, FullOpen = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                tag.Red = dialog.Color.R;
                tag.Green = dialog.Color.G;
                tag.Blue = dialog.Color.B;
                _color.BackColor = dialog.Color;
            }
        }

        private void SaveCatalog()
        {
            StoreSelection();
            if (_catalog.Items.Any(item => string.IsNullOrWhiteSpace(item.Name)))
            {
                MessageBox.Show(this, "每个标签都必须有名称。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_catalog.Items.GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            {
                MessageBox.Show(this, "标签名称不能重复。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                _catalog.Save();
                MessageBox.Show(this, "标签已保存。重新打开孔属性页即可看到更新。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshList(_list.SelectedItem as HoleTagDefinition);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败：" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static int Clamp(int value) => Math.Max(0, Math.Min(255, value));
    }
}

