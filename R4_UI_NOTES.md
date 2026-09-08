# 0905-R4 界面优化 / UI refinement

版本 / Version: `V1.2.6 · 0905-R4` — FileVersion `1.2.6.4`.

## 范围 / Scope

用户明确要求：只优化 UI、交互和界面设计，保留已经顺畅的读取与导出，不再消耗 SolidWorks 资源做重复导出实验。因此 R4 基于本轮已完成的 R3（含问题导航、快捷键、进度弹窗、仅 STEP）制作独立副本；原版本、CAD、工作记录不覆盖、不迁移、不删除。未发布 GitHub，未修改系统配置。

The user asked for UI and interaction refinement while preserving the working CAD pipeline and avoiding repeated export experiments. R4 starts from the completed R3 revision, retaining issue navigation, shortcuts, modal progress and STEP-only delivery. It uses an independent copy; old versions, CAD and work records were not overwritten, migrated or deleted. No GitHub publication or system configuration changes occurred.

## 界面变化 / Interface changes

- 字体与层次：常规字体从 9.75pt 提升到 10.5pt，辅助字体从 9pt 提升到 9.75pt。列表缩放同步；交替浅色行、深色文字、浅红选中态、32px以上操作按钮。名称/分类列更宽，选择列横向滚动时固定。
- 工具栏：SolidWorks 状态移到独立状态行，设置、重新读取等按钮在1100px窗口中保持可见。常用整理动作与选择/缩放工具分两行；保留一键全选，全不选/反选位于“选择”，批量命名/重复审查/重试位于“更多”。仅改变入口布局，调用原操作。
- 工作空间：“侧栏”按钮收起/恢复预览和分类区，给列表让出宽度。右侧预览标题更清晰；长名称、详情、状态、STEP目录说明有悬停完整提示。
- 分类：模板操作换行排列，不再横向裁切。点击“展开”在可缩放的大窗口中编辑目录树/关系图，沿用同一个控件和分类规则；关闭后返回原侧栏，选择与编辑状态保留。
- 导出区：路径、格式、STEP分类/合并、冲突与结果操作分行，扩大路径输入区域；安全说明单独成按钮。所有原格式开关、授权、取消和导出逻辑保持不变。
- 逐项整理：加大编辑区与标签宽度，强调红色“保存并下一个”，增加分类完成度条。名称、分类、选择的保存时机与原规则不变。
- 问题提示：原因与导航按钮分行，避免英文按钮遮挡。新增界面文字同步中英；用户自己命名的零件/分类不自动翻译。

Typography increases to 10.5pt for normal text and 9.75pt for secondary text. Lists use alternating rows, dark readable text, soft red selection and wider name/category columns; selection stays frozen while scrolling. Buttons are at least 32px high. SolidWorks status moves out of the toolbar, keeping core controls visible at 1100px. Frequent actions and selection/zoom tools occupy separate rows: Select all remains one click, Selection groups none/invert, and More groups batch naming, duplicate review and retries. Each entry calls the existing action.

Sidebar hides/restores preview and classification to widen the list. Preview headings are clearer; tooltips expose full truncated names, details, status and STEP-path explanations. Folder template controls wrap; Expand opens the same category tree/map in a resizable workspace and returns it intact afterward. Export controls are arranged by purpose without changing their logic. Guided mode enlarges editing fields, emphasizes Save and next, and shows classification progress without changing commit semantics. Issue text and navigation occupy separate rows. UI strings support both languages; user-defined names are never translated automatically.

## 验证 / Validation

1. `tests/VerifyR4Scope.ps1` 对比 R3：SolidWorksWorker、AssemblyStepExporter、ExportIntegrity、Models、ProjectStore、ExcelReportWriter、FolderCanvas、STEP宏、构建脚本的 SHA-256 完全一致。界面文件内的读取、导出、授权、取消、导出校验/选项、异常恢复、重命名提交和逐项保存等关键方法正文一致。
2. `tests/VerifyR4Ui.cs` 加载现有94实体项目及已保存缩略图，检查中英文1540×920和1100×720实际控件边界、工具栏/页脚可见、侧栏收放、菜单选择、模板按钮、关系图展开及返回、紧凑与逐项显示。原项目文件前后逐字节一致，未连接 SolidWorks。
3. 实际 WinForms 消息循环回归通过：问题导航、快捷键、模态进度、名称提交、Enter/完成/Esc、已保存/未保存状态跨4次自动保存、逐项按钮保存与回列表、去重折叠还原。
4. 已检查双语截图。不同实际显示器的125%/150% Windows DPI组合和原生中文输入法候选窗尚未全面实机覆盖；列表内80%–200%缩放沿用原功能。

The scope audit confirms byte-identical backend files and unchanged protected workflow/commit methods against R3. Real WinForms layout tests loaded 94 existing records and saved previews at 1540×920 and 1100×720 in both languages, checking bounds, tool visibility, sidebar toggling, menus, folder workspace restoration and guided/compact views. The fixture stayed byte-identical; SolidWorks was not connected. Existing UI message-loop tests passed for navigation, shortcuts, modal progress, editing/Enter/Esc, autosave, guided persistence and reversible folding. Bilingual screenshots were reviewed. Hardware-specific Windows DPI combinations and native IME candidate windows are not fully covered.

本轮按用户要求没有重新运行真实 STEP/装配体导出实验，也不把 UI 截图当作 CAD 成功证据。R3新增“仅 STEP”的后台实机验收边界见 [R3_CHANGES_AND_TESTS.md](R3_CHANGES_AND_TESTS.md)。

No real STEP/assembly export experiment was repeated, as requested. UI screenshots are not CAD-output evidence. The validation boundary for R3's new STEP-only option remains documented in the R3 record.

## 运行与继续原项目 / Run and resume

完整解压后运行 `MasterMiao.exe`，确认标题含 `0905-R4`。切换前先在旧版“保存项目”，再在新版“打开项目”继续。新软件目录的便携式 Data 与旧版分开，启动空白界面不代表原记录丢失；不要用新目录覆盖旧目录或删除旧 Data。源码在 `Source`，文档保留中英开发历程。

Extract the complete package and run MasterMiao.exe; check 0905-R4 in its title. Save the project in the old program before switching, then use Open project in R4. Portable Data is separate per installation: an empty new window does not mean previous records were lost. Do not overwrite the old folder or delete its Data. Source and bilingual development history are included.

开发者可只运行界面回归 / UI-only regression:

```powershell
.\build.ps1
.\tests\RunR3Regression.ps1 -OutputDirectory C:\YourSandbox\R4-Ui-New -UiOnly
.\tests\VerifyR4Scope.ps1 -BaselineSource C:\YourSandbox\R3\Source
```

输出测试目录必须为新目录。包内不含用户 CAD、分类记录或测试生成的日志/截图。

Test output must use a new directory. The release contains no user CAD, work records or generated test logs/screenshots.
