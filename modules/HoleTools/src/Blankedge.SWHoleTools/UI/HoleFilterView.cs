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
        private readonly List<HoleSelectionContext> markedContexts = new List<HoleSelectionContext>();
        private bool checking;
        private readonly Label markedCount = new Label { AutoSize = true, Text = "已标注孔：待读取", ForeColor = Color.DarkBlue };
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
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 196));
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            var groupPanel = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=2, RowCount=1, Margin=Padding.Empty };
            groupPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            groupPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var createGroup = new Button { Text="新建组", AutoSize=true, Anchor=AnchorStyles.Top, AccessibleName="新建标签分组" };
            createGroup.Click += (sender,args) => {
                var name = TagManagerForm.PromptGroupName(this);
                if (name == null) return;
                try {
                    var library = HoleTagCatalog.Load();
                    var group = library.AddGroup(name);
                    library.Save();
                    ReloadTags();
                    groups.SelectedItem = group;
                    status.Text = "当前分组：" + group + "。可在标签管理中新建或移入标签。";
                } catch (Exception ex) { status.Text = "保存分组失败：" + ex.Message; }
            };
            groupPanel.Controls.Add(groups,0,0);
            groupPanel.Controls.Add(createGroup,1,0);
            selection.Controls.Add(groupPanel,0,0);
            groups.SelectedIndexChanged+=(s,e)=>FilterTags();
            selection.Controls.Add(tags, 1, 0);
            selection.Controls.Add(swatch, 2, 0);
            selection.Controls.Add(rgb, 3, 0);
            root.Controls.Add(selection, 0, 1);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
            if (installButton != null) actions.Controls.Add(installButton);
            AddButton(actions, "扫描所选实体 / 面 / 孔", Scan);
            AddButton(actions, "选中面添加为外螺纹", BeginExternalThread);
            AddButton(actions, "选中面添加为一个孔", () => AddManual(false));
            AddButton(actions, "应用标签并上色", Apply);
            AddButton(actions, "读取已标注孔", LoadMarked);
            AddButton(actions, "清除勾选标注", ClearMarked);
            AddButton(actions, "标签管理", ManageTags);
            AddButton(actions, "保存标注零件", SaveMarkedParts);
            root.Controls.Add(actions, 0, 2);
            var filters = new FlowLayoutPanel { Dock = DockStyle.Fill };
            filters.Controls.Add(markedCount);
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
                if (e.Action == TreeViewAction.ByMouse) return;
                try { Locate(e.Node); } catch (Exception ex) { status.Text = ex.Message; }
            };
            holes.NodeMouseClick += (sender, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                try { Locate(e.Node); } catch (Exception ex) { status.Text = ex.Message; }
            };
            root.Controls.Add(status, 0, 5);
            Controls.Add(root);
            tags.SelectedIndexChanged += (sender, e) => UpdateColor();
            if (installButton == null)
            {
                root.Padding = new Padding(4);
                selection.ColumnStyles[0].Width = 140;
                selection.ColumnStyles[2].Width = 32;
                selection.ColumnStyles[3].Width = 0;
                actions.WrapContents = true;
                root.RowStyles[2].Height = 302;
                root.RowStyles[3].Height = 64;
            }
            ReloadTags();
            var menu = new ContextMenuStrip();
            foreach (var label in new[] { "全选", "全不选", "反选" })
            {
                string action = label;
                menu.Items.Add(label, null, (s,e) => { foreach (var n in holes.Nodes.Cast<TreeNode>().SelectMany(n => n.Tag != null ? new[] { n } : n.Nodes.Cast<TreeNode>())) n.Checked = action == "全选" || (action == "反选" && !n.Checked); });
            }
            menu.Items.Add("清除勾选标注", null, (s,e) => { try { ClearMarked(); } catch(Exception ex) { status.Text = ex.Message; } });
            holes.ContextMenuStrip = menu;
            VisibleChanged += (s,e) => { if (Visible) { try { LoadMarked(); } catch(Exception ex) { status.Text = ex.Message; } } };
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
            if (tag == null) { swatch.BackColor = Color.Transparent; rgb.Text = ""; return; }
            swatch.BackColor = Color.FromArgb(Math.Max(0, Math.Min(255, tag.Red)), Math.Max(0, Math.Min(255, tag.Green)), Math.Max(0, Math.Min(255, tag.Blue)));
            rgb.Text = string.Format("RGB {0}, {1}, {2}", tag.Red, tag.Green, tag.Blue);
        }
        private void ManageTags()
        {
            using (var form = new TagManagerForm(HoleTagCatalog.Load(), groups.SelectedItem as string)) form.ShowDialog(this);
            ReloadTags();
        }
        private sealed class Entry
        {
            public HoleSelectionContext Context;
            public string Reference;
            public string[] WallReferences;
            public string Group;
            public string MarkId;
            public bool Review, Checked, Manual;
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
                        var node = (parent?.Nodes ?? holes.Nodes).Add("孔 " + item.Number + (item.Context?.Component == null ? "" : " · " + item.Context.Component.Name2) + (parent == null ? " · " + item.Group : ""));
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
                scanned = doc;
                foreach (var context in HoleSelectionContext.Read(doc)) ScanContext(app, context);
                RenderEntries();
                UpdateMarkedCount();
                status.Text = "共 " + entries.Count + " 项。点击高亮；标注写入原零件，同一零件的其他实例也会共享标注。";
            }
        }

        private void ScanContext(ISldWorks app, HoleSelectionContext context)
        {
                var doc = context.Owner;
                int firstEntry = entries.Count;
                var faces = new List<IFace2>();
                var unusual = new List<IFace2>();
                foreach (var item in context.Items)
                {
                    if (item is IBody2 body)
                    {
                        var bodyFaces=SwMath.ToComList<IFace2>(body.GetFaces()).ToList();
                        faces.AddRange(bodyFaces);
                        foreach(var plane in bodyFaces.Where(f=>((ISurface)f.GetSurface()).IsPlane()))
                            CollectOpenings(plane,faces,unusual);
                    }
                    else if (item is IFace2 face && ((ISurface)face.GetSurface()).IsPlane())
                    {
                        faces.Add(face);
                        CollectOpenings(face,faces,unusual);
                    }
                    else if (item is IFace2 selected) faces.Add(selected);
                    else throw new InvalidOperationException("请选择实体、平面或孔壁。");
                }
                var seen = new HashSet<long>();
                int total = entries.Count;
                var attributes = new HoleAttributeManager(app);
                foreach (var candidate in faces.Concat(unusual).ToList())
                {
                    if (seen.Contains(SwMath.GetComIdentity(candidate))) continue;
                    var saved = attributes.Read(candidate);
                    if (!ManualHoleService.IsManual(saved)) continue;
                    var body = candidate.GetBody() as IBody2;
                    var members = SwMath.ToComList<IFace2>(body.GetFaces())
                        .Where(f => attributes.Read(f)?.HoleID == saved.HoleID).ToList();
                    foreach (var member in members) seen.Add(SwMath.GetComIdentity(member));
                    entries.Add(ManualEntry(doc, saved, members, ++total));
                }
                foreach (var face in faces.Where(IsHoleWall).Concat(unusual))
                {
                    if (!seen.Add(SwMath.GetComIdentity(face))) continue;
                    var result = detector.AnalyzeFace(doc, face);
                    string key = "待复核开口";
                    bool review = !result.Success;
                    IFace2 target = face;
                    string[] wallReferences = null;
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
                        wallReferences = a.Group.Cylinders.Select(c => c.Face).Concat(a.Group.Cones.Select(c => c.Face))
                            .Select(f => references.Create(doc, f)).Where(r => !string.IsNullOrEmpty(r)).Distinct().ToArray();
                    }
                    if (review) key += " · #" + (total + 1);
                    entries.Add(new Entry { Reference = references.Create(doc, target), WallReferences = wallReferences, Group = key, Review = review, Number = ++total });
                }
                foreach (var entry in entries.Skip(firstEntry)) entry.Context = context;
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

        
        private List<HoleSelectionContext> MarkContexts(IModelDoc2 doc)
        {
            if (doc is IPartDoc) return new List<HoleSelectionContext> { new HoleSelectionContext { Owner = doc, Configuration = doc.ConfigurationManager.ActiveConfiguration.Name } };
            return HoleSelectionContext.Read(doc).GroupBy(c => SwMath.GetComIdentity(c.Owner)).Select(g => g.First()).ToList();
        }
        private void LoadMarked()
        {
            var app = application(); var doc = app.IActiveDoc2;
            var contexts = MarkContexts(doc);
            var found = new List<Entry>(); var attributes = new HoleAttributeManager(app);
            foreach (var context in contexts)
            {
                context.Validate();
                var faces = SwMath.ToComList<IBody2>(((IPartDoc)context.Owner).GetBodies2(0,false)).SelectMany(b => SwMath.ToComList<IFace2>(b.GetFaces()));
                foreach (var group in faces.Select(f => new { Face=f, Hole=attributes.Read(f) }).Where(x=>x.Hole!=null && !string.IsNullOrWhiteSpace(x.Hole.HoleID)).GroupBy(x=>x.Hole.HoleID))
                {
                    var h=group.First().Hole; var members=group.Select(x=>x.Face).ToList();
                    var item=ManualEntry(context.Owner,h,members,found.Count+1);
                    item.Manual=ManualHoleService.IsManual(h); item.MarkId=h.HoleID; item.Context=context;
                    string end = h.TerminationType == TerminationType.Blind ? "盲孔" :
                        (h.TerminationType == TerminationType.Through || h.TerminationType == TerminationType.InterBody) ? "通孔" : "通盲待复核";
                    item.Group=(string.IsNullOrWhiteSpace(h.TagName)?h.ThreadSize:h.TagName)+" · "+end;
                    if (h.TerminationType == TerminationType.Blind)
                        item.Group += " · 底孔深 " + (h.MeasuredDepth*1000).ToString("0.###") + " mm" +
                            (h.HoleType != HoleSemanticType.Threaded ? "" : h.ThreadDepth>0 ? " · 螺纹深 " + (h.ThreadDepth*1000).ToString("0.###") + " mm" : " · 螺纹深未设置");
                    found.Add(item);
                }
            }
            markedContexts.Clear(); markedContexts.AddRange(contexts);
            entries.Clear(); entries.AddRange(found); scanned=doc; grouped.Checked=true; RenderEntries();
            markedCount.Text="已标注孔："+found.Count+" 个 / "+contexts.Count+" 个零件";
            status.Text="按标注属性读取，已退色的记录也会列出。点击高亮，勾选后清除；修改后请保存标注零件。";
        }
        private void ClearMarked() => ClearMarkedSelection(true);
        private void ClearMarkedSelection(bool confirm)
        {
            var app=application(); var doc=app.IActiveDoc2; CheckDocument(doc);
            var selected=entries.Where(x=>x.Checked && !string.IsNullOrEmpty(x.MarkId)).ToList();
            if(selected.Count==0) throw new InvalidOperationException("请先读取已标注孔，并勾选要清除的孔。");
            foreach(var item in selected) { item.Context.Validate(); Resolve(doc,item); }
            if(confirm && MessageBox.Show(this,"清除所选 "+selected.Count+" 个孔的标注属性和颜色？标签库保留。","清除孔标注",MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
            var attributes=new HoleAttributeManager(app);
            foreach(var item in selected)
            {
                var owner=item.Context.Owner;
                var members=item.WallReferences.Select(r=>references.Resolve(owner,r,out _) as IFace2).ToList();
                if(members.Any(f=>f==null))throw new InvalidOperationException("孔面已变化，请刷新清单。");
                var group=ManualHoleService.Group(members);
                attributes.Clear(group);
                if(members.Any(f=>attributes.Read(f)!=null))throw new InvalidOperationException("标注属性未完全清除，请保留现场并重试。");
                new HoleColorManager().Clear(group,owner);
            }
            doc.GraphicsRedraw2(); LoadMarked();
            status.Text="已清除 "+selected.Count+" 个孔的属性和颜色；请点击保存标注零件。";
        }

        private void UpdateMarkedCount()
        {
            var app=application();var doc=app.IActiveDoc2;
            var owners=entries.Select(e=>e.Context?.Owner ?? doc).GroupBy(SwMath.GetComIdentity).Select(g=>g.First()).ToList();
            var attrs=new HoleAttributeManager(app);
            markedCount.Text="已标注孔："+owners.Sum(o=>attrs.Enumerate(o).Count())+" 个 / "+owners.Count+" 个零件";
        }

        private void CheckDocument(IModelDoc2 doc)
        {
            if (doc == null || scanned == null || SwMath.GetComIdentity(doc) != SwMath.GetComIdentity(scanned))
                throw new InvalidOperationException("活动零件已改变，请重新扫描。");
        }

        private IFace2 Resolve(IModelDoc2 doc, Entry entry)
        {
            entry.Context?.Validate();
            var face = references.Resolve(entry.Context?.Owner ?? doc, entry.Reference, out _) as IFace2;
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
                int selectedCount = 0;
                var seen = new HashSet<long>();
                foreach (var item in items)
                {
                    var entry = (Entry)item.Tag;
                    entry.Context?.Validate();
                    var refs = entry.WallReferences != null && entry.WallReferences.Length > 0
                        ? entry.WallReferences : new[] { entry.Reference };
                    foreach (string reference in refs)
                    {
                        var face = references.Resolve(entry.Context?.Owner ?? doc, reference, out _) as IFace2;
                        if (face != null && entry.Context?.Component != null) face = entry.Context.Component.GetCorrespondingEntity(face) as IFace2;
                        if (face == null) continue;
                        if (seen.Add(SwMath.GetComIdentity(face)) && ((IEntity)face).Select4(true, null)) selectedCount++;
                    }
                }
                doc.GraphicsRedraw2();
                status.Text = selectedCount > 0 ? "已高亮孔壁（" + selectedCount + " 个面），原标注颜色不变。" : "无法高亮原孔，请重新扫描。";
            }
        }

        private Entry ManualEntry(IModelDoc2 doc, HoleDefinition hole, IList<IFace2> faces, int number)
        {
            return new Entry { Reference = references.Create(doc, faces[0]),
                WallReferences = faces.Select(f => references.Create(doc, f)).ToArray(),
                Group = (hole.HoleType == HoleSemanticType.ExternalThread ? "外螺纹 · " : "手动孔 · ") + hole.TagName, Manual = true, Number = number };
        }

        private Timer externalSelection;
        internal void BeginExternalThread()
        {
            if (externalSelection != null) { StopExternalSelection(); status.Text = "已取消选择外螺纹面。"; return; }
            var doc = application().IActiveDoc2;
            if (!(doc is IPartDoc) && !(doc is IAssemblyDoc)) throw new InvalidOperationException("请先打开零件或装配体。");
            if (((ISelectionMgr)doc.SelectionManager).GetSelectedObjectCount2(-1) > 0) { AddManual(true); return; }
            status.Text = "请在 SOLIDWORKS 图形区点选一个外圆柱面，再次点击外螺纹按钮可取消。";
            externalSelection = new Timer { Interval = 250 };
            externalSelection.Tick += (sender,args) => {
                try {
                    if (!Visible || SwMath.GetComIdentity(application().IActiveDoc2) != SwMath.GetComIdentity(doc)) { StopExternalSelection(); return; }
                    if (((ISelectionMgr)doc.SelectionManager).GetSelectedObjectCount2(-1) == 0) return;
                    StopExternalSelection();
                    AddManual(true);
                } catch (Exception ex) { StopExternalSelection(); status.Text = ex.Message; }
            };
            externalSelection.Start();
        }

        private void StopExternalSelection() { externalSelection?.Dispose(); externalSelection = null; }
        protected override void Dispose(bool disposing) { if (disposing) StopExternalSelection(); base.Dispose(disposing); }

        private void AddManual(bool external)
        {
            var app = application();
            var doc = app.IActiveDoc2;
            var contexts = HoleSelectionContext.Read(doc);
            if (contexts.Count != 1 || contexts[0].Items.Any(item => !(item is IFace2)))
                throw new InvalidOperationException("一个手动孔请选择同一零件实例的多个面。");
            var context = contexts[0];
            var owner = context.Owner;
            var faces = context.Items.Cast<IFace2>().ToList();
            var group = ManualHoleService.Group(faces);
            var tag = tags.SelectedItem as HoleTagDefinition;
            if (!external && tag == null) throw new InvalidOperationException("请先选择标签。");
            if (external) {
                if (faces.Count != 1 || !ManualHoleService.IsExternalCylinder(faces[0])) throw new InvalidOperationException("请选择一个外圆柱面。");
                tag = ExternalThreadPicker.Choose(this, tag);
                if (tag == null) return;
                ReloadTags();
            }
            else if (tag.HoleType == HoleSemanticType.ExternalThread) throw new InvalidOperationException("请使用“选中面标为外螺纹”。");
            else if (MessageBox.Show(this, "将选中的 " + group.ManualFaces.Count + " 个面合为一个手动孔，并应用标签“" + tag.Name + "”？", "手动添加孔", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            var hole = ManualHoleService.Create(app, owner, group.ManualFaces, tag);
            if (scanned == null || SwMath.GetComIdentity(scanned) != SwMath.GetComIdentity(doc)) entries.Clear();
            scanned = doc;
            var ids = new HashSet<string>(group.ManualFaces.Select(f => references.Create(owner, f)));
            entries.RemoveAll(e => e.Context?.Owner == owner && ids.Contains(e.Reference));
            var newEntry = ManualEntry(owner, hole, group.ManualFaces, entries.Count == 0 ? 1 : entries.Max(e => e.Number) + 1);
            newEntry.Context = context;
            entries.Add(newEntry);
            RenderEntries();
            doc.ClearSelection2(true);
            doc.GraphicsRedraw2();
            UpdateMarkedCount();
            status.Text = external ? "已标记外螺纹：" + tag.Name + "。请保存零件。" : "已将 " + group.ManualFaces.Count + " 个面记为一个手动孔并上色；请保存零件。";
        }

        private void SaveMarkedParts()
        {
            var host = application().IActiveDoc2;
            CheckDocument(host);
            var owners = entries.Select(e => e.Context?.Owner ?? host).GroupBy(SwMath.GetComIdentity).Select(g => g.First()).ToList();
            if (owners.Count == 0) { foreach(var c in markedContexts)c.Validate(); owners = markedContexts.Select(c=>c.Owner).ToList(); }
            if(owners.Count==0) throw new InvalidOperationException("请先读取或扫描孔。");
            int saved = 0;
            foreach (var owner in owners)
            {
                if (string.IsNullOrWhiteSpace(owner.GetPathName())) throw new InvalidOperationException("请先为新建或虚拟零件另存文件，再保存标注。");
                int errors = 0, warnings = 0;
                if (!owner.Save3(1, ref errors, ref warnings) || errors != 0)
                    throw new InvalidOperationException("已保存 " + saved + " 个零件；" + owner.GetTitle() + " 保存失败，错误码 " + errors);
                saved++;
            }
            status.Text = "已保存 " + saved + " 个零件中的孔标注和颜色。";
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
                        var entry = (Entry)node.Tag;
                        var owner = entry.Context?.Owner ?? doc;
                        if (entry.Manual)
                        {
                            var members = entry.WallReferences.Select(r => references.Resolve(owner, r, out _) as IFace2).ToList();
                            if (members.Any(f => f == null)) throw new InvalidOperationException("手动孔的面已变化，请重新扫描。");
                            var manualGroup = ManualHoleService.Group(members);
                            var manualHole = attributes.Read(face);
                            if (!ManualHoleService.IsManual(manualHole)) throw new InvalidOperationException("手动孔标注已失效，请重新扫描。");
                            if ((manualHole.HoleType == HoleSemanticType.ExternalThread) != (tag.HoleType == HoleSemanticType.ExternalThread)) throw new InvalidOperationException("请选择与标注类型一致的标签。");
                            tag.ApplyTo(manualHole);
                            var manualSaved = attributes.Save(owner, manualGroup, manualHole);
                            if (!manualSaved.Success) throw new InvalidOperationException(manualSaved.Error);
                            if (!new HoleColorManager().ApplyRgb(manualGroup, tag.Red, tag.Green, tag.Blue, owner)) throw new InvalidOperationException("上色失败。");
                            entry.Group = (tag.HoleType == HoleSemanticType.ExternalThread ? "外螺纹 · " : "手动孔 · ") + tag.Name;
                            node.Checked = false;
                            done++;
                            continue;
                        }
                        if (tag.HoleType == HoleSemanticType.ExternalThread) throw new InvalidOperationException("外螺纹标签仅适用于外圆柱面。");
                        var result = detector.AnalyzeFace(owner, face);
                        if (!result.Success) throw new InvalidOperationException(result.Error);
                        var analysis = result.Value;
                        var h = analysis.Definition;
                        var existing = analysis.Group.Cylinders.Select(c => attributes.Read(c.Face)).FirstOrDefault(x => x != null);
                        if (existing != null) { h.HoleID = existing.HoleID; h.ThreadDepth = existing.ThreadDepth; }
                        tag.ApplyTo(h);
                        if (string.IsNullOrWhiteSpace(h.AnnotationText)) h.AnnotationText = new HoleAnnotationGenerator().Generate(h);
                        var saved = attributes.Save(owner, analysis.Group, h);
                        if (!saved.Success) throw new InvalidOperationException(saved.Error);
                        if (!new HoleColorManager().ApplyRgb(analysis.Group, tag.Red, tag.Green, tag.Blue, owner))
                            throw new InvalidOperationException("已写属性，上色失败。");
                        node.Checked = false;
                        done++;
                    }
                    catch (Exception ex) { status.Text = "已完成 " + done + " 项；" + node.Text + "：" + ex.Message; return; }
                }
                doc.GraphicsRedraw2();
                UpdateMarkedCount();
                status.Text = "已标注 " + done + " 项；请保存对应零件。相同零件实例共享标注。";
            }
        }
    }
}





