using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SWBodyOrganizer
{
    internal sealed class ExportActionButton : Button
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;
            g.Clear(Parent.BackColor);
            g.SmoothingMode=SmoothingMode.AntiAlias;
            var box=new Rectangle(1,1,Width-3,Height-3);
            int corner=System.Math.Min(Height/2,16);
            using(var path=new GraphicsPath())
            {
                path.AddArc(box.Left,box.Top,corner,corner,180,90);
                path.AddArc(box.Right-corner,box.Top,corner,corner,270,90);
                path.AddArc(box.Right-corner,box.Bottom-corner,corner,corner,0,90);
                path.AddArc(box.Left,box.Bottom-corner,corner,corner,90,90);
                path.CloseFigure();
                bool hover=ClientRectangle.Contains(PointToClient(Cursor.Position));
                var fill=Enabled ? (hover ? Color.FromArgb(171,23,36) : BackColor) : Color.FromArgb(237,239,242);
                using(var brush=new SolidBrush(fill)) g.FillPath(brush,path);
            }
            TextRenderer.DrawText(g,Text,Font,box,Enabled?Color.White:Color.FromArgb(84,92,102),
                TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
            if(Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(box,-6,-5));
        }
    }
}
