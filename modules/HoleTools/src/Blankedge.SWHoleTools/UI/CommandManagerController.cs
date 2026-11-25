using System;
using System.Collections.Generic;
using Blankedge.SWHoleTools.Utilities;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Blankedge.SWHoleTools.UI
{
    public sealed class CommandManagerController : IDisposable
    {
        private const int CommandGroupId = 0x4B4801;
        private const int FaceMenuGroupId = 0x4B4802;
        private const string TabTitle = "Hole Tools";

        private readonly ISldWorks _application;
        private readonly int _cookie;
        private readonly IList<CommandTab> _tabs = new List<CommandTab>();
        private ICommandManager _manager;
        private ICommandGroup _faceMenu;

        public CommandManagerController(ISldWorks application, int cookie)
        {
            _application = application ?? throw new ArgumentNullException(nameof(application));
            _cookie = cookie;
        }

        public bool Create()
        {
            try
            {
                _manager = _application.GetCommandManager(_cookie) as ICommandManager;
                if (_manager == null)
                {
                    return false;
                }

                int errors = 0;
                var group = _manager.CreateCommandGroup2(
                    CommandGroupId,
                    TabTitle,
                    "孔语义标记与工程图标注",
                    "喵师傅孔标注",
                    -1,
                    false,
                    ref errors) as ICommandGroup;

                if (group == null)
                {
                    Logger.Error("CommandGroup 创建失败，错误码：" + errors);
                    return false;
                }

                CommandIconSet icons = CommandIconProvider.EnsureCreated();
                if (icons == null)
                {
                    Logger.Error("Toolbar 图标不可用，无法安全创建 CommandManager Tab。" );
                    return false;
                }
                group.MainIconList = icons.MainIcons;
                group.IconList = icons.CommandStrips;

                int commandType = (int)swCommandItemType_e.swMenuItem | (int)swCommandItemType_e.swToolbarItem;
                int[] indices =
                {
                    group.AddCommandItem2("喵师傅孔标注", -1, "读取 SW 当前所选孔面并设置标签与 RGB 颜色", "喵师傅孔标注", 0, "OnRecognizeHole", "CanRunHoleCommand", 1001, commandType),
                    group.AddCommandItem2("应用标记", -1, "分析并写入默认孔标记", "应用标记", 1, "OnApplyMark", "CanRunPartCommand", 1002, commandType),
                    group.AddCommandItem2("修改标记", -1, "修改所选孔已有标记", "修改标记", 2, "OnModifyMark", "CanRunPartCommand", 1003, commandType),
                    group.AddCommandItem2("清除标记", -1, "清除所选孔的 Attribute 和颜色", "清除标记", 3, "OnClearMark", "CanRunPartCommand", 1004, commandType),
                    group.AddCommandItem2("孔检查", -1, "扫描当前零件中的潜在孔", "孔检查", 4, "OnInspectHoles", "CanRunPartCommand", 1005, commandType),
                    group.AddCommandItem2("自动工程图标注", -1, "创建第一阶段基础孔 Note", "自动工程图标注", 5, "OnAutoAnnotate", "CanRunDrawingCommand", 1006, commandType),
                    group.AddCommandItem2("标签管理", -1, "新建或编辑颜色与孔语义标签", "标签管理", 6, "OnManageTags", "CanManageTags", 1007, commandType)
                };

                group.HasToolbar = true;
                group.HasMenu = true;
                if (!group.Activate())
                {
                    Logger.Warn("CommandGroup Activate 返回 false。" );
                }

                int[] commandIds = new int[indices.Length];
                for (int i = 0; i < indices.Length; i++)
                {
                    commandIds[i] = group.CommandID[indices[i]];
                }

                AddTab((int)swDocumentTypes_e.swDocPART, commandIds);
                AddTab((int)swDocumentTypes_e.swDocASSEMBLY, commandIds);
                AddTab((int)swDocumentTypes_e.swDocDRAWING, commandIds);
                AddFaceMenu(icons);
                Logger.Info("Hole Tools CommandManager 已创建。" );
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("创建 CommandManager 失败。", ex);
                return false;
            }
        }

        public void Dispose()
        {
            if (_manager == null)
            {
                return;
            }

            try
            {
                foreach (CommandTab tab in _tabs)
                {
                    _manager.RemoveCommandTab(tab);
                }
                _tabs.Clear();
                if (_faceMenu != null)
                {
                    _manager.RemoveCommandGroup2(FaceMenuGroupId, true);
                    _faceMenu = null;
                }
                _manager.RemoveCommandGroup2(CommandGroupId, true);
            }
            catch (Exception ex)
            {
                Logger.Error("移除 CommandManager 失败。", ex);
            }
            finally
            {
                _manager = null;
            }
        }

        private void AddFaceMenu(CommandIconSet icons)
        {
            try
            {
                _faceMenu = _manager.AddContextMenu(FaceMenuGroupId, "喵师傅孔标注");
                if (_faceMenu == null)
                {
                    Logger.Warn("创建面的右键菜单失败。");
                    return;
                }
                _faceMenu.SelectType = (int)swSelectType_e.swSelFACES;
                _faceMenu.MainIconList = icons.MainIcons;
                _faceMenu.IconList = icons.CommandStrips;
                int index = _faceMenu.AddCommandItem2("喵师傅孔标注", -1, "标注所选孔内壁",
                    "喵师傅孔标注", 0, "OnRecognizeHole", "CanRunHoleCommand", 2001,
                    (int)swCommandItemType_e.swMenuItem);
                _faceMenu.HasToolbar = false;
                _faceMenu.HasMenu = true;
                if (index < 0 || !_faceMenu.Activate())
                    Logger.Warn("面的右键孔标注命令未激活。");
                else
                    Logger.Info("面的右键孔标注命令已激活。");
            }
            catch (Exception ex)
            {
                Logger.Error("创建面的右键孔标注命令失败。", ex);
            }
        }

        private void AddTab(int documentType, int[] commandIds)
        {
            CommandTab existing = _manager.GetCommandTab(documentType, TabTitle);
            if (existing != null)
            {
                _manager.RemoveCommandTab(existing);
            }

            CommandTab tab = _manager.AddCommandTab(documentType, TabTitle);
            var box = tab?.AddCommandTabBox() as ICommandTabBox;
            if (tab == null || box == null)
            {
                return;
            }

            var textStyles = new int[commandIds.Length];
            for (int i = 0; i < textStyles.Length; i++)
            {
                textStyles[i] = (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextHorizontal;
            }
            box.AddCommands(commandIds, textStyles);
            _tabs.Add(tab);
        }
    }
}
