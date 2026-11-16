using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using SWBodyOrganizer;

namespace MasterMiao.Pcb
{
 public sealed class PcbModule : IWorkspaceModule
 {
  private PcbView view;
  public string Id { get { return "pcb-lightweight"; } }
  public string Title { get { return "PCB模型轻量化"; } }
  public Control View { get { return view ?? (view=new PcbView()); } }
  public bool IsBusy { get { return view!=null && view.Busy; } }
  public bool CanClose() { return !IsBusy; }
  public void Dispose() { if(view!=null)view.Dispose(); }
 }
 public static class PcbRules
 {
  public static readonly JavaScriptSerializer Json=new JavaScriptSerializer { MaxJsonLength=int.MaxValue };
  public static List<Dictionary<string,object>> Rows(Dictionary<string,object> plan) { return ((IEnumerable)plan["components"]).Cast<Dictionary<string,object>>().ToList(); }
  public static string Text(Dictionary<string,object> r,string k) { return r.ContainsKey(k)?Convert.ToString(r[k]):""; }
  public static void Apply(Dictionary<string,object> plan,int n,bool protect,string shape)
  {
   if(n<0)throw new ArgumentOutOfRangeException("n");
   foreach(var r in Rows(plan))
   {
    if(Convert.ToInt32(r["solid_count"])==0)continue;
    string cat=Text(r,"category");
    // Unknown components stay preserved until reviewed individually.
    bool keep=cat=="board" || Convert.ToBoolean(r["review"]) || (cat=="interface"?protect:Convert.ToInt32(r["rank"])<=n);
    r["action"]=keep?"keep":"envelope";r["simplify_shape"]=shape;
   }
  }
 }
 public sealed class PcbView : UserControl
 {
  readonly TextBox input=new TextBox { ReadOnly=true,Width=600 };
  readonly Button pick=new Button { Text="导入 STEP / STP",AutoSize=true };
  readonly Button analyze=new Button { Text="分析元件",AutoSize=true };
  readonly Button build=new Button { Text="生成轻量模型",AutoSize=true,Enabled=false };
  readonly Button cancel=new Button { Text="取消任务",AutoSize=true,Enabled=false };
  readonly Button open=new Button { Text="打开结果文件夹",AutoSize=true,Enabled=false };
  readonly NumericUpDown count=new NumericUpDown { Minimum=0,Maximum=100000,Value=10,Width=78 };
  readonly CheckBox ports=new CheckBox { Text="保留重要接口原形",Checked=true,AutoSize=true };


  readonly ComboBox shape=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Width=140 };
  readonly Button apply=new Button { Text="应用规则到清单",AutoSize=true };
  readonly Button all=new Button { Text="保留全部细节",AutoSize=true };
  readonly Label summary=new Label { Dock=DockStyle.Fill,Text="先导入文件并分析。原始模型保持不变。",AutoEllipsis=true,TextAlign=ContentAlignment.MiddleLeft };
  readonly TextBox log=new TextBox { Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical };
  readonly DataGridView grid=new DataGridView { Dock=DockStyle.Fill,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,RowHeadersVisible=false,BackgroundColor=Color.White,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=true };
  Dictionary<string,object> plan; string job,result; Process process; bool canceled;
  public bool Busy {get; private set;}
  static string Root {get {return AppDomain.CurrentDomain.BaseDirectory;}}
  public PcbView()
  {
   Padding=new Padding(14);BackColor=Color.FromArgb(246,248,251);Font=new Font("Microsoft YaHei UI",9);
   shape.Items.AddRange(new object[]{"自动：圆柱 / 方形","全部长方体","全部圆柱"});shape.SelectedIndex=0;
   var layout=new TableLayoutPanel { Dock=DockStyle.Fill,RowCount=7,ColumnCount=1 };
   foreach(int h in new[]{42,42,38,42})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
   layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,105));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
   var files=new FlowLayoutPanel { Dock=DockStyle.Fill };files.Controls.AddRange(new Control[]{pick,input,analyze});
   var options=new FlowLayoutPanel { Dock=DockStyle.Fill };options.Controls.AddRange(new Control[]{ports,new Label { Text="其他元件按体积保留前",AutoSize=true,Padding=new Padding(10,7,0,0)},count,new Label{Text="个原形",AutoSize=true,Padding=new Padding(0,7,8,0)},shape,apply,all});
   var modes=new FlowLayoutPanel { Dock=DockStyle.Fill };modes.Controls.AddRange(new Control[]{new Label { Text="其余元件简化占位，不删除；保留多实体、板体和孔槽，使用 SolidWorks 完整曲线导出。",AutoSize=true,Padding=new Padding(15,5,0,0)}});
   layout.Controls.Add(files,0,0);layout.Controls.Add(options,0,1);layout.Controls.Add(modes,0,2);layout.Controls.Add(summary,0,3);layout.Controls.Add(grid,0,4);layout.Controls.Add(log,0,5);
   var actions=new FlowLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(0,6,0,0) };actions.Controls.AddRange(new Control[]{build,cancel,open});layout.Controls.Add(actions,0,6);Controls.Add(layout);
   grid.Columns.Add(new DataGridViewTextBoxColumn {Name="name",HeaderText="元件名称",ReadOnly=true,FillWeight=230});
   grid.Columns.Add(new DataGridViewTextBoxColumn {Name="category",HeaderText="类别",ReadOnly=true,FillWeight=65});
   grid.Columns.Add(new DataGridViewTextBoxColumn {Name="volume",HeaderText="体积 mm³",ReadOnly=true,FillWeight=65});
   grid.Columns.Add(new DataGridViewTextBoxColumn {Name="rank",HeaderText="其他元件排名",ReadOnly=true,FillWeight=65});
   grid.Columns.Add(new DataGridViewComboBoxColumn {Name="action",HeaderText="处理方式",DataSource=new[]{"保留原形","自动简化","长方体","圆柱","忽略无厚度内容"},FillWeight=95});
   grid.Columns.Add(new DataGridViewCheckBoxColumn {Name="review",HeaderText="待人工确认",FillWeight=65});
   grid.CurrentCellDirtyStateChanged+=(s,e)=>{if(grid.IsCurrentCellDirty)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
   grid.CellValueChanged+=(s,e)=>{if(e.RowIndex>=0)UpdateSummary();};
   pick.Click+=(s,e)=>{using(var d=new OpenFileDialog{Filter="PCB STEP 模型|*.step;*.stp"})if(d.ShowDialog(this)==DialogResult.OK){input.Text=d.FileName;plan=null;grid.Rows.Clear();build.Enabled=false;open.Enabled=false;summary.Text="点击分析元件，读取板体、接口及体积排名。";}};
   analyze.Click+=async(s,e)=>await Analyze();build.Click+=async(s,e)=>await Build();
   apply.Click+=(s,e)=>{if(plan!=null){PcbRules.Apply(plan,(int)count.Value,ports.Checked,new[]{"auto","box","cylinder"}[shape.SelectedIndex]);Populate();}};
   all.Click+=(s,e)=>{if(plan==null)return;count.Value=PcbRules.Rows(plan).Count;ports.Checked=true;apply.PerformClick();};
   cancel.Click+=(s,e)=>{canceled=true;try{if(process!=null&&!process.HasExited)process.Kill();}catch(InvalidOperationException){}cancel.Enabled=false;};
   open.Click+=(s,e)=>{if(result!=null)Process.Start("explorer.exe",Quote(Path.GetDirectoryName(result)));};
  }
  void SetBusy(bool value){Busy=value;pick.Enabled=analyze.Enabled=apply.Enabled=all.Enabled=grid.Enabled=count.Enabled=ports.Enabled=shape.Enabled=!value;build.Enabled=!value&&plan!=null;cancel.Enabled=value;}
  static string Quote(string value) { return "\""+value.Replace("\"","\\\"")+"\""; }
  async Task Run(string args)
  {
   string python=Path.Combine(Root,"PcbRuntime","python.exe"),script=Path.Combine(Root,"PcbEngine","scripts","pcb_simplify.py");
   if(!File.Exists(python)||!File.Exists(script))throw new IOException("缺少 PCB 运行组件，请使用完整测试包。");
   var start=new ProcessStartInfo(python,Quote(script)+" --ocp-path "+Quote(Path.Combine(Root,"PcbRuntime","cadlibs"))+" "+args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
   start.EnvironmentVariables["PYTHONUTF8"]="1";start.EnvironmentVariables["PYTHONIOENCODING"]="utf-8";
   canceled=false;process=new Process { StartInfo=start };
   try {
    process.Start();var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
    bool done=await Task.Run(()=>process.WaitForExit(1800000));if(!done){process.Kill();throw new TimeoutException("处理超过30分钟，已停止，可保留日志排查。");}
    string text=(await stdout)+"\r\n"+(await stderr);File.AppendAllText(Path.Combine(job,"process.log"),text,Encoding.UTF8);
    log.Text=text.Length>12000?text.Substring(text.Length-12000):text;
    if(canceled)throw new OperationCanceledException("任务已取消，原文件未修改。");
    if(process.ExitCode!=0)throw new IOException("处理未完成，请查看下方日志。任务目录："+job);
   }finally{process.Dispose();process=null;}
  }
  async Task Analyze()
  {
   if(!File.Exists(input.Text)){MessageBox.Show(this,"请先选择 STEP / STP 文件。");return;}
   job=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MasterMiao","PCB模型轻量化","Jobs",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));Directory.CreateDirectory(job);
   plan=null;grid.Rows.Clear();SetBusy(true);summary.Text="正在分析板体、接口、体积排名和简化形状…";
   try{await Run("analyze "+Quote(input.Text)+" --work "+Quote(job)+" --keep-largest "+count.Value);plan=PcbRules.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(job,"plan.json")));PcbRules.Apply(plan,(int)count.Value,ports.Checked,new[]{"auto","box","cylinder"}[shape.SelectedIndex]);Populate();}
   catch(Exception ex){summary.Text=ex.Message;}finally{SetBusy(false);}
  }
  public void LoadReviewedJob(string directory){job=directory;plan=PcbRules.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(job,"plan.json")));input.Text=PcbRules.Text(plan,"input");Populate();build.Enabled=true;}
  void Populate()
  {
   grid.Rows.Clear();
   foreach(var r in PcbRules.Rows(plan).OrderBy(r=>PcbRules.Text(r,"category")=="board"?0:PcbRules.Text(r,"category")=="interface"?1:2).ThenByDescending(r=>Convert.ToDouble(r["volume_mm3"])))
   {
    string cat=PcbRules.Text(r,"category"),act=PcbRules.Text(r,"action"),kind=PcbRules.Text(r,"simplify_shape");
    int index=grid.Rows.Add(r["name"],cat=="board"?"板体":cat=="interface"?"重要接口":"其他元件",Convert.ToDouble(r["volume_mm3"]).ToString("0.###"),r.ContainsKey("rank")?r["rank"]:"",act=="keep"?"保留原形":act=="omit"?"忽略无厚度内容":kind=="box"?"长方体":kind=="cylinder"?"圆柱":"自动简化",r["review"]);
    var row=grid.Rows[index];row.Tag=r;
    if(cat=="board")row.ReadOnly=true;
    if(Convert.ToBoolean(r["review"]))row.DefaultCellStyle.BackColor=Color.LightGoldenrodYellow;
   }UpdateSummary();
  }
  void UpdateSummary(){if(plan==null)return;int kept=0,simplified=0,review=0;foreach(DataGridViewRow r in grid.Rows){if(Convert.ToBoolean(r.Cells["review"].Value))review++;string a=Convert.ToString(r.Cells["action"].Value);if(a=="保留原形")kept++;else if(a!="忽略无厚度内容")simplified++;}summary.Text=string.Format("保留原形 {0} 项 · 简化 {1} 项 · 待确认 {2} 项。可逐项修改处理方式；重要接口不参加其他元件排名。",kept,simplified,review);}
  async Task Build()
  {
   grid.EndEdit();
   foreach(DataGridViewRow g in grid.Rows){var r=(Dictionary<string,object>)g.Tag;string a=Convert.ToString(g.Cells["action"].Value);if(Convert.ToBoolean(g.Cells["review"].Value)){MessageBox.Show(this,"请逐项检查黄色待确认元件，确认类别和处理方式后取消待确认勾选。");return;}if(a=="忽略无厚度内容"&&Convert.ToInt32(r["solid_count"])>0){MessageBox.Show(this,"有实体的元件不能删除，请选择保留或简化。");return;}r["action"]=a=="保留原形"?"keep":a=="忽略无厚度内容"?"omit":"envelope";r["simplify_shape"]=a=="圆柱"?"cylinder":a=="长方体"?"box":"auto";r["review"]=false;}
   using(var d=new SaveFileDialog { Filter="轻量 STEP 模型|*.stp",FileName=Path.GetFileNameWithoutExtension(input.Text)+"_轻量化.stp",OverwritePrompt=true })
   {
    if(d.ShowDialog(this)!=DialogResult.OK)return;if(File.Exists(d.FileName)){MessageBox.Show(this,"请选择一个新文件名，避免覆盖已有模型。");return;}
    result=d.FileName;File.WriteAllText(Path.Combine(job,"plan.json"),PcbRules.Json.Serialize(plan),new UTF8Encoding(false));SetBusy(true);open.Enabled=false;summary.Text="正在生成并回读检查轻量模型…";
    try{await Run("build --work "+Quote(job)+" --output "+Quote(result)+" --multi-solid --pcurves");var report=PcbRules.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(job,"report.json")));summary.Text="已生成："+report["solid_count"]+" 个实体，"+report["final_faces"]+" 个面，"+(Convert.ToDouble(report["output_bytes"])/1048576).ToString("0.0")+" MB。几何校验通过；SolidWorks 实测另行记录。";File.Copy(Path.Combine(job,"report.json"),result+".report.json",false);open.Enabled=true;}
    catch(Exception ex){summary.Text=ex.Message;}finally{SetBusy(false);}
   }
  }
 }
}
