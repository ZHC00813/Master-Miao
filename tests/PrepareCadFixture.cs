using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// TEST ONLY: normalize a separate copy, never the user's source model.
internal static class PrepareCadFixture
{
    private static string Hash(string path) { using (SHA256 hash = SHA256.Create()) using (Stream stream = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(stream)); }
    [STAThread]
    private static int Main(string[] args)
    {
        string source = Path.GetFullPath(args[0]), target = Path.GetFullPath(args[1]);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase) || File.Exists(target)) throw new IOException("Fixture must be a NEW separate file.");
        if (Process.GetProcessesByName("SLDWORKS").Length != 0) throw new InvalidOperationException("Fixture preparation requires no existing SolidWorks session.");
        string before = Hash(source);
        Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(source, target, false);
        ISldWorks app = null; IModelDoc2 model = null;
        try
        {
            app = (ISldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application", true));
            app.Visible = false;
            int errors = 0, warnings = 0;
            model = app.OpenDoc6(target, (int)swDocumentTypes_e.swDocPART, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings);
            if (model == null) throw new IOException("Fixture open failed: " + errors);
            Console.WriteLine("Fixture dirty before save: " + model.GetSaveFlag());
            bool saved = model.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
            Console.WriteLine("Fixture saved: " + saved + "; errors=" + errors + "; warnings=" + warnings + "; dirty=" + model.GetSaveFlag());
            if (!saved || errors != 0) throw new IOException("Fixture save failed.");
            string title = model.GetTitle(); Marshal.ReleaseComObject(model); model = null; app.CloseDoc(title);
            model = app.OpenDoc6(target, (int)swDocumentTypes_e.swDocPART, (int)(swOpenDocOptions_e.swOpenDocOptions_Silent | swOpenDocOptions_e.swOpenDocOptions_ReadOnly), "", ref errors, ref warnings);
            if (model == null || model.GetSaveFlag()) throw new IOException("Normalized fixture is not clean on reopening.");
            Console.WriteLine("PASS: clean read-only reopen; original unchanged=" + (Hash(source) == before));
            return Hash(source) == before ? 0 : 4;
        }
        finally
        {
            if (model != null) Marshal.ReleaseComObject(model);
            if (app != null) { app.CloseAllDocuments(true); app.ExitApp(); Marshal.ReleaseComObject(app); }
        }
    }
}
