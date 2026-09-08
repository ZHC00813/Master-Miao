using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using SWBodyOrganizer;

internal static class VerifyExcelReport
{
    private static void Require(bool test, string message) { if (!test) throw new Exception(message); }
    private static XDocument Xml(ZipArchive zip, string path) { using (var stream = zip.GetEntry(path).Open()) return XDocument.Load(stream); }
    private static string Value(XDocument sheet, string cell)
    {
        XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return string.Concat(sheet.Descendants(s + "c").Where(c => (string)c.Attribute("r") == cell).Descendants(s + "t").Select(t => t.Value));
    }
    private static int Main(string[] args)
    {
        try
        {
            string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
            var images = new List<string>();
            int[,] sizes = { {420,300}, {160,640}, {900,120} };
            for (int i=0;i<3;i++)
            {
                string path=Path.Combine(output,"ratio-"+i+".png");
                using(var image=new Bitmap(sizes[i,0],sizes[i,1]))
                using(var g=Graphics.FromImage(image))
                {
                    g.Clear(Color.White); g.DrawEllipse(Pens.Red, 4,4,Math.Min(sizes[i,0],sizes[i,1])-8,Math.Min(sizes[i,0],sizes[i,1])-8);
                    image.Save(path,System.Drawing.Imaging.ImageFormat.Png);
                }
                images.Add(path);
            }
            var rows = new List<ExportResultItem>();
            string[] statuses={"本次成功","跳过但未验证","失败","取消","未执行","沿用且已验证"};
            for(int i=0;i<statuses.Length;i++) rows.Add(new ExportResultItem {
                ExportName="实际件_2", PlannedExportName="计划件", SourceName="零件.SLDPRT", SourcePath="C:\\Design\\零件.SLDPRT",
                Quantity=2, Occurrences=new List<string>{"C:\\Design\\a.SLDPRT / 实体一","C:\\Design\\b.SLDPRT / 实体二"},
                PreviewIso=images[0],PreviewFront=images[1],PreviewTop=images[2],Outcome=statuses[i],SldprtStatus=statuses[i],
                StepStatus="失败",AssemblyStepStatus="未执行",Message="=not a formula; STEP reopen failed",SldprtVerification="几何校验通过",StepVerification="文件头通过"
            });
            // A missing image remains a readable placeholder rather than a broken drawing.
            rows.Add(new ExportResultItem{ExportName="缺图件", Quantity=1,Outcome="未执行"});
            var method=typeof(AppProject).Assembly.GetType("SWBodyOrganizer.ExcelReportWriter").GetMethod("Create",BindingFlags.Static|BindingFlags.Public);
            foreach(string lang in new[]{"zh-CN","en-US"})
            {
                string path=Path.Combine(output,"report-"+lang+".xlsx");
                method.Invoke(null,new object[]{path,rows,"V1.2.6 报表验收",output,lang});
                using(var zip=ZipFile.OpenRead(path))
                {
                    var sheet=Xml(zip,"xl/worksheets/sheet1.xml"); var drawing=Xml(zip,"xl/drawings/drawing1.xml");
                    XNamespace x="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
                    XNamespace s="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                    var extents=drawing.Descendants(x+"ext").ToList(); Require(extents.Count==18,"Three views per populated row");
                    for(int i=0;i<3;i++)
                    {
                        double actual=(double)extents[i].Attribute("cx")/(double)extents[i].Attribute("cy");
                        Require(Math.Abs(actual-(double)sizes[i,0]/sizes[i,1])<0.0001,"Image aspect ratio changed");
                    }
                    // Render directly from saved OOXML anchors/media as an independent image check.
                    using(var canvas=new Bitmap(342,96))
                    using(var g=Graphics.FromImage(canvas))
                    {
                        g.Clear(Color.White);
                        for(int i=0;i<3;i++)
                        {
                            var anchor=extents[i].Parent; var from=anchor.Element(x+"from");
                            using(var media=zip.GetEntry("xl/media/image"+(i+1)+".png").Open())
                            using(var image=Image.FromStream(media))
                                g.DrawImage(image,i*114+(float)((double)from.Element(x+"colOff")/9525),
                                    (float)((double)from.Element(x+"rowOff")/9525),
                                    (float)((double)extents[i].Attribute("cx")/9525),(float)((double)extents[i].Attribute("cy")/9525));
                            g.DrawRectangle(Pens.LightGray,i*114,0,113,95);
                        }
                        canvas.Save(Path.Combine(output,"embedded-image-layout.png"),System.Drawing.Imaging.ImageFormat.Png);
                    }
                    Require(Value(sheet,"D3")=="实际件_2" && Value(sheet,"P3")=="计划件","Actual/planned names lost");
                    Require(Value(sheet,"W3").Contains("实体二"),"All occurrence provenance lost");
                    Require(Value(sheet,"R3")== (lang=="en-US"?"Failed":"失败"),"STEP failure hidden");
                    Require(Value(sheet,"A9").Length>0,"Missing preview not labeled");
                    Require(sheet.Descendants(s+"f").Count()==0,"Literal notes became formulas");
                    Require(sheet.Descendants(s+"c").Single(c=>(string)c.Attribute("r")=="H3").Element(s+"v").Value=="2","Quantity must stay numeric");
                    Require((string)sheet.Descendants(s+"autoFilter").Single().Attribute("ref")=="A2:Y9","Filter coverage");
                    Require((string)sheet.Descendants(s+"pane").Single().Attribute("state")=="frozen","Frozen headers lost");
                }
                byte[] before=File.ReadAllBytes(path);
                using(var locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
                {
                    bool rejected=false;try{method.Invoke(null,new object[]{path,rows,"overwrite",output,lang});}catch(TargetInvocationException e){rejected=e.InnerException is IOException;}
                    Require(rejected,"Locked report unexpectedly overwritten");
                }
                Require(before.SequenceEqual(File.ReadAllBytes(path)),"Failed overwrite damaged previous workbook");
            }
            Console.WriteLine("PASS: three image ratios, per-format outcomes, all occurrences, literal notes, numeric quantity, filter/freeze, missing preview and locked overwrite survival.");
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
