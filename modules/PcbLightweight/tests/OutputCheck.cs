using System;using System.IO;using System.Linq;using MasterMiao.Pcb;
class OutputCheck{
 static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
 static void Main(string[] args){string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);string source=Path.Combine(root,"staged.stp");File.WriteAllText(source,"step fixture");File.WriteAllText(Path.ChangeExtension(source,"SLDPRT"),"part fixture");
 foreach(int mode in new[]{1,2,3}){string dir=Path.Combine(root,"mode"+mode);Directory.CreateDirectory(dir);var paths=PcbOutputs.Publish(source,Path.Combine(dir,"模型.SLDPRT"),(mode&1)!=0,(mode&2)!=0);Check(paths.Length==(mode==3?2:1)&&Directory.GetFiles(dir).Length==paths.Length,"selected formats only "+mode);}
 bool rejected=false;try{PcbOutputs.Targets(source,false,false);}catch(ArgumentException){rejected=true;}Check(rejected,"empty format selection rejected");
 string collision=Path.Combine(root,"mode3","模型.stp");rejected=false;try{PcbOutputs.Publish(source,collision,true,true);}catch(IOException){rejected=true;}Check(rejected&&File.ReadAllText(collision)=="step fixture","existing output preserved");
 File.Delete(Path.ChangeExtension(source,"SLDPRT"));string failed=Path.Combine(root,"partial.stp");rejected=false;try{PcbOutputs.Publish(source,failed,true,true);}catch(IOException){rejected=true;}Check(rejected&&!File.Exists(failed),"partial output rollback");}
}
