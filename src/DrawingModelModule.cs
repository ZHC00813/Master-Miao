using System;
using Environment = System.Environment;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace SWBodyOrganizer
{
    internal sealed class DrawingModelModule : IWorkspaceModule
    {
        private DrawingModelView view;
        public string Id => "drawing-model";
        public string Title => "AI 图纸建模";
        public Control View => view ?? (view = new DrawingModelView());
        public bool IsBusy => view != null && view.Busy;
        public bool CanClose() => !IsBusy;
        public void Dispose() => view?.Dispose();
    }

    internal sealed class DrawingModelView : UserControl
    {
        private readonly TextBox notes = new TextBox { Dock=DockStyle.Top };
        private readonly TextBox preview = new TextBox { Dock=DockStyle.Fill, Multiline=true, ScrollBars=ScrollBars.Vertical, ReadOnly=true };
        private readonly Button read = new Button {Text="选择图纸并发送给 DeepSeek", AutoSize=true};
        private readonly Button build = new Button {Text="确认尺寸并生成 SLDPRT", AutoSize=true, Enabled=false};
        private ModelPlan plan;
        internal bool Busy {get; private set;}
        public DrawingModelView()
        {
            Padding=new Padding(12);
            var bar=new FlowLayoutPanel {Dock=DockStyle.Top,Height=42};
            bar.Controls.Add(read); bar.Controls.Add(build);
            Controls.Add(preview); Controls.Add(bar); Controls.Add(notes);
            Controls.Add(new Label{Dock=DockStyle.Top,Height=28,Text="补充尺寸或单位（可选）。支持等厚板、法兰和直通圆孔。"});
            read.Click += async (s,e) => {
                using(var picker=new OpenFileDialog{Filter="工程图|*.pdf;*.dxf;*.svg"})
                {
                    if(picker.ShowDialog(this)!=DialogResult.OK)return;
                    Busy=true; read.Enabled=build.Enabled=false; plan=null;
                    try
                    {
                        preview.Text="正在识别 "+Path.GetFileName(picker.FileName);
                        string instructions=notes.Text;
                        var result=await Task.Run(()=>DrawingModelEngine.Read(picker.FileName,instructions));
                        preview.Text=result.Describe();
                        result.Validate(); plan=result; build.Enabled=true;
                    }
                    catch(Exception ex){preview.AppendText(Environment.NewLine+ex.GetBaseException().Message);}
                    finally{Busy=false;read.Enabled=true;}
                }
            };
            build.Click += (s,e)=>{
                using(var picker=new SaveFileDialog{Filter="SolidWorks 零件|*.SLDPRT",DefaultExt="SLDPRT"})
                {
                    if(plan==null || picker.ShowDialog(this)!=DialogResult.OK)return;
                    try { Busy=true; DrawingModelEngine.Build(plan,picker.FileName); preview.AppendText("\r\n已保存："+picker.FileName); }
                    catch(Exception ex){MessageBox.Show(this,ex.GetBaseException().Message,"建模");}
                    finally{Busy=false;}
                }
            };
        }
    }

    public sealed class ModelCircle { public double x,y,diameter; }
    public sealed class ModelPlan
    {
        public double thickness;
        public double[][] outline;
        public ModelCircle outerCircle;
        public ModelCircle[] holes;
        public string[] missing;
        public string description;
        public void Validate()
        {
            if(missing!=null && missing.Length>0)throw new InvalidDataException("请补充："+string.Join("；",missing));
            if(!Positive(thickness)) throw new InvalidDataException("缺少有效板厚。");
            bool poly=outline!=null && outline.Length>=3 && outline.Length<=1000;
            if(poly==(outerCircle!=null)) throw new InvalidDataException("需要一个完整的外轮廓。");
            if(poly && outline.Any(pt=>pt==null || pt.Length!=2 || !pt.All(Finite)))throw new InvalidDataException("轮廓坐标无效。");
            foreach(var c in (holes??new ModelCircle[0]).Concat(outerCircle==null?new ModelCircle[0]:new[]{outerCircle}))
                if(c==null || !Finite(c.x)||!Finite(c.y)||!Positive(c.diameter))throw new InvalidDataException("圆坐标或直径无效。");
        }
        private static bool Finite(double n)=>!double.IsNaN(n)&&!double.IsInfinity(n);
        private static bool Positive(double n)=>Finite(n)&&n>0;
        public string Describe()
        {
            var text=new StringBuilder(description).AppendLine().AppendLine("单位 mm，厚度 "+thickness);
            if(outerCircle!=null)text.AppendLine("外圆 Ø"+outerCircle.diameter+" 中心 "+outerCircle.x+", "+outerCircle.y);
            if(outline!=null)foreach(var pt in outline)text.AppendLine("轮廓点 "+string.Join(", ",pt??new double[0]));
            foreach(var c in holes??new ModelCircle[0])text.AppendLine("通孔 Ø"+c.diameter+" 中心 "+c.x+", "+c.y);
            return text.ToString();
        }
    }

    public static class DrawingModelEngine
    {
        private static readonly HttpClient http=new HttpClient(new HttpClientHandler{SslProtocols=System.Security.Authentication.SslProtocols.Tls12}){Timeout=TimeSpan.FromMinutes(3)};
        public static async Task<ModelPlan> Read(string path,string instructions)
        {
            var parts=new List<object>();
            string prompt="分析所附机械图纸，单位换算成毫米。仅支持单个等厚直边多边形板或圆盘及圆形通孔。输出 JSON：thickness 数值，outline 按顺序排列的二维顶点数组(不重复起点)或 null，outerCircle 为 {x,y,diameter} 或 null，holes 为同结构圆数组，missing 是缺失尺寸、单位、复杂结构或不确定事项数组，description 中文说明。不能用像素猜测或忽略倒角、盲孔、槽、复杂视图。不能确定的请写进 missing。用户补充："+instructions;
            parts.Add(new {type="text",text=prompt});
            string ext=Path.GetExtension(path).ToLowerInvariant();
            if(ext==".pdf")
            {
                string temp=Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),"MiaoPdf-"+Guid.NewGuid().ToString("N"))).FullName;
                try
                {
                    var script=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"RenderDrawingPdf.ps1");
                    var info=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe"),"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+script+"\" -File \""+path+"\" -Output \""+temp+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};
                    using(var process=Process.Start(info)){var error=process.StandardError.ReadToEnd();process.WaitForExit();if(process.ExitCode!=0)throw new IOException("PDF 页面读取失败："+error);}
                    foreach(var image in Directory.GetFiles(temp,"page-*.img").OrderBy(x=>x))
                    using(var bitmap=Image.FromFile(image))using(var stream=new MemoryStream())
                    {
                        bitmap.Save(stream,ImageFormat.Png);
                        parts.Add(new {type="image_url",image_url=new {url="data:image/png;base64,"+Convert.ToBase64String(stream.ToArray())}});
                    }
                    if(parts.Count==1)throw new IOException("PDF 没有可读取的页面。");
                }
                finally{foreach(var f in Directory.GetFiles(temp))File.Delete(f);Directory.Delete(temp);}
            }
            else
            {
                if(ext!=".dxf"&&ext!=".svg")throw new InvalidDataException("不支持该格式。");
                if(new FileInfo(path).Length>400000)throw new InvalidDataException("请导出精简的 ASCII DXF 或 SVG（400 KB 以内）。");
                string data=File.ReadAllText(path);
                if(data.IndexOf('\0')>=0)throw new InvalidDataException("请将二进制 DXF 另存为 ASCII DXF。");
                parts.Add(new {type="text",text=data});
            }
            var json=new JavaScriptSerializer{MaxJsonLength=64*1024*1024};
            using(var request=new HttpRequestMessage(HttpMethod.Post,"https://api.deepseek.com/chat/completions"))
            {
                string key=AssistantKeys.Current();
                request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
                request.Content=new StringContent(json.Serialize(new {model=Environment.GetEnvironmentVariable("MASTER_MIAO_DEEPSEEK_MODEL")??"deepseek-flash",messages=new[]{new {role="user",content=parts}},response_format=new {type="json_object"}}),Encoding.UTF8,"application/json");
                using(var response=await http.SendAsync(request).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    var raw=json.Deserialize<AssistantPane.Completion>(await response.Content.ReadAsStringAsync().ConfigureAwait(false)).choices.First().message.content;
                    return json.Deserialize<ModelPlan>(raw)??throw new InvalidDataException("未返回建模方案。");
                }
            }
        }

        public static void Build(ModelPlan plan,string path)
        {
            plan.Validate();
            if(File.Exists(path))throw new IOException("请选择新的零件名称。");
            ISldWorks sw;
            try { sw=(ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
            catch(COMException ex) when (ex.ErrorCode==unchecked((int)0x800401E3))
            {
                sw=(ISldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application",true));
                sw.Visible=true; sw.UserControl=true;
            }
            var template=WorkerMain.FindPartTemplate(sw);
            if(string.IsNullOrWhiteSpace(template))throw new IOException("未找到可用的 SolidWorks 零件模板。请在 SW 系统选项中设置默认零件模板。");
            var doc=(IModelDoc2)sw.NewDocument(template,0,0,0);
            if(doc==null)throw new IOException("无法新建零件，请检查默认零件模板。");
            try
            {
                IFeature plane=doc.FirstFeature() as IFeature;
                while(plane!=null && plane.GetTypeName2()!="RefPlane")plane=plane.GetNextFeature() as IFeature;
                if(plane==null || !plane.Select2(false,0))throw new IOException("基准面不可用。");
                var sketch=doc.SketchManager;
                sketch.InsertSketch(true);
                if(plan.outline!=null && plan.outline.Length>0)
                for(int i=0;i<plan.outline.Length;i++)
                {
                    var a=plan.outline[i];var b=plan.outline[(i+1)%plan.outline.Length];
                    if(sketch.CreateLine(a[0]/1000,a[1]/1000,0,b[0]/1000,b[1]/1000,0)==null)throw new IOException("轮廓边创建失败。");
                }
                foreach(var c in (plan.holes??new ModelCircle[0]).Concat(plan.outerCircle==null?new ModelCircle[0]:new[]{plan.outerCircle}))
                    if(sketch.CreateCircleByRadius(c.x/1000,c.y/1000,0,c.diameter/2000)==null)throw new IOException("圆轮廓创建失败。");
                var feature=doc.FeatureManager.FeatureExtrusion2(true,false,false,0,0,plan.thickness/1000,0,false,false,false,false,0,0,false,false,false,false,true,true,true,0,0,false);
                var bodies=((IPartDoc)doc).GetBodies2(0,false) as object[];
                if(feature==null || bodies==null || bodies.Length!=1)throw new IOException("拉伸未生成单一实体，请检查轮廓。");
                double area=0;
                if(plan.outerCircle!=null) area=Math.PI*Math.Pow(plan.outerCircle.diameter/2,2);
                else
                {
                    for(int i=0;i<plan.outline.Length;i++)
                    {
                        var a=plan.outline[i]; var b=plan.outline[(i+1)%plan.outline.Length];
                        area+=a[0]*b[1]-b[0]*a[1];
                    }
                    area=Math.Abs(area)/2;
                }
                area-=(plan.holes??new ModelCircle[0]).Sum(c=>Math.PI*Math.Pow(c.diameter/2,2));
                double expected=area*plan.thickness/1e9;
                var mass=((IBody2)bodies[0]).GetMassProperties(1.0) as double[];
                if(expected<=0 || mass==null || Math.Abs(mass[3]-expected)>Math.Max(expected*1e-5,1e-15))
                    throw new IOException("实体体积与轮廓、孔径及厚度不符，未保存。");
                int error=0,warning=0;
                if(!doc.Extension.SaveAs(path,0,1,null,ref error,ref warning)||error!=0)throw new IOException("保存零件失败："+error);
            }
            catch{sw.CloseDoc(doc.GetTitle());throw;}
        }
    }
}
