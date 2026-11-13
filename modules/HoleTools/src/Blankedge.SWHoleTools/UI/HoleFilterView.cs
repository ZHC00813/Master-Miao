using System;
using System.Drawing;
using System.Collections.Generic;
using Blankedge.SWHoleTools.Model;
using Blankedge.SWHoleTools.Utilities;
using SolidWorks.Interop.sldworks;
using System.Linq;
using System.Windows.Forms;
using Blankedge.SWHoleTools.Appearance;
using Blankedge.SWHoleTools.Attributes;


using Blankedge.SWHoleTools.Drawing;
using Blankedge.SWHoleTools.Geometry;
using Blankedge.SWHoleTools.Tags;
using Blankedge.SWHoleTools.UI;

namespace Blankedge.SWHoleTools.UI
{
    public class HoleFilterView : UserControl
    {
        private readonly ComboBox groups=new ComboBox {Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList,AccessibleName="标签分组"};
        private HoleTagCatalog catalog;
        private readonly ComboBox tags = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly Panel swatch = new Panel { Dock = DockStyle.Fill, Margin = new Padding(8) };
        private readonly Label rgb = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        private readonly TreeView holes = new TreeView { Dock = DockStyle.Fill, CheckBoxes = true, HideSelection = false };
        private IModelDoc2 scanned;
        private bool checking;
        private readonly List<Entry> entries = new List<Entry>();
        private readonly CheckBox grouped = new CheckBox { Text = "按孔型分组", AutoSize = true };
        private readonly TextBox filter = new TextBox { Width = 210 };
        private readonly CheckBox irregular = new CheckBox { Text = "识别异形孔", AutoSize = true, Checked = true };
        private readonly Label status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Func<ISldWorks> application;
        public HoleFilterView(Func<ISldWorks> application, Button installButton = null)
        {
            this.application = application;
            Dock = DockStyle.Fill;
            BackColor = Color.White;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 6 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.Controls.Add(new Label { Text = "孔标注助手", Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 15F, FontStyle.Bold), Dock = DockStyle.Fill }, 0, 0);
            var selection = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
            selection.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            selection.Controls.Add(groups,0,0);
            groups.SelectedIndexChanged+=(s,e)=>FilterTags();
            selection.Controls.Add(tags, 1, 0);
            selection.Controls.Add(swatch, 2, 0);
            selection.Controls.Add(rgb, 3, 0);
            root.Controls.Add(selection, 0, 1);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
            if (installButton != null) actions.Controls.Add(installButton);
            AddButton(actions, "扫描所选实体 / 面 / 孔", Scan);
            AddButton(actions, "应用标签并上色", Apply);
            AddButton(actions, "标签管理", ManageTags);
            root.Controls.Add(actions, 0, 2);
            var filters = new FlowLayoutPanel { Dock = DockStyle.Fill };
            filters.Controls.Add(grouped);
            filters.Controls.Add(irregular);
            irregular.CheckedChanged += (sender,e) => RenderEntries();
            filters.Controls.Add(new Label { Text = "筛选孔径 / 深度 / 孔型", AutoSize = true });
            filters.Controls.Add(filter);
            root.Controls.Add(filters, 0, 3);
            grouped.CheckedChanged += (sender, e) => RenderEntries();
            filter.TextChanged += (sender, e) => RenderEntries();
            root.Controls.Add(holes, 0, 4);
            holes.AfterCheck += (sender, e) =>
            {
                if (checking) return;
                checking = true;
                try
                {
                    if (e.Node.Tag is Entry item) item.Checked = e.Node.Checked;
                    foreach (TreeNode child in e.Node.Nodes)
                    {
                        child.Checked = e.Node.Checked;
                        ((Entry)child.Tag).Checked = e.Node.Checked;
                    }
                    if (e.Node.Parent != null)
                        e.Node.Parent.Checked = e.Node.Parent.Nodes.Cast<TreeNode>().All(n => n.Checked);
                }
                finally { checking = false; }
            };
            holes.AfterSelect += (sender, e) =>
            {
                try { Locate(e.Node); } catch (Exception ex) { status.Text = ex.Message; }
            };
            root.Controls.Add(status, 0, 5);
            Controls.Add(root);
            tags.SelectedIndexChanged += (sender, e) => UpdateColor();
            if (installButton == null)
            {
                root.Padding = new Padding(4);
                selection.ColumnStyles[0].Width = 50;
                selection.ColumnStyles[2].Width = 32;
                selection.ColumnStyles[3].Width = 0;
                actions.WrapContents = true;
                root.RowStyles[2].Height = 118;
                root.RowStyles[3].Height = 64;
            }
            ReloadTags();
        }
        private void AddButton(Control parent, string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(150, 34), Margin = new Padding(0, 4, 12, 4) };
            button.Click += (sender, e) => { try { action(); } catch (Exception ex) { status.Text = ex.Message; } };
            parent.Controls.Add(button);
        }
        public void ReloadTags()
        {
            var old=groups.SelectedItem as string;
            catalog=HoleTagCatalog.Load();
            var options=catalog.GroupNames;
            groups.DataSource=options;
            groups.SelectedItem=options.Contains(old)?old:catalog.Items.FirstOrDefault()?.Group ?? options[0];
            FilterTags();
        }

        private void FilterTags()
        {
            tags.DataSource=catalog.Items.Where(t=>t.InGroup(groups.SelectedItem as string)).OrderBy(t=>t.Name).ToList();
            tags.DisplayMember = "DisplayName";
            UpdateColor();
        }
        private void UpdateColor()
        {
            var tag = tags.SelectedItem as HoleTagDefinition;
            if (tag == null) return;
            swatch.BackColor = Color.FromArgb(Math.Max(0, Math.Min(255, tag.Red)), Math.Max(0, Math.Min(255, tag.Green)), Math.Max(0, Math.Min(255, tag.Blue)));
            rgb.Text = string.Format("RGB {0}, {1}, {2}", tag.Red, tag.Green, tag.Blue);
        }
        private void ManageTags()
        {
            using (var form = new TagManagerForm(HoleTagCatalog.Load())) form.ShowDialog(this);
            ReloadTags();
        }
        private sealed class Entry
        {
            public string Reference;
            public string Group;
            public bool Review, Checked;
            public int Number;
        }

        private void RenderEntries()
        {
            checking = true;
            holes.BeginUpdate();
            try
            {
                holes.Nodes.Clear();
                foreach (var items in entries.Where(item => (!item.Review || irregular.Checked) && item.Group.IndexOf(filter.Text.Trim(), StringComparison.OrdinalIgnoreCase) >= 0).GroupBy(item => item.Group))
                {
                    var parent = grouped.Checked ? holes.Nodes.Add(items.Key + "（" + items.Count() + "）") : null;
                    if (parent != null)
                    {
                        parent.Checked = items.All(item => item.Checked);
                        if (items.First().Review) parent.ForeColor = Color.DarkOrange;
                    }
                    foreach (var item in items)
                    {
                        var node = (parent?.Nodes ?? holes.Nodes).Add("孔 " + item.Number + (parent == null ? " · " + item.Group : ""));
                        node.Tag = item;
                        node.Checked = item.Checked;
                        if (item.Review) node.ForeColor = Color.DarkOrange;
                    }
                }
            }
            finally { holes.EndUpdate(); checking = false; }
        }

        private readonly PersistentReferenceManager references = new PersistentReferenceManager();
        private readonly HoleDetector detector = new HoleDetector(new PersistentReferenceManager());

        public void Scan()
        {
            holes.Nodes.Clear();
            entries.Clear();
            scanned = null;
            
            {
                var app = application();
                var doc = app.IActiveDoc2;
                if (!(doc is IPartDoc)) throw new InvalidOperationException("请打开零件并选择实体、平面或多个孔壁。");
                var selection = (ISelectionMgr)doc.SelectionManager;
                int count = selection.GetSelectedObjectCount2(-1);
                if (count == 0) throw new InvalidOperationException("请在 SW 选择一个实体、平面或多个孔壁，再扫描。");
                var faces = new List<IFace2>();
                var unusual = new List<IFace2>();
                for (int i = 1; i <= count; i++)
                {
                    var item = selection.GetSelectedObject6(i, -1);
                    if (item is IBody2 body)
                    {
                        var bodyFaces=SwMath.ToComList<IFace2>(body.GetFaces()).ToList();
                        faces.AddRange(bodyFaces);
                        foreach(var plane in bodyFaces.Where(f=>((ISurface)f.GetSurface()).IsPlane()))
                            CollectOpenings(plane,faces,unusual);
                    }
                    else if (item is IFace2 face && ((ISurface)face.GetSurface()).IsPlane())
                    {
                        CollectOpenings(face,faces,unusual);
                    }
                    else if (item is IFace2 selected) faces.Add(selected);
                    else throw new InvalidOperationException("请选择实体、平面或孔壁。");
                }
                var seen = new HashSet<long>();
                scanned = doc;
                int total = 0;
                foreach (var face in faces.Where(IsHoleWall).Concat(unusual))
                {
                    if (!seen.Add(SwMath.GetComIdentity(face))) continue;
                    var result = detector.AnalyzeFace(doc, face);
                    string key = "待复核开口";
                    bool review = !result.Success;
                    IFace2 target = face;
                    if (result.Success)
                    {
                        var a = result.Value;
                        foreach (var c in a.Group.Cylinders) seen.Add(SwMath.GetComIdentity(c.Face));
                        foreach (var c in a.Group.Cones) seen.Add(SwMath.GetComIdentity(c.Face));
                        var diameters = a.Group.Cylinders.Select(c => Math.Round(c.Geometry.Radius * 2000, 4)).Distinct().OrderBy(x => x).ToList();
                        bool drillPoint = a.Group.Cones.Count == 1 && a.Definition.TerminationType == TerminationType.Blind &&
                            a.Definition.BottomType == HoleBottomType.Conical && !a.Definition.HasCountersink;
                        bool special = diameters.Count > 1 || (a.Group.Cones.Count > 0 && !drillPoint);
                        review = special || a.Definition.TerminationType == TerminationType.Ambiguous;
                        var h = a.Definition;
                        key = (special ? "复合孔 · " : "") + "Ø" + string.Join(" / Ø", diameters) + " mm · " +
                            (h.TerminationType == TerminationType.Through ? "通孔 · " + (h.ThroughLength * 1000).ToString("0.####") + " mm" :
                             h.TerminationType == TerminationType.Blind ? "盲孔 · " + (h.MeasuredDepth * 1000).ToString("0.####") + " mm" : "深度待复核");
                        target = a.Group.PrimaryCylinderFace;
                    }
                    if (review) key += " · #" + (total + 1);
                    entries.Add(new Entry { Reference = references.Create(doc, target), Group = key, Review = review, Number = ++total });
                }
                RenderEntries();
                status.Text = "共 " + total + " 项。点选定位，勾选后应用标签。";
            }
        }

        private static bool IsHoleWall(IFace2 face) =>
            face.GetSurface() is ISurface surf && surf.IsCylinder() && face.FaceInSurfaceSense();

        private static void CollectOpenings(IFace2 plane,List<IFace2> faces,List<IFace2> unusual)
        {
            foreach(var loop in SwMath.ToComList<ILoop2>(plane.GetLoops()))
            {
                if(loop.IsOuter())continue;
                var edges=SwMath.ToComList<IEdge>(loop.GetEdges()).ToList();
                var adjacent=edges.SelectMany(e=>SwMath.ToComList<IFace2>(e.GetTwoAdjacentFaces2()))
                    .Where(f=>SwMath.GetComIdentity(f)!=SwMath.GetComIdentity(plane)).ToList();
                faces.AddRange(adjacent);
                if(adjacent.Count==0 || adjacent.Any(IsHoleWall) || !edges.All(e=>(e.GetCurve() as ICurve)?.IsCircle()==true))continue;
                var side=adjacent[0];
                // At chamfered openings the cylindrical wall is one face farther in.
                var next=SwMath.ToComList<IEdge>(side.GetEdges()).SelectMany(e=>SwMath.ToComList<IFace2>(e.GetTwoAdjacentFaces2())).Where(IsHoleWall).ToList();
                faces.AddRange(next);
                if(next.Count==0 && !(((ISurface)side.GetSurface()).IsCylinder() && !side.FaceInSurfaceSense()))unusual.Add(side);
            }
        }

        private void CheckDocument(IModelDoc2 doc)
        {
            if (doc == null || scanned == null || SwMath.GetComIdentity(doc) != SwMath.GetComIdentity(scanned))
                throw new InvalidOperationException("活动零件已改变，请重新扫描。");
        }

        private IFace2 Resolve(IModelDoc2 doc, Entry entry)
        {
            var face = references.Resolve(doc, entry.Reference, out _) as IFace2;
            if (face == null) throw new InvalidOperationException("无法定位原孔面，请重新扫描。");
            return face;
        }

        private void Locate(TreeNode node)
        {
            
            {
                var app = application();
                var doc = app.IActiveDoc2;
                CheckDocument(doc);
                var items = node.Tag != null ? new[] { node } : node.Nodes.Cast<TreeNode>();
                doc.ClearSelection2(true);
                foreach (var item in items)
                    ((IEntity)Resolve(doc, (Entry)item.Tag)).Select4(true, null);
            }
        }

        private void Apply()
        {
            var selected = holes.Nodes.Cast<TreeNode>().SelectMany(n => n.Tag != null ? new[] { n } : n.Nodes.Cast<TreeNode>()).Where(n => n.Checked).ToList();
            if (selected.Count == 0) throw new InvalidOperationException("请先勾选孔或组。");
            var tag = tags.SelectedItem as HoleTagDefinition;
            if (tag == null) throw new InvalidOperationException("请先选择标签。");
            
            {
                var app = application();
                var doc = app.IActiveDoc2;
                CheckDocument(doc);
                if (MessageBox.Show(this, "将“" + tag.Name + "”应用到 " + doc.GetTitle() + " 中的 " + selected.Count + " 项？", "孔标注", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                var attributes = new HoleAttributeManager(app);
                int done = 0;
                foreach (var node in selected)
                {
                    try
                    {
                        var face = Resolve(doc, (Entry)node.Tag);
                        var result = detector.AnalyzeFace(doc, face);
                        if (!result.Success) throw new InvalidOperationException(result.Error);
                        var analysis = result.Value;
                        var h = analysis.Definition;
                        var existing = analysis.Group.Cylinders.Select(c => attributes.Read(c.Face)).FirstOrDefault(x => x != null);
                        if (existing != null) { h.HoleID = existing.HoleID; h.ThreadDepth = existing.ThreadDepth; }
                        tag.ApplyTo(h);
                        if (string.IsNullOrWhiteSpace(h.AnnotationText)) h.AnnotationText = new HoleAnnotationGenerator().Generate(h);
                        var saved = attributes.Save(doc, analysis.Group, h);
                        if (!saved.Success) throw new InvalidOperationException(saved.Error);
                        if (!new HoleColorManager().ApplyRgb(analysis.Group, tag.Red, tag.Green, tag.Blue, doc))
                            throw new InvalidOperationException("已写属性，上色失败。");
                        node.Checked = false;
                        done++;
                    }
                    catch (Exception ex) { status.Text = "已完成 " + done + " 项；" + node.Text + "：" + ex.Message; return; }
                }
                status.Text = "已标注 " + done + " 项；模型尚未保存。";
            }
        }
    }
}
