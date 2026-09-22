using System;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
namespace SWBodyOrganizer
{
    internal sealed class ImportTemplateScope : IDisposable
    {
        private readonly ISldWorks app;
        private readonly string part, assembly;
        private readonly bool always, interconnect, multibody;
        private readonly int mapping;
        public ImportTemplateScope(ISldWorks application)
        {
            app = application;
            part = app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            assembly = app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplateAssembly);
            always = app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swAlwaysUseDefaultTemplates);
            interconnect = app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swMultiCAD_Enable3DInterconnect);
            multibody = app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swImportMultBodyAsPartData);
            mapping = app.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swImportNeutralAssemblyStructureMapping);
            string validPart = WorkerMain.FindPartTemplate(app), validAssembly = WorkerMain.FindAssemblyTemplate(app);
            if (!File.Exists(validPart) || !File.Exists(validAssembly)) throw new IOException("STEP 校验缺少有效的零件或装配体模板，请在 SolidWorks 设置默认模板。 / Valid part and assembly templates are required for STEP verification.");
            try
            {
                if (!app.SetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart, validPart) ||
                    !app.SetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplateAssembly, validAssembly))
                    throw new IOException("无法设置 STEP 校验模板。 / Cannot set STEP import templates.");
                app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swAlwaysUseDefaultTemplates, true);
                // Verification needs all solids at their original coordinates, not
                // imported assembly documents that can prompt for a new template.
                app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swMultiCAD_Enable3DInterconnect, true);
                app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swImportMultBodyAsPartData, true);
                app.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swImportNeutralAssemblyStructureMapping, 2);
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            // Restore all three preferences even if one restoration fails.
            try { app.SetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart, part); }
            finally { try { app.SetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplateAssembly, assembly); }
                finally { try { app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swAlwaysUseDefaultTemplates, always); }
                    finally { try { app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swMultiCAD_Enable3DInterconnect, interconnect); }
                        finally { try { app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swImportMultBodyAsPartData, multibody); }
                            finally { app.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swImportNeutralAssemblyStructureMapping, mapping); } } } } }
        }
    }
}
