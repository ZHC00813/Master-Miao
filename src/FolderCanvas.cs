using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SWBodyOrganizer
{
    public sealed class FolderCanvas : ScrollableControl
    {
        private readonly Dictionary<string, Rectangle> boxes = new Dictionary<string, Rectangle>();
        private List<CategoryNode> nodes = CategoryNode.CreateDefaultTree();
        private string selectedId = CategoryNode.RootId;
        private string draggingId = string.Empty;
        private Point mouseDown;

        public event EventHandler TreeChanged;
        public event EventHandler SelectionChanged;

        public FolderCanvas()
        {
            DoubleBuffered = true;
            AutoScroll = true;
            BackColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9F);
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        public List<CategoryNode> Nodes
        {
            get { return nodes; }
            set
            {
                nodes = value ?? CategoryNode.CreateDefaultTree();
                if (!nodes.Any(item => item.Id == selectedId)) selectedId = CategoryNode.RootId;
                RebuildLayout();
                Invalidate();
            }
        }

        public string SelectedId
        {
            get { return selectedId; }
            set
            {
                selectedId = value;
                Invalidate();
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }
        }

        public CategoryNode SelectedNode
        {
            get { return nodes.FirstOrDefault(item => item.Id == selectedId); }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RebuildLayout();
        }

        private void RebuildLayout()
        {
            boxes.Clear();
            CategoryNode root = nodes.FirstOrDefault(item => item.Id == CategoryNode.RootId);
            if (root == null) return;
            int nextY = 22;
            LayoutBranch(root, 18, ref nextY, new HashSet<string>());
            int right = boxes.Count == 0 ? Width : boxes.Values.Max(item => item.Right) + 30;
            AutoScrollMinSize = new Size(Math.Max(Width - 2, right), Math.Max(Height - 2, nextY + 20));
        }

        private int LayoutBranch(CategoryNode node, int x, ref int nextY, HashSet<string> ancestry)
        {
            if (!ancestry.Add(node.Id)) return nextY;
            List<CategoryNode> children = nodes.Where(item => item.ParentId == node.Id)
                .OrderBy(item => item.Order).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            const int width = 158;
            const int height = 46;
            int y;
            if (children.Count == 0)
            {
                y = nextY;
                nextY += 64;
            }
            else
            {
                List<int> childCenters = new List<int>();
                foreach (CategoryNode child in children)
                    childCenters.Add(LayoutBranch(child, x + 202, ref nextY, new HashSet<string>(ancestry)) + height / 2);
                y = Math.Max(16, (childCenters.First() + childCenters.Last()) / 2 - height / 2);
            }
            boxes[node.Id] = new Rectangle(x, y, width, height);
            return y;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (Pen connector = new Pen(Color.FromArgb(190, 196, 205), 2F))
            {
                foreach (CategoryNode node in nodes)
                {
                    Rectangle childBox, parentBox;
                    if (!boxes.TryGetValue(node.Id, out childBox) || !boxes.TryGetValue(node.ParentId ?? string.Empty, out parentBox)) continue;
                    Point a = new Point(parentBox.Right, parentBox.Top + parentBox.Height / 2);
                    Point b = new Point(childBox.Left, childBox.Top + childBox.Height / 2);
                    int middle = (a.X + b.X) / 2;
                    e.Graphics.DrawLines(connector, new[] { a, new Point(middle, a.Y), new Point(middle, b.Y), b });
                }
            }
            foreach (CategoryNode node in nodes)
            {
                Rectangle rect;
                if (!boxes.TryGetValue(node.Id, out rect)) continue;
                bool selected = node.Id == selectedId;
                bool dragging = node.Id == draggingId;
                Color accent = ParseColor(node.ColorHex, Color.FromArgb(215, 25, 32));
                using (Brush fill = new SolidBrush(dragging ? Color.FromArgb(255, 237, 238) : Color.White))
                using (Pen border = new Pen(selected ? accent : Color.FromArgb(214, 218, 224), selected ? 3F : 1.2F))
                using (Brush accentBrush = new SolidBrush(accent))
                using (Brush textBrush = new SolidBrush(Color.FromArgb(35, 40, 48)))
                {
                    e.Graphics.FillRectangle(fill, rect);
                    e.Graphics.DrawRectangle(border, rect);
                    e.Graphics.FillRectangle(accentBrush, new Rectangle(rect.Left, rect.Top, 7, rect.Height));
                    Rectangle textRect = new Rectangle(rect.Left + 16, rect.Top + 5, rect.Width - 22, rect.Height - 10);
                    TextRenderer.DrawText(e.Graphics, node.Name, Font, textRect, Color.FromArgb(35, 40, 48),
                        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Point virtualPoint = ToVirtual(e.Location);
            CategoryNode hit = Hit(virtualPoint);
            if (hit == null) return;
            SelectedId = hit.Id;
            mouseDown = virtualPoint;
            if (e.Button == MouseButtons.Left && !hit.IsSystem) draggingId = hit.Id;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (string.IsNullOrWhiteSpace(draggingId) || e.Button != MouseButtons.Left) return;
            Point p = ToVirtual(e.Location);
            if (Math.Abs(p.X - mouseDown.X) + Math.Abs(p.Y - mouseDown.Y) > 5) Cursor = Cursors.SizeAll;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (string.IsNullOrWhiteSpace(draggingId)) return;
            Point p = ToVirtual(e.Location);
            CategoryNode target = Hit(p);
            CategoryNode moving = nodes.FirstOrDefault(item => item.Id == draggingId);
            draggingId = string.Empty;
            Cursor = Cursors.Default;
            if (target == null || moving == null || target.Id == moving.Id || target.Id == CategoryNode.UnclassifiedId)
            {
                Invalidate();
                return;
            }
            if (CategoryRules.IsDescendant(nodes, target.Id, moving.Id))
            {
                MessageBox.Show(this, "不能把文件夹拖入它自己的下级。", "父子关系无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Invalidate();
                return;
            }
            if (moving.ParentId != target.Id)
            {
                moving.ParentId = target.Id;
                RebuildLayout();
                Invalidate();
                if (TreeChanged != null) TreeChanged(this, EventArgs.Empty);
            }
        }

        private Point ToVirtual(Point value)
        {
            return new Point(value.X - AutoScrollPosition.X, value.Y - AutoScrollPosition.Y);
        }

        private CategoryNode Hit(Point point)
        {
            KeyValuePair<string, Rectangle>? hit = boxes.FirstOrDefault(item => item.Value.Contains(point));
            if (!hit.HasValue || string.IsNullOrWhiteSpace(hit.Value.Key)) return null;
            return nodes.FirstOrDefault(item => item.Id == hit.Value.Key);
        }

        private static Color ParseColor(string value, Color fallback)
        {
            try { return ColorTranslator.FromHtml(value); } catch { return fallback; }
        }
    }
}
