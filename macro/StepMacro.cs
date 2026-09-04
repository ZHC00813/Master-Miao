using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SolidWorks.Interop.sldworks;

namespace SWBodyOrganizerStepMacro
{
    [Guid("7C850B7A-43F4-4F8E-8644-D18E28D28471")]
    public sealed class SolidWorksMacro
    {
        public SldWorks swApp;

        public void Main()
        {
            string jobPath = Path.Combine(Path.GetTempPath(), "SWBodyOrganizer.StepJob." + swApp.GetProcessID().ToString(CultureInfo.InvariantCulture) + ".txt");
            string[] lines = File.ReadAllLines(jobPath, Encoding.UTF8);
            if (lines.Length < 2) throw new InvalidDataException("STEP job file is incomplete.");

            string logPath = Decode(lines[0]);
            string originalActiveTitle = Decode(lines[1]);
            List<string> log = new List<string>();
            bool originalAtomic = false;
            bool preferenceRead = false;
            try
            {
                originalAtomic = swApp.GetUserPreferenceToggle(786);
                preferenceRead = true;
                swApp.SetUserPreferenceToggle(786, true);
                log.Add("BEGIN|atomicOriginal=" + originalAtomic.ToString(CultureInfo.InvariantCulture));
                for (int index = 2; index < lines.Length; index++)
                {
                    string[] fields = lines[index].Split(new[] { '\t' }, 2);
                    string result = fields.Length == 2 ? ExportOne(Decode(fields[0]), Decode(fields[1])) : "INVALID_JOB|0|0";
                    log.Add("ITEM|" + (index - 2).ToString(CultureInfo.InvariantCulture) + "|" + result);
                }
            }
            catch (Exception ex)
            {
                log.Add("FATAL|" + ex.HResult.ToString(CultureInfo.InvariantCulture) + "|" + Clean(ex.Message));
            }
            finally
            {
                if (preferenceRead)
                {
                    try { swApp.SetUserPreferenceToggle(786, originalAtomic); } catch { }
                }
                if (!string.IsNullOrWhiteSpace(originalActiveTitle))
                {
                    try { int errors = 0; swApp.ActivateDoc3(originalActiveTitle, false, 0, ref errors); } catch { }
                }
                bool restored = false;
                try { restored = !preferenceRead || swApp.GetUserPreferenceToggle(786) == originalAtomic; } catch { }
                log.Add("RESTORED|" + restored.ToString(CultureInfo.InvariantCulture));
                log.Add("DONE");
                try { File.WriteAllLines(logPath, log.ToArray(), Encoding.UTF8); } catch { }
            }
        }

        private string ExportOne(string assemblyPath, string outputPath)
        {
            ModelDoc2 model = null;
            string title = string.Empty;
            try
            {
                int openErrors = 0, openWarnings = 0;
                model = swApp.OpenDoc6(assemblyPath, 2, 1, string.Empty, ref openErrors, ref openWarnings) as ModelDoc2;
                if (model == null) return "OPEN_FAILED|" + openErrors.ToString(CultureInfo.InvariantCulture) + "|" + openWarnings.ToString(CultureInfo.InvariantCulture);
                title = model.GetTitle();
                int activateErrors = 0;
                swApp.ActivateDoc3(title, false, 0, ref activateErrors);
                ModelDoc2 active = swApp.ActiveDoc as ModelDoc2;
                if (active == null) return "INTERFERENCE|NO_ACTIVE_DOCUMENT|0";
                if (!string.Equals(active.GetTitle(), title, StringComparison.OrdinalIgnoreCase)) return "INTERFERENCE|ACTIVE_DOCUMENT_CHANGED|0";
                model.ClearSelection2(true);
                int saveErrors = 0, saveWarnings = 0;
                bool saved = model.Extension.SaveAs(outputPath, 0, 1, null, ref saveErrors, ref saveWarnings);
                active = swApp.ActiveDoc as ModelDoc2;
                if (active == null) return "INTERFERENCE|NO_ACTIVE_DOCUMENT_AFTER_SAVE|0";
                if (!string.Equals(active.GetTitle(), title, StringComparison.OrdinalIgnoreCase)) return "INTERFERENCE|ACTIVE_DOCUMENT_CHANGED_AFTER_SAVE|0";
                return (saved && File.Exists(outputPath) ? "OK" : "SAVE_FAILED") + "|" + saveErrors.ToString(CultureInfo.InvariantCulture) + "|" + saveWarnings.ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                return "ERROR|" + ex.HResult.ToString(CultureInfo.InvariantCulture) + "|" + Clean(ex.Message);
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(title)) try { swApp.CloseDoc(title); } catch { }
            }
        }

        private static string Decode(string value)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value ?? string.Empty));
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Replace("|", "/").Replace("\r", " ").Replace("\n", " ");
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public string Execute(string debug)
        {
            try
            {
                if (swApp == null) throw new NullReferenceException("SolidWorksMacro.swApp is null.");
                Main();
                return string.Empty;
            }
            catch (Exception ex)
            {
                return ex.ToString();
            }
            finally
            {
                swApp = null;
            }
        }
    }
}
