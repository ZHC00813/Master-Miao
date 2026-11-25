using System;
using System.Linq;
using System.Runtime.InteropServices;
using Blankedge.SWHoleTools.Appearance;
using Blankedge.SWHoleTools.Attributes;
using Blankedge.SWHoleTools.Drawing;
using Blankedge.SWHoleTools.Geometry;
using Blankedge.SWHoleTools.Inspection;
using Blankedge.SWHoleTools.Model;
using Blankedge.SWHoleTools.Standards;
using Blankedge.SWHoleTools.Tags;
using Blankedge.SWHoleTools.UI;
using Blankedge.SWHoleTools.Utilities;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;

namespace Blankedge.SWHoleTools.Addin
{
    [Guid(AddinGuid)]
    [ProgId("Blankedge.SWHoleTools")]
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class SwAddin : ISwAddin
    {
        public const string AddinGuid = "6D68D92F-6A8C-45A0-9DCD-3B92AA5103D1";
        private const string AddinTitle = "喵师傅孔标注";
        private const string AddinDescription = "孔语义标记与自动工程图孔标注工具";

        private ISldWorks _application;
        private int _cookie;
        private CommandManagerController _commands;
        private ITaskpaneView _pane;
        private HoleFilterView _filter;
        private HolePropertyManager _propertyPage;
        private PersistentReferenceManager _persistentReferences;
        private HoleAttributeManager _attributes;
        private HoleColorManager _colors;
        private HoleDetector _detector;
        private HoleInspector _inspector;
        private DrawingHoleResolver _drawingResolver;
        private HoleAnnotationGenerator _annotationGenerator;
        private HoleTagCatalog _tagCatalog;

        public bool ConnectToSW(object thisSW, int cookie)
        {
            try
            {
                _application = thisSW as ISldWorks;
                _cookie = cookie;
                if (_application == null)
                {
                    Logger.Error("ConnectToSW 收到的应用对象无效。" );
                    return false;
                }

                var assembly = typeof(SwAddin).Assembly;
                Logger.Info($"插件加载：{assembly.Location}，构建 {assembly.ManifestModule.ModuleVersionId}。");
                if (!_application.SetAddinCallbackInfo2(0, this, cookie))
                {
                    Logger.Warn("SetAddinCallbackInfo2 返回 false。" );
                }

                _persistentReferences = new PersistentReferenceManager(new GeometricFaceFallbackResolver());
                _attributes = new HoleAttributeManager(_application);
                _attributes.EnsureDefinition();
                _colors = new HoleColorManager();
                _detector = new HoleDetector(_persistentReferences);
                _inspector = new HoleInspector(_detector, _attributes);
                _annotationGenerator = new HoleAnnotationGenerator();
                _tagCatalog = HoleTagCatalog.Load();
                _drawingResolver = new DrawingHoleResolver(_attributes, _persistentReferences);
                _propertyPage = new HolePropertyManager(
                    _application,
                    _attributes,
                    _colors,
                    new HoleStandardDatabase(),
                    _tagCatalog,
                    _annotationGenerator,
                    Notify);

                _commands = new CommandManagerController(_application, cookie);
                bool created = _commands.Create();
                Logger.Info("ConnectToSW 完成，CommandManager=" + created);
                return created;
            }
            catch (Exception ex)
            {
                Logger.Error("插件加载失败。", ex);
                return false;
            }
        }

        public bool DisconnectFromSW()
        {
            try
            {
                _pane?.DeleteView();
                _pane = null;
                _filter?.Dispose();
                _filter = null;
                _commands?.Dispose();
                Logger.Info("插件卸载。" );
            }
            catch (Exception ex)
            {
                Logger.Error("插件卸载过程中发生异常。", ex);
            }
            finally
            {
                _propertyPage = null;
                _commands = null;
                _drawingResolver = null;
                _annotationGenerator = null;
                _inspector = null;
                _detector = null;
                _colors = null;
                _attributes = null;
                _persistentReferences = null;
                _tagCatalog = null;
                _application = null;
                _cookie = 0;
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            return true;
        }

        public void OnRecognizeHole()
        {
            try
            {
                if (_pane == null)
                {
                    var icons = CommandIconProvider.EnsureCreated();
                    _pane = _application.CreateTaskpaneView3(icons.MainIcons, "喵师傅孔标注");
                    _filter = new HoleFilterView(() => _application);
                    if (!_pane.DisplayWindowFromHandlex64(_filter.Handle.ToInt64()))
                        throw new InvalidOperationException("筛选面板加载失败。");
                }
                _filter.ReloadTags();
                _pane.ShowView();
                var model = ActiveModel();
                if ((model?.SelectionManager as ISelectionMgr)?.GetSelectedObjectCount2(-1) > 0)
                {
                    try { _filter.Scan(); }
                    catch (Exception ex) { Logger.Error("当前选择不能扫描，保留孔助手面板。", ex); Notify(ex.Message); }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("打开孔筛选面板失败。", ex);
                Notify(ex.Message);
                _pane?.DeleteView();
                _pane = null;
                _filter?.Dispose();
                _filter = null;
            }
        }

        public void OnApplyMark()
        {
            IModelDoc2 model = ActiveModel();
            OperationResult<HoleAnalysis> analysis = _detector?.AnalyzeSelection(model);
            if (analysis == null || !analysis.Success)
            {
                Notify(analysis?.Error ?? "孔检测器尚未初始化。" );
                return;
            }

            HoleDefinition hole = analysis.Value.Definition;
            hole.ColorCode = hole.HasCounterbore
                ? HoleColorCode.Counterbore
                : hole.HasCountersink ? HoleColorCode.Countersink : HoleColorCode.Custom;
            hole.AnnotationText = _annotationGenerator.Generate(hole);
            OperationResult<HoleDefinition> saved = _attributes.Save(model, analysis.Value.Group, hole);
            if (!saved.Success)
            {
                Notify(saved.Error);
                return;
            }

            _colors.Apply(analysis.Value.Group, hole.ColorCode, model);
            Notify("已应用默认孔标记：" + hole.HoleID + "。可使用“修改标记”补充工程语义。" );
        }

        public void OnModifyMark()
        {
            IModelDoc2 model = ActiveModel();
            OperationResult<HoleAnalysis> analysis = _detector?.AnalyzeSelection(model);
            if (analysis == null || !analysis.Success)
            {
                Notify(analysis?.Error ?? "孔检测器尚未初始化。" );
                return;
            }

            HoleDefinition existing = analysis.Value.Group.Cylinders
                .Select(item => _attributes.Read(item.Face))
                .FirstOrDefault(item => item != null);
            if (existing == null)
            {
                Notify("所选孔尚未标记；请先使用“识别孔”或“应用标记”。" );
                return;
            }

            if (!_propertyPage.Show(model, analysis.Value, existing))
            {
                Notify("无法显示孔属性页，请查看日志：" + Logger.LogPath);
            }
        }

        public void OnClearMark()
        {
            IModelDoc2 model = ActiveModel();
            OperationResult<HoleAnalysis> analysis = _detector?.AnalyzeSelection(model);
            if (analysis == null || !analysis.Success)
            {
                Notify(analysis?.Error ?? "孔检测器尚未初始化。" );
                return;
            }

            int removed = _attributes.Clear(analysis.Value.Group);
            _colors.Clear(analysis.Value.Group, model);
            Notify(removed > 0 ? $"已清除 {removed} 个孔面上的标记。" : "所选孔没有可清除的标记。" );
        }

        public void OnInspectHoles()
        {
            HoleInspectionReport report = _inspector?.Scan(ActiveModel()) ?? new HoleInspectionReport();
            Notify("孔检查完成：" + report + "。详细信息已写入日志。" );
        }

        public void OnManageTags()
        {
            try
            {
                using (var form = new TagManagerForm(_tagCatalog ?? (_tagCatalog = HoleTagCatalog.Load())))
                {
                    form.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("打开标签管理器失败。", ex);
                Notify("无法打开标签管理器：" + ex.Message);
            }
        }

        public int CanManageTags() => 1;

        public void OnAutoAnnotate()
        {
            IModelDoc2 drawing = ActiveModel();
            if (drawing == null || drawing.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                Notify("请先打开并激活工程图文档。" );
                return;
            }

            var matches = _drawingResolver.Resolve(drawing);
            DrawingHoleMatch first = matches.FirstOrDefault(item => item.DrawingEntity != null) ?? matches.FirstOrDefault();
            if (first == null)
            {
                Notify("当前工程图视图中未找到可解析的 Blankedge 孔标记。" );
                return;
            }

            bool created = _annotationGenerator.CreateSimpleNote(drawing, first);
            Notify(created
                ? "已为第一个匹配孔创建基础 Note 示例；原生 Hole Callout 与 Leader 将在下一阶段实现。"
                : "创建基础工程图 Note 失败，请查看日志。" );
        }

        public int CanRunHoleCommand()
        {
            try { var model = ActiveModel(); return model is IPartDoc || model is IAssemblyDoc ? 1 : 0; }
            catch { return 0; }
        }

        public int CanRunPartCommand()
        {
            try
            {
                return ActiveModel()?.GetType() == (int)swDocumentTypes_e.swDocPART ? 1 : 0;
            }
            catch
            {
                return 0;
            }
        }

        public int CanRunDrawingCommand()
        {
            try
            {
                return ActiveModel()?.GetType() == (int)swDocumentTypes_e.swDocDRAWING ? 1 : 0;
            }
            catch
            {
                return 0;
            }
        }

        [ComRegisterFunction]
        public static void Register(Type type)
        {
            try
            {
                using (RegistryKey addin = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\SolidWorks\Addins\{" + AddinGuid + "}"))
                {
                    addin?.SetValue(null, 0, RegistryValueKind.DWord);
                    addin?.SetValue("Title", AddinTitle, RegistryValueKind.String);
                    addin?.SetValue("Description", AddinDescription, RegistryValueKind.String);
                }
                using (RegistryKey startup = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\SolidWorks\AddInsStartup\{" + AddinGuid + "}"))
                {
                    // Re-registration must preserve the user's startup preference.
                    if (startup != null && startup.GetValue(null) == null)
                    {
                        startup.SetValue(null, 0, RegistryValueKind.DWord);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("无法写入当前用户的 SOLIDWORKS Add-in 注册信息。", ex);
            }
        }

        [ComUnregisterFunction]
        public static void Unregister(Type type)
        {
            Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\SolidWorks\Addins\{" + AddinGuid + "}", false);
            Registry.CurrentUser.DeleteSubKeyTree(@"SOFTWARE\SolidWorks\AddInsStartup\{" + AddinGuid + "}", false);
        }

        private IModelDoc2 ActiveModel()
        {
            try
            {
                return _application?.IActiveDoc2;
            }
            catch (Exception ex)
            {
                Logger.Error("读取活动文档失败。", ex);
                return null;
            }
        }

        private void Notify(string message)
        {
            try
            {
                _application?.SendMsgToUser2(
                    message,
                    (int)swMessageBoxIcon_e.swMbInformation,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
            catch (Exception ex)
            {
                Logger.Error("显示用户消息失败：" + message, ex);
            }
        }

        private static string SafeTitle(IModelDoc2 model)
        {
            try
            {
                return model?.GetTitle() ?? "<无文档>";
            }
            catch
            {
                return "<读取失败>";
            }
        }
    }
}
