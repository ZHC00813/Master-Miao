using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
namespace MasterMiao.Weld {
 public static class WeldBadge {
  public static string Label(WeldGroup group){return string.IsNullOrWhiteSpace(group.BadgeNumber)?group.Code.TrimStart('W','0'):group.BadgeNumber;}
  public static string ValidateNumber(string number){number=(number??"").Trim();if(number.Length<1||number.Length>6)throw new ArgumentException("标牌数字请输入 1 至 6 位数字。");foreach(char c in number)if(c<'0'||c>'9')throw new ArgumentException("标牌编号只能使用数字。");return number;}
  public static Bitmap Draw(string number,int color,bool mask=false){
   var image=new Bitmap(384,512,PixelFormat.Format32bppArgb);
   using(var g=Graphics.FromImage(image))using(var shape=new GraphicsPath()){
    g.Clear(mask?Color.Black:Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
    shape.StartFigure();shape.AddBezier(192,16,210,16,250,35,326,60);shape.AddBezier(326,60,351,69,360,84,360,106);shape.AddLine(360,106,360,405);shape.AddBezier(360,405,360,429,346,442,325,451);shape.AddBezier(325,451,266,474,215,496,192,496);shape.AddBezier(192,496,169,496,118,474,59,451);shape.AddBezier(59,451,38,442,24,429,24,405);shape.AddLine(24,405,24,106);shape.AddBezier(24,106,24,84,33,69,58,60);shape.AddBezier(58,60,134,35,174,16,192,16);shape.CloseFigure();
    using(var fill=new SolidBrush(mask?Color.White:ColorTranslator.FromWin32(color)))g.FillPath(fill,shape);
    if(!mask){using(var pen=new Pen(Color.Black,14))g.DrawPath(pen,shape);using(var font=new Font("Arial",Math.Min(176,450f/Math.Max(1,number.Length)),FontStyle.Regular,GraphicsUnit.Pixel))using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})g.DrawString(number,font,Brushes.Black,new RectangleF(42,38,300,436),format);}
   }return image;
  }
  public static void Save(string imagePath,string number,int color){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(imagePath)));using(var bmp=Draw(number,color))bmp.Save(imagePath,ImageFormat.Png);using(var mask=Draw(number,color,true))mask.Save(Path.ChangeExtension(imagePath,"mask.png"),ImageFormat.Png);}
 }
}
