using System;using System.IO;using System.Reflection;using System.Runtime.CompilerServices;using System.Windows.Forms;using SWBodyOrganizer;using MasterMiao.Pcb;
[assembly:AssemblyVersion("1.4.8.0")]
[assembly:AssemblyFileVersion("1.4.8.0")]
[assembly:AssemblyProduct("喵师傅 Master Miao")]
class ReleaseLauncher{
[STAThread]static int Main(){try{
Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Core","MasterMiao.exe"));
return Run();
}catch(Exception e){MessageBox.Show(e.GetBaseException().Message,"喵师傅启动失败");return 1;}}
[MethodImpl(MethodImplOptions.NoInlining)]static int Run(){
Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
var assembly=typeof(ModuleRegistry).Assembly;
foreach(string name in new[]{"AppPaths","UserSettingsStore"}){var type=assembly.GetType("SWBodyOrganizer."+name,true);type.GetMethod(name=="AppPaths"?"Ensure":"Load",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);}
var registry=ModuleRegistry.BuiltIn();registry.Register(()=>new PcbModule());var shell=new WorkspaceShell(registry);shell.Text="喵师傅 Master Miao · V1.4.8";Application.Run(shell);return 0;
}}
