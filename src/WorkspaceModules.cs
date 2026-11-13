using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DrawingForm = Blankedge.SWHoleTools.BatchExporter.MainForm;

namespace SWBodyOrganizer
{
    public interface IWorkspaceModule : IDisposable
    {
        string Id { get; }
        string Title { get; }
        Control View { get; }
        bool IsBusy { get; }
        bool CanClose();
    }

    public sealed class ModuleRegistry
    {
        private readonly List<Func<IWorkspaceModule>> factories = new List<Func<IWorkspaceModule>>();
        public void Register(Func<IWorkspaceModule> factory)
        {
            if (factory == null) throw new ArgumentNullException("factory");
            factories.Add(factory);
        }
        public IList<IWorkspaceModule> CreateModules()
        {
            var modules = new List<IWorkspaceModule>();
            try
            {
                foreach (var factory in factories)
                {
                    var module = factory();
                    if (module == null) throw new InvalidOperationException("Null module.");
                    modules.Add(module);
                    if (string.IsNullOrWhiteSpace(module.Id) || modules.Count(item => item.Id == module.Id) > 1)
                        throw new InvalidOperationException("Module IDs must be unique and nonempty.");
                }
                return modules;
            }
            catch { foreach (var module in modules) module.Dispose(); throw; }
        }
        public static ModuleRegistry BuiltIn()
        {
            var registry = new ModuleRegistry();
            registry.Register(() => new BodyExportModule());
            registry.Register(() => new HoleWorkspaceModule());
            registry.Register(() => new DrawingWorkspaceModule());
            registry.Register(() => new DrawingModelModule());
            return registry;
        }
    }

    internal static class EmbeddedForm
    {
        public static void Prepare(Form form)
        {
            form.TopLevel = false;
            form.FormBorderStyle = FormBorderStyle.None;
            form.ShowInTaskbar = false;
            form.Dock = DockStyle.Fill;
            form.MinimumSize = Size.Empty;
        }
    }

    internal sealed class BodyExportModule : IWorkspaceModule
    {
        private MainForm form;
        public string Id { get { return "body-export"; } }
        public string Title { get { return UiText.T("多实体导出", "Body Export"); } }
        public Control View
        {
            get
            {
                if (form == null)
                {
                    form = new MainForm();
                    EmbeddedForm.Prepare(form);
                    form.PrepareModuleLayout();
                }
                return form;
            }
        }
        public bool IsBusy { get { return form != null && form.ModuleBusy; } }
        public bool CanClose() { return form == null || form.ApproveModuleClose(); }
        public void Dispose() { if (form != null) { form.Close(); form.Dispose(); } }
    }

    internal sealed class DrawingWorkspaceModule : IWorkspaceModule
    {
        private DrawingForm drawing;
        public string Id => "drawings";
        public string Title => UiText.T("自动工程图", "Drawing Export");
        public bool IsBusy => drawing != null && drawing.IsRunning;
        public Control View
        {
            get
            {
                if (drawing == null)
                {
                    drawing = new DrawingForm();
                    EmbeddedForm.Prepare(drawing);
                }
                return drawing;
            }
        }
        public bool CanClose() => !IsBusy;
        public void Dispose() { drawing?.Dispose(); }
    }

    internal sealed class HoleWorkspaceModule : IWorkspaceModule
    {
        private HoleAssistantView view;
        public string Id => "holes";
        public string Title => UiText.T("孔标注助手", "Hole Assistant");
        public Control View => view ?? (view = new HoleAssistantView());
        public bool IsBusy => false;
        public bool CanClose() => true;
        public void Dispose() { view?.Dispose(); }
    }

    public sealed partial class MainForm
    {
        internal bool ModuleBusy { get { return worker.IsBusy || storageSaving || exportProgressDialog != null; } }
        internal void PrepareModuleLayout()
        {
            var root = Controls.OfType<TableLayoutPanel>().First();
            root.GetControlFromPosition(0, 0).Visible = false;
            root.RowStyles[0].Height = 0;
        }
        internal bool ApproveModuleClose()
        {
            if (ModuleBusy) return false;
            var args = new FormClosingEventArgs(CloseReason.UserClosing, false);
            HandleMainFormClosing(this, args);
            return !args.Cancel;
        }
    }
}
