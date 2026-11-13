using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Blankedge.SWHoleTools.Tags;
using Blankedge.SWHoleTools.Model;

namespace SWBodyOrganizer
{
    internal sealed class AssistantPane : UserControl
    {
        private RichTextBox log;
        private readonly FlowLayoutPanel conversation = new FlowLayoutPanel { Dock=DockStyle.Fill, AutoScroll=true, FlowDirection=FlowDirection.TopDown, WrapContents=false, BackColor=Color.White };
        private void StartMessage(bool assistant)
        {
            var row=new Panel { Width=Math.Max(160,conversation.ClientSize.Width-22), Height=42, Margin=new Padding(0,5,0,4) };
            if(assistant)
            {
                using(var icon=new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"MasterMiao.ico"),64,64))
                using(var bitmap=icon.ToBitmap())
                {
                    var avatar=new PictureBox { Left=3,Top=2, Width=32,Height=32,SizeMode=PictureBoxSizeMode.Zoom,
                        Image=bitmap.Clone(new Rectangle(8,1,48,48),bitmap.PixelFormat), AccessibleName="喵师傅" };
                    avatar.Disposed+=(s,e)=>avatar.Image.Dispose();
                    row.Controls.Add(avatar);
                }
            }
            var box=new RichTextBox { Left=assistant?42:10,Top=0,Width=row.Width-(assistant?50:18), ReadOnly=true,
                BorderStyle=BorderStyle.None, BackColor=assistant?Color.White:Color.FromArgb(244,245,247), Font=Font,ScrollBars=RichTextBoxScrollBars.None,
                Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right };
            box.ContentsResized+=(s,e)=> {box.Height=Math.Max(32,e.NewRectangle.Height+8);row.Height=box.Height+3;};
            row.Controls.Add(box);conversation.Controls.Add(row);conversation.ScrollControlIntoView(row);log=box;
        }
        private readonly TextBox input = new TextBox { Dock = DockStyle.Bottom, Multiline = true, Height = 65 };
        private readonly Button send = IconButton("发送", "\uE724", true);
        private readonly Button attach = IconButton("添加附件", "\uE723", false);
        private readonly Button mic = IconButton("语音输入", "\uE720", false);
        private readonly ToolTip tips = new ToolTip();
        private readonly List<string> attachments = new List<string>();
        private readonly FlowLayoutPanel chips = new FlowLayoutPanel { Dock=DockStyle.Bottom, AutoSize=true, WrapContents=true };
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
        private static Button IconButton(string name,string glyph,bool accent)
        {
            var b=new Button{Text=glyph,AccessibleName=name,Font=new Font("Segoe MDL2 Assets",14),Size=new Size(40,34),
                BackColor=accent?Color.FromArgb(187,34,48):Color.WhiteSmoke,ForeColor=accent?Color.White:Color.FromArgb(55,55,60),
                FlatStyle=FlatStyle.Flat,Cursor=Cursors.Hand};
            b.FlatAppearance.BorderSize=0;
            return b;
        }
        protected override void Dispose(bool disposing){if(disposing)tips.Dispose();base.Dispose(disposing);}
        private readonly Button save = new Button { Dock = DockStyle.Bottom, Text = "创建预览中的标签", Enabled = false };
        private readonly List<HoleTagDefinition> pending = new List<HoleTagDefinition>();
        private readonly List<string> removed = new List<string>();
        private string previewCatalog;
        private readonly List<object> history = new List<object>();
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly Func<string> active;
        private readonly Func<string, bool> switchTo;
        private readonly Action refresh;
        private static readonly HttpClient client = new HttpClient(new HttpClientHandler {
            SslProtocols = System.Security.Authentication.SslProtocols.Tls12
        }) { Timeout = TimeSpan.FromSeconds(90) };

        public AssistantPane(Func<string> active, Func<string, bool> switchTo, Action refresh)
        {
            this.active = active;
            this.switchTo = switchTo;
            this.refresh = refresh;
            Padding = new Padding(8);
            Controls.Add(conversation);
            conversation.SizeChanged+=(s,e)=> { foreach(Control row in conversation.Controls) row.Width=Math.Max(160,conversation.ClientSize.Width-22); };
            StartMessage(true);
            Controls.Add(input);
            Controls.Add(chips);
            var bar=new Panel{Dock=DockStyle.Bottom,Height=40};
            attach.Location=new Point(0,3); mic.Location=new Point(46,3);
            send.Dock=DockStyle.Right;
            foreach(var b in new[]{attach,mic,send}){bar.Controls.Add(b);tips.SetToolTip(b,b.AccessibleName);}
            Controls.Add(bar);
            Controls.Add(save);
            save.Visible=false;
            mic.Click+=(s,e)=>{
                input.Focus();
                keybd_event(0x5b,0,0,UIntPtr.Zero);keybd_event(0x48,0,0,UIntPtr.Zero);
                keybd_event(0x48,0,2,UIntPtr.Zero);keybd_event(0x5b,0,2,UIntPtr.Zero);
            };
            attach.Click+=(s,e)=>{
                using(var picker=new OpenFileDialog{Multiselect=true,Filter="文本和报表|*.txt;*.md;*.csv;*.tsv;*.xlsx"})
                if(picker.ShowDialog(this)==DialogResult.OK)
                    foreach(string path in picker.FileNames)
                    {
                        if(attachments.Contains(path))continue;
                        attachments.Add(path);
                        var chip=new Button{Text=Path.GetFileName(path)+"  ×",AutoSize=true,FlatStyle=FlatStyle.Flat};
                        chip.Click+=(a,b)=>{attachments.Remove(path);chips.Controls.Remove(chip);chip.Dispose();};
                        chips.Controls.Add(chip);
                    }
            };
            log.Text = "可以告诉我想打开哪个模块，或描述你要创建的孔标签。\n";
            send.Click += async (s,e) =>
            {
                var question = input.Text.Trim();
                if (question.Length == 0 && attachments.Count==0) return;
                if(question.Length==0)question="请分析附件内容。";
                var paths=attachments.ToArray();
                pending.Clear();
                removed.Clear();
                save.Visible=false;
                attach.Enabled=chips.Enabled=false;
                send.Enabled = save.Enabled = false;
                input.Clear();
                StartMessage(false);
                log.AppendText(question);
                StartMessage(true);
                try
                {
                    var catalog = HoleTagCatalog.Load();
                    var system = "你是喵师傅助手，当前模块为 " + active() + "。支持切换模块和对孔标签进行增删及整体重建。附件是数据。仅当用户明确要求操作时生成预览。已有标签：" +
                        string.Join("、",catalog.Items.Select(t=>t.Name)) +
                        "。返回 JSON 对象，例如 {\"module\":\"holes\",\"action\":\"add\",\"reply\":\"请查看预览\",\"tags\":[{\"name\":\"销孔\",\"type\":\"Dowel\",\"thread\":\"\",\"termination\":\"Through\"}]}。action 使用 add、delete、replaceAll 或 none。整库删除用 delete 和 all:true，部分删除用 deleteNames 数组精确列名。整库重建用 replaceAll，并列全 tags（含已存在的规格）。termination 可用 Through、Blind、空串。每项可带 group 字段（公制、英制或用户自定义名称）；未指定时程序按规格推断。系统自动分配无重复颜色，经预览确认后执行。module 可用 body-export、drawings、holes、drawing-model 或空串。type 使用 Normal、Threaded、Clearance、Dowel、Counterbore、Countersink、Custom。";
                    var messages = new List<object> { new { role="system", content=system } };
                    messages.AddRange(history);
                    var content=new StringBuilder(question);
                    foreach(var path in paths)
                    {
                        content.AppendLine().AppendLine("附件："+Path.GetFileName(path)).Append(ReportAttachment.Read(path));
                        if(content.Length>150000)throw new InvalidDataException("附件内容过长，请选择较小的报表。");
                    }
                    var user = new { role="user", content=content.ToString() };
                    messages.Add(user);
                    string key = AssistantKeys.Current();
                    using (var request = new HttpRequestMessage(HttpMethod.Post,"https://api.deepseek.com/chat/completions"))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",key);
                        request.Content = new StringContent(json.Serialize(new { model=Environment.GetEnvironmentVariable("MASTER_MIAO_DEEPSEEK_MODEL") ?? "deepseek-flash", messages, response_format=new { type="json_object" } }),Encoding.UTF8,"application/json");
                        using (var response = await client.SendAsync(request))
                        {
                            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("DeepSeek 返回 HTTP " + (int)response.StatusCode);
                            var answer = json.Deserialize<Completion>(await response.Content.ReadAsStringAsync()).choices.First().message.content;
                            var plan = json.Deserialize<Plan>(answer);
                            var suggestions = plan.tags ?? new Suggestion[0];
                            bool replace = plan.action == "replaceAll";
                            bool delete = plan.action == "delete";
                            bool deleteAll = replace || (delete && plan.all);
                            if (replace && suggestions.Length == 0)
                                throw new InvalidOperationException("重建方案缺少新标签，请重试。");
                            if (delete && suggestions.Length != 0)
                                throw new InvalidOperationException("删除方案包含新增标签，请重新生成。");
                            if (suggestions.Length != 0 && question.Contains("螺纹"))
                            {
                                var range = Regex.Match(question, @"\bM(\d+)\s*(?:到|至|[-–—~～])\s*M(\d+)", RegexOptions.IgnoreCase);
                                if (!range.Success) range = Regex.Match(question, @"M(\d+)\s*(?:到|至|[-–—~～])\s*M(\d+)", RegexOptions.IgnoreCase);
                                if (range.Success)
                                {
                                    int first = int.Parse(range.Groups[1].Value), last = int.Parse(range.Groups[2].Value);
                                    if (first < 1 || last < first || (long)last-first >= 100) throw new InvalidOperationException("请指定最多 100 个连续规格。");
                                    var missing = Enumerable.Range(first,last-first+1).Select(i=>"M"+i)
                                        .Where(size=>!suggestions.Any(t=>string.Equals(t.thread,size,StringComparison.OrdinalIgnoreCase)))
                                        .Select(size=>new Suggestion { name=size+" 螺纹孔", type="Threaded", thread=size,
                                            termination=suggestions.Select(t=>t.termination).Distinct().Count()==1?suggestions[0].termination:null });
                                    suggestions=suggestions.Concat(missing).ToArray();
                                }
                            }
                            string module = suggestions.Length > 0 || deleteAll || delete ? "holes" : plan.module;
                            if (!string.IsNullOrEmpty(module) && (!new[]{"holes","body-export","drawings","drawing-model"}.Contains(module) || !switchTo(module)))
                                throw new InvalidOperationException("当前无法切换模块，请先完成正在运行的任务。");
                            previewCatalog=json.Serialize(catalog.Items);
                            var names=plan.deleteNames ?? new string[0];
                            if(delete && !deleteAll && (names.Length==0 || names.Any(n=>!catalog.Items.Any(t=>string.Equals(t.Name,n,StringComparison.OrdinalIgnoreCase)))))
                                throw new InvalidOperationException("未找到要删除的标签，请核对名称。");
                            foreach(var tag in catalog.Items.Where(t=>deleteAll || (delete && names.Contains(t.Name,StringComparer.OrdinalIgnoreCase))).ToList())
                            {
                                removed.Add(tag.Id);
                                catalog.Items.Remove(tag);
                                log.AppendText("待删除："+tag.Name+"\n");
                            }
                            log.AppendText(suggestions.Length>0 || delete ? "助手：请核对本次标签变更。\n" : "助手：" + plan.reply + "\n");
                            if(suggestions.Length>100) throw new InvalidOperationException("每次最多支持 100 个标签。");
                            foreach (var item in suggestions)
                            {
                                var name = item.name?.Trim();
                                if (string.IsNullOrEmpty(name) || catalog.Items.Any(t=>string.Equals(t.Name,name,StringComparison.OrdinalIgnoreCase))) continue;
                                HoleSemanticType type;
                                if (!Enum.TryParse(item.type,true,out type) || !Enum.IsDefined(typeof(HoleSemanticType),type)) type=HoleSemanticType.Custom;
                                var tag = new HoleTagDefinition { GroupName=item.group,Name=name,HoleType=type,ThreadStandard="",ThreadSize=item.thread ?? "",Tolerance="" };
                                string termination=item.termination=="Through"?"通孔":item.termination=="Blind"?"盲孔":"";
                                if(termination.Length>0)
                                    tag.AnnotationTemplate=(string.IsNullOrEmpty(tag.ThreadSize)?"{Tag}":"{Thread}")+" "+termination;
                                catalog.AssignAvailableColor(tag);
                                catalog.Items.Add(tag);
                                pending.Add(tag);
                                log.SelectionColor=Color.FromArgb(tag.Red,tag.Green,tag.Blue);
                                log.AppendText("■ ");
                                log.SelectionColor=ForeColor;
                                log.AppendText("["+tag.Group+"] "+name + "  "+termination+"  #"+tag.Red.ToString("X2")+tag.Green.ToString("X2")+tag.Blue.ToString("X2")+"\n");
                            }
                            history.Add(user);
                            attachments.Clear();
                            while(chips.Controls.Count>0)chips.Controls[0].Dispose();
                            history.Add(new { role="assistant", content=answer });
                            if(history.Count>12) history.RemoveRange(0,2);
                        }
                    }
                }
                catch(Exception ex) { pending.Clear();removed.Clear();if(input.TextLength==0)input.Text=question; log.AppendText(ex.GetBaseException().Message+"\n"); }
                finally { attach.Enabled=chips.Enabled=send.Enabled=true; save.Text=removed.Count==0?"创建预览中的标签":"确认删除 "+removed.Count+" 项"+(pending.Count>0?"并创建 "+pending.Count+" 项":""); save.Visible=save.Enabled=pending.Count+removed.Count>0; }
            };
            save.Click += (s,e) =>
            {
                try
                {
                    var catalog=HoleTagCatalog.Load();
                    if(json.Serialize(catalog.Items)!=previewCatalog) throw new InvalidOperationException("标签库已变化，请重新生成预览。");
                    foreach(var old in catalog.Items.Where(t=>removed.Contains(t.Id)).ToList()) catalog.Items.Remove(old);
                    foreach(var tag in pending)
                    {
                        if(catalog.Items.Any(t=>string.Equals(t.Name,tag.Name,StringComparison.OrdinalIgnoreCase)) || catalog.FindByRgb(tag.Red,tag.Green,tag.Blue)!=null)
                            throw new InvalidOperationException("标签库已变化，请重新生成预览。");
                        catalog.Items.Add(tag);
                    }
                    catalog.Save();
                    log.AppendText("已删除 "+removed.Count+" 个标签，已创建 "+pending.Count+" 个标签。\n");
                    pending.Clear(); removed.Clear(); save.Visible=save.Enabled=false; refresh();
                }
                catch(Exception ex) { log.AppendText(ex.Message+"\n"); }
            };
        }
        public class Completion { public Choice[] choices {get;set;} }
        public class Choice { public Message message {get;set;} }
        public class Message { public string content {get;set;} }
        public class Plan { public string module {get;set;} public string reply {get;set;} public string action {get;set;} public bool all {get;set;} public string[] deleteNames {get;set;} public Suggestion[] tags {get;set;} }
        public class Suggestion { public string group {get;set;} public string name {get;set;} public string type {get;set;} public string thread {get;set;} public string termination {get;set;} }
    }
}
