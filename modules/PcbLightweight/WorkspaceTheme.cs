using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using SWBodyOrganizer;

// Presentation-only adapter: retains the original module view, events and data stores.
internal sealed class StyledModule : IWorkspaceModule {
 readonly IWorkspaceModule inner;
 public StyledModule(IWorkspaceModule module){inner=module;}
 public string Id {get{return inner.Id;}}
 public string Title {get{return inner.Title;}}
 public Control View {get{var view=inner.View;if(Id!="body-export")WorkspaceTheme.Apply(view);return view;}}
 public bool IsBusy {get{return inner.IsBusy;}}
 public bool CanClose(){return inner.CanClose();}
 public void Dispose(){inner.Dispose();}
}
internal static class WorkspaceTheme {
 static readonly ConditionalWeakTable<Control,object> styled=new ConditionalWeakTable<Control,object>();
 static readonly Color Red=Color.FromArgb(215,25,32), Ink=Color.FromArgb(45,51,60), Border=Color.FromArgb(211,215,221);
 public static void Apply(Control root){
  if(root==null||root.IsDisposed||root.GetType().FullName=="SWBodyOrganizer.MainForm")return;
  object marker;if(styled.TryGetValue(root,out marker))return;styled.Add(root,new object());
  root.SuspendLayout();
  try{
   var button=root as Button;
   if(button!=null&&button.GetType()==typeof(Button)){
    // Color swatches are data, not decoration.
    bool swatch=button.Text.Contains("填充色")||button.Text.Contains("颜色")||button.Text.Contains("配色");
    if(!swatch){
     bool primary=button.Text=="生成轻量模型"||button.Text=="导出所选组"||button.Text=="开始导出"||button.Text=="确认尺寸并生成 SLDPRT"||button.Text=="应用标签并上色"||button.Text=="保存";
     button.UseVisualStyleBackColor=false;button.FlatStyle=FlatStyle.Flat;
     button.BackColor=primary?Red:Color.White;button.ForeColor=primary?Color.White:Ink;
     button.FlatAppearance.BorderColor=primary?Red:Border;button.FlatAppearance.BorderSize=1;
     button.FlatAppearance.MouseOverBackColor=primary?Color.FromArgb(190,25,32):Color.FromArgb(255,242,243);
     button.FlatAppearance.MouseDownBackColor=primary?Color.FromArgb(166,20,26):Color.FromArgb(255,228,231);
     if(primary){Action state=()=>{button.BackColor=button.Enabled?Red:Color.FromArgb(237,239,242);button.ForeColor=button.Enabled?Color.White:Color.FromArgb(100,107,116);button.FlatAppearance.BorderColor=button.Enabled?Red:Color.FromArgb(237,239,242);};button.EnabledChanged+=(s,e)=>state();state();}
     button.Cursor=Cursors.Hand;
     button.Resize+=(s,e)=>Round(button);Round(button);
    }
    if(button.Text=="选择图纸并发送给 DeepSeek")button.Text="添加图纸并识别";
   }
   var grid=root as DataGridView;
   if(grid!=null){grid.BackgroundColor=Color.White;grid.BorderStyle=BorderStyle.None;grid.GridColor=Color.FromArgb(231,234,238);grid.EnableHeadersVisualStyles=false;grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(248,249,250);grid.ColumnHeadersDefaultCellStyle.ForeColor=Ink;grid.DefaultCellStyle.ForeColor=Ink;grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(255,228,231);grid.DefaultCellStyle.SelectionForeColor=Color.FromArgb(102,18,28);grid.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(249,250,252);grid.RowTemplate.Height=Math.Max(30,grid.RowTemplate.Height);grid.ColumnHeadersHeight=Math.Max(32,grid.ColumnHeadersHeight);}
   var list=root as ListView;if(list!=null){list.BackColor=Color.White;list.ForeColor=Ink;list.BorderStyle=BorderStyle.FixedSingle;}
   var text=root as TextBox;if(text!=null){text.BorderStyle=BorderStyle.FixedSingle;text.BackColor=text.ReadOnly?Color.FromArgb(248,249,250):Color.White;text.ForeColor=Ink;}
   if(root is UserControl){root.BackColor=Color.FromArgb(244,246,248);}
   if(root is GroupBox){root.BackColor=Color.White;root.ForeColor=Ink;}
   if(root is FlowLayoutPanel){root.BackColor=Color.White;}
   foreach(Control child in root.Controls)Apply(child);
   root.ControlAdded+=(s,e)=>Apply(e.Control);
  }finally{root.ResumeLayout();}
 }
 static void Round(Button button){
  if(button.Width<12||button.Height<12)return;
  using(var path=new GraphicsPath()){int d=10,w=button.Width-1,h=button.Height-1;path.AddArc(0,0,d,d,180,90);path.AddArc(w-d,0,d,d,270,90);path.AddArc(w-d,h-d,d,d,0,90);path.AddArc(0,h-d,d,d,90,90);path.CloseFigure();var old=button.Region;button.Region=new Region(path);if(old!=null)old.Dispose();}
 }
}
