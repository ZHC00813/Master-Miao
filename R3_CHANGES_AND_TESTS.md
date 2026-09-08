# 0905-R3 修改与测试 / Changes and tests

日期 / Date: 2026-09-05. 文件版本 / File version: `1.2.6.3`.

## 基线与范围 / Baseline and scope

基于用户提供的 `Master-Miao-v1.2.6.zip`（0905-R2），不是重新使用 V1.2.5。基线 SHA-256：

`9B9A995F04432D36A1947A9A7E7E8C248EAA07BEC4B4DBC9A61BB7A663A7F5B2`

本次在新的隔离目录开发，仅新增问题定位、自定义快捷键、导出进度弹窗和仅 STEP 模式，并做必要的中英文布局调整。原压缩包、源 CAD 和已保存分类项目未被覆盖、移动或删除，没有发布 GitHub 或修改系统配置。没有新增生产代码文件；原有读取、几何验证、STEP 宏、装配体构建、三视图、项目存储与 Excel 生成算法不重写。

Based on the supplied V1.2.6/R2 archive identified above, not V1.2.5. Work took place in a new isolated source directory. Original archives, CAD and saved work projects were not overwritten, moved or deleted. No GitHub publication or system configuration changes occurred. The scope is issue navigation, customizable shortcuts, a modal export progress display and STEP-only delivery with bilingual layout changes. No production source file was added; scanning, geometry checks, macro/assembly execution, previews, storage and Excel-generation algorithms were not rewritten.

## 使用方法 / Usage

### 问题定位 / Issue navigation

导出前检查发现空名称、重名、重复组分类不一致或源文件问题时，收集对应条目。关闭原提示后，列表清除阻挡条目的筛选，滚动并选中第一项，右侧预览同步。琥珀色提示条显示原因和序号。“检查并下一项”先保存当前编辑，再重新检查，按源文件/实体顺序跳转；全部条目通过后仍需用户重新点击导出。失败源尚无实体时选中左侧源文件；部分导出失败也可以按顺序查看。合并模式中隐藏的成员可以成为可见代表，不删除记录、不关闭合并。未分类是否允许导出沿用原规则。

Pre-export checks collect affected bodies under the existing rules. The list clears obstructing filters, scrolls to and selects the first issue, updating its preview. The amber strip shows its reason and index. Check / next issue commits the current edit, rechecks and proceeds in source/body order; passing all item checks does not automatically export. Empty failed sources select the left source entry; failed exports can also be reviewed sequentially. Hidden duplicate members can be focused without deleting records or disabling merging. Classification remains optional where the baseline allowed it.

### 自定义快捷键 / Customizable shortcuts

在“设置”中点击输入框后按键，为“在 SW 中定位”和“切换相同件合并”分别设置字母、数字、功能键或 Ctrl/Alt/Shift 组合。默认不绑定，可清除；相同绑定、关闭/复制/粘贴等常用保留键被拒绝。设置随便携版 Data 保存。快捷键仅在软件当前窗口生效，支持列表和逐项模式；输入文字、操作下拉框、读取和导出时不抢占按键，长按不连续切换。逐项模式切换合并前保存当前草稿。没有注册系统全局快捷键。

In Settings, click a shortcut box and press a letter, number or function key, optionally with Ctrl/Alt/Shift. Assign separate bindings to Locate in SW and Toggle duplicate merging. Defaults are unbound; Clear removes a binding. Conflicting assignments and common reserved combinations are rejected. Settings persist in portable Data. Bindings are app-local and support list/guided windows; typing, combo boxes and busy tasks take precedence. Held keys do not repeatedly toggle. Guided mode commits its draft before toggling. No OS-global shortcuts are registered.

### 导出进度 / Export progress

沿用原 SolidWorks 授权弹窗。确认并启动导出后显示“喵师傅正在施工”/“Master Miao is at work”，提示结束前不要操作 SolidWorks 或 Master Miao。百分比、任务阶段及安全取消按钮置于模态弹窗，阻止主窗口编辑；关闭/Alt+F4不能绕过任务，取消等待原安全边界。完成、失败、取消后回到原结果提示。

百分比按阶段及实体/文件数估算，不是预计剩余时间。STEP 任务的零件准备阶段映射到 0–72%，后续使用原 STEP 进度，避免过早停在99%。同步 CAD 调用时可能暂时不变。弹窗不能锁住操作系统中其他软件；干扰检测沿用原安全检查。

After existing authorization, the modal dialog displays the localized construction message, stage and percentage, and asks users not to operate either application. The owner is disabled; safe cancellation remains available, while Close/Alt+F4 cannot bypass the task. Existing result handling resumes afterward. Percentages represent stages and counts, not remaining time. Native preparation occupies 0–72% for STEP tasks; synchronous calls may leave progress unchanged. Other OS applications are not locked; existing interference checks remain in force.

### 仅 STEP / STEP only

“导出格式”新增“仅导出 STEP”，自动启用 STEP、关闭最终 SLDPRT 和原生装配体输出。零件 STEP 直接进入输出根目录下已分配的分类文件夹；Excel 报表由原复选框独立控制。旧 STEP 链路产生的源装配体 STEP 仍保留在根目录。此选项改变交付格式，不改变实体选择、分类与几何验证。

后台仍通过原宏链路工作：先在本次任务隔离暂存目录生成并验证 SLDPRT，再转换 STEP。常规结束时仅逐文件清理任务专属路径内的中间 SLDPRT；清理受阻则保留并记录原因。进程异常恢复时保留可能被宏使用的中间件，但不将其计入交付数量或报表链接。最终分类目录不交付 SLDPRT。本机 SolidWorks 和零件/装配体模板仍为必需。

选择 SLDPRT 或原生装配体会退出仅 STEP；取消仅 STEP恢复 SLDPRT。原同目录/镜像双目录偏好不会被清除，仅 STEP 时暂不适用。

STEP only enables STEP and disables native part/assembly delivery. Parts go directly into their assigned category folders below the output root; Excel remains independent. The baseline source-assembly STEP is retained at the root. Selection, classification and geometry validation are unchanged. The existing macro pipeline still needs verified native prerequisites, staged privately per task. Normal completion cleans only individual SLDPRT files in that task's staging scope; cleanup failures are reported and retained. Crash recovery keeps potentially live macro inputs but excludes them from delivery counts and report links. SolidWorks and both templates remain required. Selecting native parts/assembly exits this mode; the previous mirrored-folder preference is retained for regular exports.

## 自动测试 / Automated tests

```powershell
.\build.ps1
.\tests\RunR3Regression.ps1 -OutputDirectory C:\YourSandbox\R3-Test-Unique
```

输出目录必须不存在。脚本创建独立运行副本、合成记录和报表，不连接 SolidWorks。可用 `-BinaryDirectory` 指向已解压发行文件验证交付二进制。不要使用旧项目或用户数据目录。

The test directory must be new. The runner creates an isolated runtime, synthetic records and reports without connecting to SolidWorks. BinaryDirectory optionally targets extracted release binaries. Never target existing work projects or user-data directories.

本轮以下自动回归全部通过 / The following regression checks passed:

| 检查 / Check | 证据 / Evidence |
|---|---|
| 启动 / Startup | 实际 EXE 自检和截图 / Actual executable startup and screenshot |
| 双语导航 / Bilingual navigation | 120条合成记录、隐藏筛选、第11/81项滚动、提交后下一项、失败排序、无实体失败源、隐藏重复成员、预览同步 / 120 records, filtered issues, scrolling, commits, failure order, empty failed source, hidden member and preview sync |
| 快捷键 / Shortcuts | 设置持久化与冲突、输入/忙时屏蔽、长按保护、实际逐项窗口切换与草稿保存 / Settings persistence/conflicts, editing/busy/repeat guards, actual guided toggling and saved draft |
| 进度 / Progress | 实际模态消息循环、主窗口禁用、64%可见填充、单次取消、禁止关闭、结束恢复；中英截图检查 / Real modal loop, disabled owner, visible 64% fill, one-shot cancel, close guard, restored owner and bilingual screenshots |
| 仅 STEP / STEP only | 控件互斥、项目保存、分类路径、限定清理、源/最终文件保护、进程异常恢复 / Options/persistence, classified paths, bounded cleanup, source/final protection and crash recovery |
| 编辑回归 / Editing | 真实 Enter/完成/Esc、已保存/未保存各跨4次自动保存、逐项实际按钮回写、折叠恢复 / Actual controls, four autosaves in both save states, guided button persistence and reversible folding |
| 文件回归 / Files | 项目存储25项、旧目录冲突备份、定位身份接口替身、双语Excel结构/缩略图/覆盖保护、互操作程序集构建保护 / 25 storage checks, existing routing/conflicts/backups, locator interface doubles, bilingual workbook/image/overwrite checks and interop copy guards |

以上不等同于生产 SolidWorks 验收。本轮没有操作用户正在使用的 SW/CAD 文档；真实仅 STEP 全流程、中文输入法候选窗和人工干扰实机验收仍待完成。测试占位 SLDPRT/STEP 是文件保护哨兵，不是有效 CAD 或导出成功证据。R2历史94体扫描不能替代本轮输出验证。当前交付为本地测试候选，请先用工程副本验收。

These checks are not production SolidWorks acceptance. This revision did not operate the user's live CAD session. Real end-to-end STEP-only export, native Chinese IME candidate interaction and manual interference remain unverified. Sentinel SLDPRT/STEP files are not valid CAD or export-success evidence. Historical R2 scans do not prove current exports. This is a local test candidate; validate engineering copies before production use.
