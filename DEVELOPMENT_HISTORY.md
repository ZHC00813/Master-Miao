# Master Miao 开发背景与历程 / Development Background and History

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

### 9. 当前原则与后续方向

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

### 9. Current principles and future work

The project follows four principles: sources stay read-only, formal output is verified first, user sessions are recoverable, and failures are explainable. The code remains in a small number of modules instead of accumulating a new layer for every feature.

Possible future work includes SolidWorks configuration selection, stronger geometric equivalence, cross-version user-data migration, one-click continuation of recent projects, broader visible-desktop automation tests, and signed distribution packages. None of these should weaken source protection or staged commit guarantees.
