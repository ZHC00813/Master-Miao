# Master Miao 架构与开发文件说明 / Architecture and Developer File Guide

## 中文

### 总体结构

Master Miao 是一个无 NuGet 依赖的 .NET Framework x64 WinForms 应用。主 EXE 同时包含界面入口和 `--worker` 工作进程入口。界面把任务写成 JSON，独立工作进程调用 SolidWorks COM API，并通过标准输出报告进度。该边界避免长时间 COM 工作阻塞界面，也让取消、异常回收和响应记录更清晰。

```text
WinForms UI
   │  request.json / cancel.request
   ▼
MasterMiao.exe --worker
   │  SolidWorks COM + compiled STEP macro
   ▼
隔离暂存区 → SLDPRT/SLDASM/STEP 验证 → 正式分类目录
   │
   └─ response.json → UI → Excel 报表与完成/失败弹窗
```

### 源文件职责

| 文件 | 职责 |
|---|---|
| `src/Program.cs` | 程序入口、工作进程分派、启动/逻辑/项目/界面/报表自检入口、程序集元数据。 |
| `src/MainForm.cs` | 主窗口布局、实体表格、分类交互、导出计划、工作进程编排、进度与完成提示。 |
| `src/V120Features.cs` | 逐项模式、项目保存恢复、多选分组、语言切换、关闭确认以及 V1.2 生命周期辅助逻辑。 |
| `src/Models.cs` | 项目、源文件、实体、分类、导出设置、工作请求/响应、用户设置、JSON 和名称规则。 |
| `src/SolidWorksWorker.cs` | SolidWorks 会话、安装检测、扫描、三视图生成、实体拆分、单实体验证、装配体创建与文件路径规划。 |
| `src/AssemblyStepExporter.cs` | 装配体批量 STEP 任务、编译宏启动、干扰检测、日志验证、STEP 文件头检查与正式归位。 |
| `src/ExcelReportWriter.cs` | 不依赖 Office 的 Open XML `.xlsx` 写入、15 列清单、三张嵌入图片及中英文表头。 |
| `src/FolderCanvas.cs` | 可拖动的“电池式”目录关系图、父子关系调整和非法循环阻止。 |
| `src/Localization.cs` | 中文/英文固定文本映射、首次语言选择和窗口品牌图标。 |
| `macro/StepMacro.cs` | 在 SolidWorks 内执行装配体 STEP 保存、临时选项修改、选项恢复和批次日志。 |
| `tools/BuildIcon.ps1` | 从原始品牌 PNG 生成 16–256 像素多尺寸 Windows ICO。 |
| `tests/VerifyStepFolderLayout.cs` | 验证 STEP 同目录/镜像目录、跨根目录自动编号和零件/装配体 STEP 归位。 |
| `build.ps1` | 编译宏和主程序、嵌入图标、复制本机 SolidWorks 互操作程序集到 `build/`。 |

### 关键数据关系

- `AppProject` 是可恢复工作的根对象。
- `SourceRecord` 指向原始 SLDPRT 并保存大小、修改时间、状态和实体列表。
- `BodyRecord` 保存原实体身份、三视图、导出名称、分类 ID、几何指纹和选择状态。
- `CategoryNode` 同时表示标签和目录节点；`ParentId` 构成树。
- `ExportPlanItem` 是从用户项目生成的不可变导出批次输入。
- `ExportResultItem` 和 `AssemblyResultItem` 记录每种文件的实际路径、状态和失败原因。

### STEP 流程

1. 根据分类和冲突策略规划 SLDPRT 与 STEP 路径。
2. 复制实体几何到新零件，在暂存区保存 SLDPRT。
3. 重新只读打开并确认只有一个实体。
4. 按源多实体零件生成临时或正式原位装配体。
5. 通过 `RunMacro2` 启动编译型 STEP 宏。
6. 宏保存装配体 STEP，并由 SolidWorks 同时生成组件 STEP。
7. 主程序验证宏日志、选项恢复标记和每个 STEP 文件头。
8. 按同目录或独立镜像规则提交文件；失败文件不进入正式目录。

### 安全不变量

- 不调用源文档保存；扫描使用只读打开。
- 正式路径限制在用户选择的主输出目录中。
- 路径片段经过非法字符和穿越片段净化。
- 覆盖前先移动到 `Data/Backups`。
- 多 SolidWorks 会话时停止，不猜测连接对象。
- 用户原有会话不退出；程序自有会话按 PID 和启动时间核验。
- 任务响应包含具体失败原因，UI 不把部分失败显示为完全成功。

### 构建与验证

```powershell
.\build.ps1
```

构建要求本机安装 SolidWorks API 互操作程序集，输出只进入忽略的 `build/`。

主要无 SolidWorks 自检入口：

```powershell
.\build\MasterMiao.exe --startup-selftest startup.png
.\build\MasterMiao.exe --logic-selftest <project.swbody.json>
.\build\MasterMiao.exe --project-selftest <input-project> <saved-project>
.\build\MasterMiao.exe --ui-project-screenshot <project> ui.png
.\build\MasterMiao.exe --ui-guided-screenshot <project> guided.png
.\build\MasterMiao.exe --report-selftest report.xlsx
```

`tests/VerifyStepFolderLayout.cs` 可单独编译，并对构建后的 EXE 使用反射运行路径回归。涉及真实 SLDPRT、装配体和 STEP 翻译器的测试必须在受控桌面环境进行，且程序与 SolidWorks 应使用相同 Windows 权限级别。

---

## English

### Overview

Master Miao is an x64 .NET Framework WinForms application with no NuGet dependencies. The main executable contains both the UI entry point and a `--worker` entry point. The UI serializes work to JSON, a separate process calls the SolidWorks COM API, and progress is reported through standard output. This boundary keeps long COM operations away from the UI thread and gives cancellation, cleanup, and response records a clear lifecycle.

```text
WinForms UI
   │  request.json / cancel.request
   ▼
MasterMiao.exe --worker
   │  SolidWorks COM + compiled STEP macro
   ▼
isolated staging → SLDPRT/SLDASM/STEP verification → final category trees
   │
   └─ response.json → UI → Excel report and completion/failure dialog
```

### Source file responsibilities

| File | Responsibility |
|---|---|
| `src/Program.cs` | Application entry, worker dispatch, startup/logic/project/UI/report self-test entry points, and assembly metadata. |
| `src/MainForm.cs` | Main layout, body table, category interaction, export-plan construction, worker orchestration, progress, and completion messages. |
| `src/V120Features.cs` | Guided mode, project persistence, grouped selection, language switching, close confirmation, and V1.2 lifecycle helpers. |
| `src/Models.cs` | Project, source, body, category, export settings, worker request/response, user settings, JSON, and naming rules. |
| `src/SolidWorksWorker.cs` | SolidWorks session ownership, installation detection, scanning, three-view generation, body splitting, single-body verification, assembly creation, and path planning. |
| `src/AssemblyStepExporter.cs` | Assembly STEP batches, compiled-macro launch, interference detection, log verification, STEP header checks, and final placement. |
| `src/ExcelReportWriter.cs` | Office-free Open XML `.xlsx` generation with 15 columns, three embedded images, and localized headers. |
| `src/FolderCanvas.cs` | Draggable battery-style hierarchy view, parent changes, and cycle prevention. |
| `src/Localization.cs` | Chinese/English fixed-text mapping, first-run language dialog, and brand icon handling. |
| `macro/StepMacro.cs` | Runs inside SolidWorks to save assembly STEP, modify settings temporarily, restore settings, and produce batch logs. |
| `tools/BuildIcon.ps1` | Generates a 16–256 pixel multi-size ICO from the canonical brand PNG. |
| `tests/VerifyStepFolderLayout.cs` | Verifies same-folder and mirrored STEP roots, cross-root auto-numbering, and part/assembly STEP placement. |
| `build.ps1` | Compiles the macro and application, embeds the icon, and copies local SolidWorks interop assemblies to `build/`. |

### Key data relationships

- `AppProject` is the root of a recoverable work session.
- `SourceRecord` references an original SLDPRT and stores identity, scan status, and bodies.
- `BodyRecord` stores body identity, three previews, export name, category ID, geometry fingerprint, and selection state.
- `CategoryNode` is both a tag and a folder node; `ParentId` forms the tree.
- `ExportPlanItem` is the batch input derived from editable project state.
- `ExportResultItem` and `AssemblyResultItem` record real paths, per-format status, and failure reasons.

### STEP pipeline

1. Plan SLDPRT and STEP paths from category and conflict policy.
2. Copy body geometry into a new part and save it in staging.
3. Reopen read-only and verify exactly one body.
4. Build a temporary or retained in-place assembly per source part.
5. Start the compiled STEP macro through `RunMacro2`.
6. The macro saves assembly STEP and asks SolidWorks to generate component STEP files.
7. Verify the macro log, settings-restored marker, and every STEP header.
8. Commit using same-folder or separate-mirrored routing; failed artifacts never enter formal output.

### Safety invariants

- Source-document save methods are never called; scanning uses read-only open.
- Formal paths remain under the user-selected main output root.
- Invalid characters and path traversal fragments are sanitized.
- Overwrite moves the old target to `Data/Backups` first.
- Multiple SolidWorks sessions stop the task instead of guessing.
- User-owned sessions remain open; application-owned sessions are matched by PID and start time.
- Responses contain concrete failure reasons, and partial failure is not presented as complete success.

### Build and validation

```powershell
.\build.ps1
```

The build requires SolidWorks API interop assemblies from a local installation. Output is written only to the ignored `build/` folder.

Main self-test entry points that do not require SolidWorks:

```powershell
.\build\MasterMiao.exe --startup-selftest startup.png
.\build\MasterMiao.exe --logic-selftest <project.swbody.json>
.\build\MasterMiao.exe --project-selftest <input-project> <saved-project>
.\build\MasterMiao.exe --ui-project-screenshot <project> ui.png
.\build\MasterMiao.exe --ui-guided-screenshot <project> guided.png
.\build\MasterMiao.exe --report-selftest report.xlsx
```

`tests/VerifyStepFolderLayout.cs` can be compiled separately and run through reflection against the built executable. Tests involving real SLDPRT files, assemblies, and the STEP translator must run in a controlled desktop environment where Master Miao and SolidWorks use the same Windows integrity level.
