# Master Miao 开发背景与历程 / Development Background and History

## V1.2.6 稳定性迭代 / Stability iteration — 2026-09-05

用户在 V1.2.5 的基础上提供了系统性修改清单。本次重新审视“输入—保存—读取身份—输出验证—结果清单”的整条链路：旧版持久编辑框虽然修复了即时结束编辑，但自动保存仍会触发提交；同一回归测试已复现旧行为。新版将草稿与已提交模型分离，并使逐项编辑和列表共享模型。

另外，本轮不再把几何摘要当作重复件的最终证明，而是提供明确的人工审查与确认；增加内容哈希、配置和持久实体引用，统一输出名称规划、逐件检查点与失败状态。项目文件使用相对、不可变预览与原子写入；STEP 从检查文件头升级为重新导入几何校验。实际测试、尚未完成的验收和故意保留的安全限制分别记录，不沿用历史成功结论。

The user supplied a systematic checklist against V1.2.5. This iteration reviews the whole editing, persistence, identity, validation and reporting chain. The persistent name editor fixed immediate termination, but autosave could still end an edit; the same regression reproduced the old behavior. Drafts are now separate from committed state, and guided/list editing share the model.

Geometry digests are no longer treated as proof of duplicate parts; explicit human review is required. Content hashes, configurations, persistent references, global output planning, item checkpoints and honest outcomes strengthen export integrity. Projects use relative immutable previews and atomic writes. STEP validation reimports geometry rather than relying on a header. Current evidence and untested boundaries are documented separately from historical successes.

完整本轮变更 / Full iteration details: [V1.2.6_CHANGES.md](V1.2.6_CHANGES.md). 以下内容是旧版本发展历史，不是 V1.2.6 验收证明 / The sections below are historical context, not V1.2.6 acceptance evidence.

## 中文

### 1. 项目背景

Master Miao 来源于一个具体的生产准备问题：一个 SolidWorks 多实体零件可能包含几十个甚至上百个实体，而下游采购、加工、装配和质量检视通常需要的是独立零件文件。SolidWorks 自身可以拆分实体，但当工作同时涉及批量命名、目录分类、重复件识别、STEP 生产文件、装配体复原和报表时，纯手工流程容易出现遗漏、重名、错放目录以及源文件被意外改动等问题。

因此，本项目并不是单纯的“批量另存为”工具。它的目标是把读取、视觉确认、命名、分类、导出和交付清单组合成一个可恢复、可检查、对源文件保持只读的工作流。使用者应当能够先理解模型中有什么，再决定每个实体叫什么、属于哪个生产类别，最后才生成正式文件。

### 2. 需求形成阶段

最初目标是把一个多实体 SLDPRT 的所有实体分别保存为独立零件，并把结果放在桌面上的单独文件夹。随后需求逐步扩展为一个通用 PC 工具：

- 支持拖入或选择多个多实体零件；
- 读取实体数量并显示进度；
- 为每个实体生成等轴测、前视和上视三张视图；
- 在列表或逐项模式中重命名、选择和分类；
- 把标签与文件夹绑定，保证分类名称和输出路径一致；
- 通过可拖动的关系图编辑文件夹父子结构；
- 保存并复用标签、文件夹树和工作项目；
- 选择全部或部分实体导出，并可选相同几何只保留一件；
- 同时生成 SLDPRT、STEP、原位装配体和带缩略图的 Excel 清单。

这一阶段形成了产品的核心思想：实体信息只保留“名称”和“标签”两个主要用户字段，其中标签直接对应目标文件夹。新建文件夹会立即成为可选标签；新建标签时则要求确定它在目录树中的父级位置。

### 3. 第一版桌面原型

第一版采用 .NET Framework WinForms，以便在不安装额外运行时、不引入数据库的前提下直接调用本机 SolidWorks COM API。程序使用红白配色、接近 SolidWorks 的工业软件视觉，同时保持便携式部署：解压后运行，不注册服务，不写系统注册表。

早期实现验证了以下基本链路：检测 SolidWorks、只读打开源文件、枚举实体、复制实体到新零件、保存 SLDPRT、生成预览、创建分类目录以及写出 Excel 报表。所有正式输出都先经过隔离暂存和重新验证。

### 4. STEP 导出问题与方案演进

STEP 是开发中最重要的技术难点。直接从后台 COM 实例逐件另存 STEP 在部分环境中会返回保存失败，而同一台电脑上通过 SolidWorks 可见界面手动导出又是正常的。这说明问题不在 STEP 翻译器是否安装，而在调用环境、会话状态和 SolidWorks 内部导出选项。

经过多轮探针测试后，方案转为：先用拆分后的 SLDPRT 生成原位装配体，再让可见 SolidWorks 会话把装配体另存为 STEP，同时启用“将装配体零部件导出为单独 STEP 文件”。这个方法一次即可得到装配体 STEP 和所有零件 STEP，速度快，也更接近已验证的人工操作路径。

最终实现使用编译型宏 `MasterMiao.StepMacro.dll` 在 SolidWorks 内部执行保存。宏负责临时修改 STEP 选项、逐批保存、记录日志，并在成功、失败或异常时恢复原选项。主程序只在日志确认恢复成功且文件通过 `ISO-10303-21;` 文件头检查后，才把暂存文件提交到正式目录。V1.1.2 在真实用户桌面完成了三零件批次的全自动验收；同一方法也曾验证 94 组件批次。

### 5. 会话安全与全自动化

工具必须在不破坏用户当前 SolidWorks 工作的前提下自动运行，因此增加了明确的会话所有权：

- 如果 SolidWorks 原本由用户打开，程序复用该会话，任务结束后恢复原活动文档和界面状态，不关闭程序；
- 如果用户授权 Master Miao 自动启动 SolidWorks，程序记录进程 ID 和启动时间，只回收完全匹配的自有进程；
- 如果自动启动或自动化连接失败，界面提供手动打开和重试入口；
- 检测到多个会话、活动文档变化、进程关闭或关键保存阶段受干扰时，任务停止并提示原因；
- 除“是否允许自动打开 SolidWorks”等必要授权外，不要求用户手动运行宏。

读取成功后保留源多实体零件打开，方便后续“在 SW 中定位”。定位只执行选择高亮，不保存文档。

### 6. V1.2 工作流扩展

随着实际整理场景增多，界面从单一表格发展为两种互补工作方式。列表模式适合快速对照、多选和批量分类；逐项模式一次显示一个实体、三张大图、名称、标签、输出位置以及已分类/未分类计数。列表加入 80%–200% 缩放，以适应不同屏幕和高 DPI 环境。

同一阶段加入多文件项目、跨文件批量分类、几何重复组折叠、中文/英文切换、工作项目保存、自动保存、异常恢复、最近项目、源文件重新绑定和关闭确认。项目文件保存数据关系和预览，但不复制原始 CAD 文件。

V1.2.0 首次真实双击运行时暴露出窗口句柄尚未创建便调用 `BeginInvoke` 的异常。后续版本把恢复检查移动到窗口显示后执行，并增加真实启动分支自检。这次问题促使测试范围从“逻辑函数可运行”扩展到“真实 WinForms 生命周期可运行”。

### 7. 品牌统一与 V1.2.3

V1.2.3 将软件正式命名为 **Master Miao**。用户提供的猫咪扳手图被保留为原始 PNG，并通过可复现脚本生成多尺寸 ICO，应用于 EXE 文件信息、窗口标题栏和主界面页眉。宏和依赖文件也统一使用 `MasterMiao` 命名。

这一版本重点整理了交付包、验证文档、启动测试、中文/英文截图、项目恢复以及源文件保留行为，没有重写已经通过真实桌面验收的 STEP 宏核心。

### 8. 生产管理分流与 V1.2.4

生产管理通常希望可编辑的 SolidWorks 源零件与下游加工使用的 STEP 文件分开存放，但两者仍需保持相同分类结构。V1.2.4 因此增加两种 STEP 目标模式：

- “与 SLDPRT 同目录”：保持早期版本兼容行为；
- “独立双目录（镜像分类树）”：在主输出下建立 `零件源文件` 和 `STEP生产文件`，两边复用同一分类树。

独立模式中，SLDPRT 与可选 SLDASM 进入源文件树，零件 STEP 与装配体 STEP 进入生产树，Excel 记录两边的实际路径。自动编号会同时检查两套目录，避免任一格式已存在时发生无提示覆盖。

该版本没有改变 SolidWorks 宏和装配体批量导出顺序，只改变通过校验后的正式归位路径。为避免受 SolidWorks 桌面权限影响，新增了独立路径测试：使用有效 STEP 文件头在临时目录中验证同目录兼容、镜像目录、零件与装配体 STEP 归位、跨目录重名检测以及 STEP 不泄漏到源文件树。

V1.2.4 的发布修订还首次修复了列表中“导出名称”每输入一个字符便退出编辑的问题。根因是 `CurrentCellDirtyStateChanged` 对所有单元格统一立即提交，文本单元格也被当成复选框或下拉框处理。修订后只有复选框和分类下拉框即时提交，名称编辑在结束时才写回。这个修复解决了普通键盘连续输入，但后续真实使用发现中文输入法的 Enter 选字仍可能触发 DataGridView 自身结束编辑，因此继续演进为 V1.2.5。

### 9. 输入法安全改名与 V1.2.5

V1.2.5 将导出名称编辑从 DataGridView 的临时内置编辑控件中分离出来，改为覆盖在目标名称单元格上的持久 `TextBox`。编辑框会跟随列表滚动、列宽、行高和窗口尺寸变化重新定位；名称列本身改为只读，只有双击时才开启覆盖编辑器，避免表格内部生命周期提前接管输入。

这一版明确区分“输入法确认”和“项目提交”：Enter 被编辑框消化，只用于输入法选字或继续输入，不会提交名称；点击另一单元格、切换源文件或点击“命名完毕 / Finish naming”才净化名称并写回项目及重复件组；Esc 则取消本次修改。相应自检会逐字符输入中文、反复模拟 Enter、确认数据模型在输入阶段不变，再验证按钮提交和点击其他单元格提交。另增加专用改名界面截图入口，便于回归检查编辑框位置与按钮状态。

V1.2.5 仅调整列表改名交互和对应测试，没有改变多实体读取、源文件只读策略、装配体构建、STEP 宏或双目录归位逻辑。

### 9.1 现场反馈后的编辑链重构

后续真实工作暴露出 V1.2.5 的自检盲区：模拟 Enter 通过并不等于真实中文输入法候选窗稳定；逐项窗口写入数据模型后，后台自动保存还可能从未刷新的主列表反向写回旧值，因此名称和分类会“有时保存、有时消失”。V1.2.6 不再把 V1.2.5 的改名描述当作完成验收。

修订后，列表使用明确声明 Enter 为输入键的独立编辑框，并按实体引用而不是行号提交；逐项模式把名称、分类和导出勾选当作一次提交；自动保存只读取已提交的数据模型，列表永远只是显示投影。回归也从直接调用内部方法升级为点击实际“保存并下一个/上一个”按钮、跨越自动保存周期并核对保存后的 JSON。真实输入法候选窗仍单列为桌面验收项目。

### 10. 已保存发行包的证据化复盘

本次公开整理没有只依赖对话记忆，而是逐一读取用户保留的八个发行压缩包，核对 ZIP 条目、源码变化、README、验证记录、测试结果、文件时间与 SHA-256。它们构成了从 SW Body Organizer 到 Master Miao 的可追溯版本链：

| 版本包 | 保存时间 | SHA-256 | 经文件对比确认的主要变化 |
|---|---:|---|---|
| `SWBodyOrganizer-v1.1.0.zip` | 2026-09-04 00:46 | `AF68EE61C93109EA58B272BDCF37335A1308C72E830D1A9C43EC1237B2A89580` | 建立多实体读取、三视图、SLDPRT 拆分、原位装配体、12 列 Excel、暂存验证与源文件保护基线；当时后台 STEP 仍返回保存错误。 |
| `SWBodyOrganizer-v1.1.2.zip` | 2026-09-04 09:33 | `46D9DBB34E8BDA49B33CCF52E7D0021D0AFF63513F021312FE6276D42531DFFD` | 新增编译型 STEP 宏与装配体批量 STEP 路线；加入 SolidWorks 启动授权、会话所有权、干扰检测、结束统计及失败原因；真实桌面三零件验收通过。 |
| `SWBodyOrganizer-v1.2.1.zip` | 2026-09-04 12:03 | `72AADEB33DB9C940C1D3F155F3DBBA12C2AB2CB494A7D5D9A1FC0DD5E9166594` | 引入三缩略图、列表/逐项双模式、缩放、多文件、多选批量分类、可视化去重、SW 高亮定位、中英文、工作项目与恢复；修复 V1.2.0 的 `BeginInvoke` 启动崩溃。 |
| `SWBodyOrganizer-v1.2.2.zip` | 2026-09-04 12:42 | `19A55CC7C201D7CA506626390D492A1AD98054C891DA7EC91B7AC64FD431C4CA` | 读取后保留源多实体零件和 SolidWorks；增加自动打开、手动打开、取消三种选择、页面“打开 SolidWorks”按钮及自动失败后的手动重试。 |
| `Master-Miao-v1.2.3.zip` | 2026-09-04 18:27 | `EA3FC506EFB62A011E43311FC570834067EF0A5A3A87BB7707AB862BA8DA8E36` | 软件、EXE、宏和 Excel 元数据统一为 Master Miao；加入用户提供的猫咪扳手 PNG、9 尺寸 Windows ICO 及可复现图标构建脚本。 |
| `Master-Miao-v1.2.4.zip` | 2026-09-04 21:30 | `98BE3F83BD400459EDFCEDF017584428BE72DD6100D6FF2144AFB79132B7C235` | 增加 STEP 与 SLDPRT 同目录/独立镜像双目录两种生产归档方式，并加入目录路由回归测试。 |
| `Master-Miao-v1.2.4-name-edit-fix.zip` | 2026-09-04 22:41 | `7024BE2251793AAA0EA544BFE294FF21AB3BC36D0CA5A0E2503661B4F75AFE6D` | V1.2.4 最终修订：修复导出名称逐字符编辑中断，增加“完成改名”按钮、英文翻译和对应 UI 生命周期自检。 |
| `Master-Miao-v1.2.4-name-edit-fix-v2.zip` | 2026-09-04 23:25 | `D508E2100AF9A29ECB7AFEF932D4557C2F94599E2FC73267971352D6A5A4096A` | V1.2.5 的输入基线：以持久覆盖编辑框解决中文输入法选字提交问题，增加“命名完毕”、点击外部提交和输入法 Enter 回归测试。 |

V1.2.0 没有出现在保存包中；它的存在及启动问题由 V1.2.1 的 README、测试记录和源代码修复共同佐证，因此在本历程中被明确标为中间开发版本，而不是可分发版本。公开仓库以 V2 修订源码为 V1.2.5 的功能基线，并独立完成版本号、双语文档、构建和发行包装；旧压缩包仅用于历史核对，不上传用户运行数据、CAD 测试模型或旧二进制。

### 11. 当前原则与后续方向

项目目前坚持四个原则：源文件只读、正式输出前验证、用户会话可恢复、失败原因可解释。代码保持在少量明确模块中，不为单一功能无限增加层级。

可继续演进的方向包括 SolidWorks 配置选择、更强的几何等价判断、用户数据跨版本迁移、最近项目一键继续、更完整的自动化桌面验收，以及签名安装包。任何扩展都不应削弱现有源文件保护和暂存提交机制。

---

## English

### 1. Background

Master Miao began with a practical production-preparation problem. A SolidWorks multi-body part may contain dozens or hundreds of bodies, while purchasing, fabrication, assembly, and inspection teams usually need independent part files. SolidWorks can split bodies, but a manual workflow becomes fragile when it also includes batch naming, folder classification, duplicate detection, production STEP files, assembly reconstruction, and delivery reports. Typical failures include omissions, duplicate names, misplaced files, and accidental source edits.

The project was therefore designed as more than a batch Save As utility. Its goal is to combine discovery, visual verification, naming, classification, export, and reporting into a recoverable workflow that keeps source files read-only. Users should first understand what exists in the model, then decide how each body should be named and classified, and only then generate formal output.

### 2. Requirement discovery

The original goal was to save every body from one multi-body SLDPRT as an independent part in a dedicated desktop folder. It evolved into a general Windows application that would:

- accept multiple multi-body parts through drag-and-drop or file selection;
- report body counts with scan progress;
- generate isometric, front, and top views for every body;
- rename, select, and classify bodies in table or guided workflows;
- bind tags directly to output folders;
- edit folder parent-child relationships through draggable visual blocks;
- save and reuse tags, folder trees, and work projects;
- export all or selected bodies and optionally retain only one representative of identical geometry;
- generate SLDPRT, STEP, in-place assemblies, and thumbnail-rich Excel reports.

This phase established the central data-model decision: the two primary user-facing fields for a body are its name and tag, and the tag is the destination folder. Creating a folder immediately creates an available tag; creating a tag requires selecting its parent in the hierarchy.

### 3. First desktop prototype

The first version used .NET Framework WinForms so it could call the local SolidWorks COM API without an additional runtime or database. Its red-and-white visual language references SolidWorks while remaining a portable application: extract and run, with no service installation and no registry writes.

The early prototype validated SolidWorks detection, read-only source opening, body enumeration, copying a body into a new part, SLDPRT saving, preview generation, category-folder creation, and Excel reporting. Formal output was staged and reopened for verification before being committed.

### 4. STEP export investigation

STEP export became the most important technical challenge. Saving individual STEP files directly from a background COM instance failed in some environments, while manual export from the visible SolidWorks UI on the same computer worked. This showed that the translator was installed and pointed instead to invocation context, session state, and internal export options.

After several probe implementations, the workflow changed: build an in-place assembly from the split SLDPRT files, then use a visible SolidWorks session to save the assembly as STEP with “export assembly components as separate STEP files” enabled. One operation produces both the assembly STEP and every part STEP and follows the path already proven by manual use.

The final design runs a compiled `MasterMiao.StepMacro.dll` inside SolidWorks. The macro changes STEP settings temporarily, saves each batch, writes a log, and restores the original settings on success, failure, and exceptions. The host application commits files only after the log confirms restoration and each file passes the `ISO-10303-21;` header check. V1.1.2 completed an automated three-part batch on the real user desktop; the same method was also exercised with a 94-component assembly.

### 5. Session safety and automation

Automation had to coexist safely with the user's SolidWorks work, so explicit session ownership was introduced:

- A user-owned SolidWorks session is reused and restored; it is never closed by the application.
- A session started after explicit authorization is tracked by process ID and start time, and only that exact owned process may be reclaimed.
- Automatic connection failure falls back to a manual-open-and-retry workflow.
- Multiple sessions, active-document changes, process termination, and interference during critical saves stop the task with a specific reason.
- Apart from required authorization such as permission to launch SolidWorks, the user never has to run a macro manually.

Successfully scanned source parts remain open so “Locate in SW” can highlight bodies later. Highlighting changes selection only and never saves the document.

### 6. V1.2 workflow expansion

The interface evolved from one table into two complementary workflows. Table mode supports rapid comparison, multi-selection, and batch classification. Guided mode focuses on one body with three large views, editable metadata, output location, and classified/unclassified progress. Table scaling from 80% to 200% supports different screens and high-DPI environments.

The same phase introduced multi-file projects, cross-file batch classification, duplicate-group folding, Chinese and English UI, project persistence, delayed auto-save, recovery records, recent projects, source rebinding, and close confirmation. Projects store relationships and previews but never copy original CAD models.

The first real double-click run of V1.2.0 exposed a WinForms lifecycle error caused by calling `BeginInvoke` before the form handle existed. Recovery checks were moved to the Shown phase and a real startup-branch regression test was added. This changed the test philosophy from checking individual logic to checking the actual application lifecycle.

### 7. Unified branding in V1.2.3

V1.2.3 adopted the official name **Master Miao**. The user-provided cat-and-wrench PNG remains the canonical source and a reproducible script builds a multi-size ICO for executable metadata, the title bar, and the red application header. Executable, macro, and dependency names were aligned with the new brand.

This release focused on packaging, validation documentation, startup tests, bilingual screenshots, project recovery, and source-document retention. It deliberately avoided rewriting the STEP macro core that had already passed real desktop validation.

### 8. Production separation in V1.2.4

Manufacturing workflows often need editable SolidWorks sources separated from downstream STEP deliverables while preserving the same classification structure. V1.2.4 added two STEP destination modes:

- Same folder as SLDPRT, preserving compatibility;
- Separate mirrored trees under `零件源文件` and `STEP生产文件`.

In separate mode, SLDPRT and optional SLDASM files enter the source tree; part and assembly STEP files enter the production tree; Excel records all real paths. Auto-numbering checks both trees so an existing file in either format cannot be overwritten silently.

The SolidWorks macro and assembly batch sequence did not change. Only the final destination after validation changed. An isolated route test now uses valid STEP headers in a temporary directory to verify compatibility mode, mirrored trees, part and assembly placement, cross-root conflict detection, and the absence of STEP leakage into the source tree.

The published V1.2.4 revision also made the first fix for export-name editing that ended after every character. The cause was a shared `CurrentCellDirtyStateChanged` handler committing text cells as aggressively as checkboxes and combo boxes. Restricting immediate commits to checkbox and category cells fixed ordinary continuous typing. Real use later showed that IME candidate confirmation through Enter could still make DataGridView end the edit, which led to V1.2.5.

### 9. IME-safe naming in V1.2.5

V1.2.5 separates export-name editing from DataGridView's temporary built-in editor. A persistent `TextBox` is overlaid on the target name cell and repositioned when the table scrolls, columns or rows resize, or the window changes size. The name column itself is read-only and double-click explicitly opens the overlay, preventing the grid's edit lifecycle from taking control prematurely.

The revision distinguishes IME confirmation from project commit. Enter is consumed by the editor so candidate selection and continued typing do not submit the name. Clicking another cell, changing the selected source, or choosing **Finish naming** sanitizes and commits the value to the project and duplicate group; Esc cancels it. Regression coverage now types Chinese one character at a time, repeatedly simulates Enter, verifies that the model remains unchanged during composition, and then checks both button and click-away commit paths. A dedicated name-edit screenshot entry point supports visual regression of editor placement and button state.

V1.2.5 changes only list naming interaction and its tests. Multi-body scanning, read-only source handling, assembly construction, the STEP macro, and mirrored routing are unchanged.

### 9.1 Editing-chain redesign after field feedback

Production use exposed gaps in the V1.2.5 checks: simulated Enter did not establish native Chinese IME stability, and autosave could still replay stale main-grid values after the guided window had changed the model. Names and categories could therefore appear to save and later disappear. V1.2.6 no longer treats the V1.2.5 naming description as completed acceptance.

The revision uses a dedicated editor that explicitly owns Enter and commits by body reference rather than row number. Guided name, category and export selection are one commit, autosave reads only committed model state, and the grid is a projection. Regression now clicks the actual Save-and-next/Previous buttons, crosses an autosave period and verifies saved JSON. Native IME candidate-window behavior remains a separate desktop acceptance item.

### 10. Archive-backed release reconstruction

The public history was reconstructed from the eight release archives retained by the user rather than from conversation memory alone. ZIP inventories, source changes, README files, validation notes, test results, timestamps, and SHA-256 hashes were compared:

| Archive | Saved | SHA-256 | File-backed milestone |
|---|---:|---|---|
| `SWBodyOrganizer-v1.1.0.zip` | 2026-09-04 00:46 | `AF68EE61C93109EA58B272BDCF37335A1308C72E830D1A9C43EC1237B2A89580` | Established body scanning, three views, SLDPRT splitting, in-place assembly, a 12-column Excel report, staged verification, and source protection; background STEP saving still failed. |
| `SWBodyOrganizer-v1.1.2.zip` | 2026-09-04 09:33 | `46D9DBB34E8BDA49B33CCF52E7D0021D0AFF63513F021312FE6276D42531DFFD` | Added the compiled macro and assembly batch STEP route, launch authorization, session ownership, interference checks, completion statistics, and detailed failures; passed a real desktop three-part run. |
| `SWBodyOrganizer-v1.2.1.zip` | 2026-09-04 12:03 | `72AADEB33DB9C940C1D3F155F3DBBA12C2AB2CB494A7D5D9A1FC0DD5E9166594` | Added three thumbnails, table/guided modes, scaling, multi-file classification, visible deduplication, SW highlighting, bilingual UI, project persistence, and recovery; fixed the V1.2.0 `BeginInvoke` startup crash. |
| `SWBodyOrganizer-v1.2.2.zip` | 2026-09-04 12:42 | `19A55CC7C201D7CA506626390D492A1AD98054C891DA7EC91B7AC64FD431C4CA` | Kept scanned source documents and SolidWorks open; added automatic/manual/cancel launch choices, an in-page launch button, and manual retry after an automatic failure. |
| `Master-Miao-v1.2.3.zip` | 2026-09-04 18:27 | `EA3FC506EFB62A011E43311FC570834067EF0A5A3A87BB7707AB862BA8DA8E36` | Unified the Master Miao product, executable, macro, and report identity; added the user-designed cat-and-wrench artwork, nine-size ICO, and reproducible icon builder. |
| `Master-Miao-v1.2.4.zip` | 2026-09-04 21:30 | `98BE3F83BD400459EDFCEDF017584428BE72DD6100D6FF2144AFB79132B7C235` | Added same-folder and separate mirrored-tree production routing plus a dedicated folder-layout regression test. |
| `Master-Miao-v1.2.4-name-edit-fix.zip` | 2026-09-04 22:41 | `7024BE2251793AAA0EA544BFE294FF21AB3BC36D0CA5A0E2503661B4F75AFE6D` | Final V1.2.4 revision: fixed interrupted multi-character export-name editing and added the localized Finish rename action and lifecycle self-test. |
| `Master-Miao-v1.2.4-name-edit-fix-v2.zip` | 2026-09-04 23:25 | `D508E2100AF9A29ECB7AFEF932D4557C2F94599E2FC73267971352D6A5A4096A` | V1.2.5 input baseline: introduced the persistent overlay editor for Chinese IME safety, Finish naming, click-away commit, and simulated IME Enter regression tests. |

V1.2.0 is not present among the retained archives. Its intermediate existence and failure mode are supported by the V1.2.1 README, test evidence, and source changes, so it is documented as a development build rather than a distributable release. The public repository uses the V2 revision source as the V1.2.5 functional baseline and separately applies the new version identity, bilingual documentation, build, and release packaging. Older archives remain evidence only; runtime user data, CAD test models, and old binaries are not published.

### 11. Current principles and future work

The project follows four principles: sources stay read-only, formal output is verified first, user sessions are recoverable, and failures are explainable. The code remains in a small number of modules instead of accumulating a new layer for every feature.

Possible future work includes SolidWorks configuration selection, stronger geometric equivalence, cross-version user-data migration, one-click continuation of recent projects, broader visible-desktop automation tests, and signed distribution packages. None of these should weaken source protection or staged commit guarantees.

### 12. 0905-R2：恢复定位与去重交互 / Restoring location and duplicate interaction

用户在实际使用中报告，主文件已在SW打开时定位却提示文件占用，去重勾选也不隐藏重复行。检查发现定位复用了导出磁盘摘要与未保存状态检查，文件共享模式又与SW的可写句柄冲突；去重则被R1新增的人工确认门槛改变了操作顺序。R2把只读的实时高亮与严格导出校验分开，恢复复选框即时折叠/还原，并将每个折叠成员的身份随导出计划传递，在导出前进行真实几何重合核对。

真实SW 2024读取独立副本94体通过；真实WinForms列表94→71→94→71通过。测试进程随后无法连接活动对象，因此实机高亮、真实合并输出未列为通过。详细证据、失败原因及后续验收保留于 [R2_FIX_VALIDATION.md](R2_FIX_VALIDATION.md)，没有继续发布GitHub。

Field feedback showed file-in-use errors when locating an already open source and no visible folding after enabling deduplication. Location had inherited disk-hash and dirty-state export checks with incompatible file sharing; R1's manual-confirmation gate had also changed the original checkbox workflow. R2 separates live highlighting from strict export verification, restores reversible immediate folding, and includes every folded identity in the export plan for kernel congruence checks.

A real SW 2024 scan of an isolated copy returned 94 solids; actual WinForms rows toggled 94/71/94/71. Active-object attachment then failed in the test environment, so live highlighting and actual deduplicated output remain unaccepted. Evidence and limits are recorded in the linked R2 document. GitHub publication remains stopped.

### 13. 0905-R3：减少整理中断与纯 STEP 交付 / Issue navigation and STEP-only delivery

用户在 V1.2.6 上提出：校验提示应直接找到对应条目，支持逐项修正；定位和合并操作需要可自定义快捷键；导出中应显示百分比及“喵师傅正在施工”的操作提醒；生产交付还需要仅 STEP 的选择。开发以用户保存的 R2 压缩包为基线，在独立目录修改，没有回退到旧版本，也没有迁移或清理用户工作记录。

本次让问题队列关联稳定实体 ID，复用原校验规则和编辑提交动作；在原设置窗口增加应用内按键绑定，输入法和忙时优先；通过模态进度窗口阻止主页面修改，不强锁系统或结束 SW 会话。仅 STEP 使用原装配体宏与验证路线，但将中间 SLDPRT 放到任务暂存区，并从交付计数和报表路径中排除；异常恢复保留可能被宏占用的中间件。代码继续使用已有模块，中英文同步。

120条合成记录的真实 WinForms 导航、逐项快捷键与草稿、模态进度、仅 STEP 路由/清理/恢复，以及旧编辑、25项项目存储、目录/报表/定位接口等自动回归通过。没有真实 CAD 仅 STEP 导出验收，不能将文件路径哨兵测试当成几何成功证据。交付标识为 `V1.2.6 · 0905-R3`，保留历史包和记录，不发布 GitHub。详情见 [R3_CHANGES_AND_TESTS.md](R3_CHANGES_AND_TESTS.md)。

The user requested direct navigation from validation warnings, sequential corrections, configurable locate/merge shortcuts, a modal percentage/construction notice, and STEP-only production delivery. Development started from the supplied R2 archive in an isolated directory, without reverting versions or migrating/deleting work records. Issue queues use stable body IDs and existing validation/edit-commit logic. App-local bindings defer to text/IME editing and busy work. Modal progress blocks organizer edits without locking the OS or terminating SW. STEP-only retains the verified assembly-macro route while staging native prerequisites privately and excluding them from delivery counts and report paths; crash recovery preserves potentially live macro inputs. Existing modules and both languages are retained.

Actual WinForms tests with 120 synthetic records, guided shortcuts/draft persistence, modal progress, STEP-only routing/cleanup/recovery and previous editing/storage/report/identity contracts passed. Real CAD STEP-only output remains unaccepted; sentinel files are not geometry evidence. The local package is marked V1.2.6 / 0905-R3, old archives and records remain, and GitHub publication stays stopped. See the linked R3 record for reproducible tests and limitations.

### 14. 0905-R4：冻结导出后台，专注界面 / Frozen backend, focused UI refinement

用户反馈原有导出流程已经顺畅，要求不要继续修改或反复测试可用的导出逻辑，把优化集中在 UI 和操作体验。R4 因此冻结 R3 后台，以文件摘要和关键方法正文对比确认读取、拆分、STEP、装配体、几何、存储和提交语义未变。界面提高字号和对比度，将拥挤横排按钮分组，保留直接全选；增加侧栏收放，重排路径/格式区，突出逐项保存，给分类关系图独立的大窗口。分类使用原控件与事件，退出展开窗口时原位归还，不建立第二套分类数据。

中英文1540/1100窗口、94条现有记录与缩略图、菜单动作、分类展开返回、名称/自动保存回归通过，读取的原项目逐字节未变。本轮没有连接 SolidWorks 或进行 CAD 导出实验。R4 采用独立目录和明确版本号，提醒用户先保存旧项目再打开，以免将新目录的空白启动界面误认为工作记录丢失。详情见 [R4_UI_NOTES.md](R4_UI_NOTES.md)。

The user confirmed the export workflow was already smooth and asked to focus on UI/interaction instead of modifying or repeatedly testing working CAD logic. R4 freezes R3's backend, auditing file hashes and protected method text for scanning, splitting, STEP, assemblies, geometry, storage and commit semantics. Typography/contrast, grouped tools, one-click selection, sidebar toggling, export-area layout and guided-mode emphasis were refined. Classification expands the existing controls into a larger workspace and returns them afterward; no duplicate classification model is introduced.

Bilingual 1540/1100 layouts, 94 saved records/previews, menus, workspace restoration and editing/autosave regression passed. The original project remained byte-identical. No SolidWorks connection or CAD export experiment was performed. The independent R4 folder/version and save-then-open guidance avoid mistaking a fresh portable UI for lost work. See the R4 notes for evidence and limitations.
