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

V1.2.4 的发布修订还首次修复了列表中“导出名称”每输入一个字符便退出编辑的问题。根因是 `CurrentCellDirtyStateChanged` 对所有单元格统一立即提交，文本单元格也被当成复选框或下拉框处理。修订后只有复选框和分类下拉框即时提交，名称编辑在结束时才写回。这个修复解决了普通键盘连续输入，但后续真实使用发现中文输入法的 Enter 选字仍可能触发 DataGridView 自身结束编辑，因此继续演进为 V1.2.5。

### 9. 输入法安全改名与 V1.2.5

V1.2.5 将导出名称编辑从 DataGridView 的临时内置编辑控件中分离出来，改为覆盖在目标名称单元格上的持久 `TextBox`。编辑框会跟随列表滚动、列宽、行高和窗口尺寸变化重新定位；名称列本身改为只读，只有双击时才开启覆盖编辑器，避免表格内部生命周期提前接管输入。

这一版明确区分“输入法确认”和“项目提交”：Enter 被编辑框消化，只用于输入法选字或继续输入，不会提交名称；点击另一单元格、切换源文件或点击“命名完毕 / Finish naming”才净化名称并写回项目及重复件组；Esc 则取消本次修改。相应自检会逐字符输入中文、反复模拟 Enter、确认数据模型在输入阶段不变，再验证按钮提交和点击其他单元格提交。另增加专用改名界面截图入口，便于回归检查编辑框位置与按钮状态。

V1.2.5 仅调整列表改名交互和对应测试，没有改变多实体读取、源文件只读策略、装配体构建、STEP 宏或双目录归位逻辑。

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
