# Master Miao V1.2.6 架构与开发说明 / Architecture and Developer Guide

## 中文

Master Miao 是离线运行的 x64 .NET Framework WinForms 程序，无 NuGet、数据库或 Office 自动化依赖。主 EXE 同时提供界面和 `--worker` 入口。V1.2.6 保留原有界面结构，用三个小型辅助文件明确职责，而不引入新的框架。

### 数据读写边界

| 层次 | 责任 |
|---|---|
| 编辑模型 | `AppProject` 是已提交数据的唯一来源；列表和逐项窗口修改同一个模型。名称编辑框中未确认文字属于临时编辑状态。 |
| 项目存储 | `ProjectStore` 保存模型快照、管理资源，不读取表格单元格，也不结束输入法编辑。 |
| 导出任务与结果 | `WorkerRequest` 固定名称、分类、身份、选项和路径；结果记录实际文件、格式状态、来源实例和错误。任务不从变化中的 UI 重新读取设置。 |

```text
列表 / 逐项窗口 → 提交编辑 → AppProject
                              ├─ 快照 → ProjectStore → JSON + Previews
                              └─ 任务快照 → request.json → 独立工作进程
                                                     → 暂存、校验、正式提交
                                                     → checkpoint / response → 界面与 Excel
```

### 文件职责 / File responsibilities

| 文件 / File | 中文 | English |
|---|---|---|
| `Models.cs` | Schema、实体身份、目录、设置、任务数据、原子 JSON 与数据目录。 | Schemas, identities, categories, settings, task data, atomic JSON and data locations. |
| `ProjectStore.cs` | 新增辅助：迁移、相对预览、并发保存、恢复隔离与源文件重新绑定。 | New helper: migration, portable previews, save concurrency, recovery isolation and relinking. |
| `UiWorkflow.cs` | 新增辅助：筛选、紧凑列表、批量命名预览、撤销、疑似重复审查和重试。 | New helper: filters, compact list, name previews, undo, duplicate review and retry. |
| `ExportIntegrity.cs` | 新增辅助：源内容/配置/持久引用、几何、STEP 重开与安全提交。 | New helper: source/configuration/reference identity, geometry, STEP reopening and safe commits. |
| `MainForm.cs` | 主界面、模型绑定、任务快照、工作进程编排、完成提示。 | Main UI, model binding, task snapshots, worker orchestration and completion feedback. |
| `V120Features.cs` | 名称与逐项交互、后台自动保存、项目入口、语言、关闭与定位。 | Name/guided editing, background autosave, project flows, language, close and locate actions. |
| `SolidWorksWorker.cs` | 会话、扫描、三视图、身份继承、全局路径规划、SLDPRT 与装配体。 | Sessions, scanning, previews, identity inheritance, final path planning, parts and assemblies. |
| `AssemblyStepExporter.cs`、`macro/StepMacro.cs` | 可见会话批量 STEP、宏选项恢复、重开验证及归类。 | Visible-session STEP batches, option restoration, reimport validation and routing. |
| `ExcelReportWriter.cs` | Open XML、等比例三图、双语清单与实际任务结果。 | Open XML, three proportional previews, bilingual inventory and actual task results. |
| `FolderCanvas.cs`、`Localization.cs` | 目录关系、同级重名/循环检查、品牌及语言。 | Hierarchy, sibling/cycle validation, branding and language. |
| `Program.cs`、`build.ps1` | 启动诊断、工作/自检入口、可配置 API 依赖及构建。 | Startup diagnostics, worker/test entry points, configurable API dependencies and build. |

除宏与构建脚本外，源码位于 `src/`。

### 编辑、保存与迁移

每 2.5 秒检查变更版本号。自动保存复制已提交模型，后台处理 JSON 与预览资源；完成后仅在项目和编辑版本仍对应时清除待保存状态。它不改变焦点、不提交中文输入法未确认文字。未指定位置的项目仍保留“需要保存项目”的关闭语义。手动保存、切换源文件/项目和“命名完毕”使用明确提交规则；Esc 放弃当前草稿。逐项与列表不再分别维护可互相覆盖的记录。

失败时保留编辑和待保存标志，显示经过节流的错误及最近成功保存时间。Schema 3 增加 `ProjectId`、源文件 SHA-256、配置和实体引用。Schema 2 载入有默认值，但不会凭空补齐源身份；缺少可靠身份的旧源必须重读后导出。未知更新 Schema 被拒绝，避免降级覆盖。

项目 JSON 使用相对预览路径 `Previews/<哈希前缀>/<SHA256>.png`；载入时统一解析为绝对路径。图片按内容寻址且不覆盖旧版本，因此新保存失败或过期写入不会改变旧项目/备份引用的资源。路径、大小与修改时间缓存避免重复处理未变资源。移动 Schema 2 项目后优先在项目自身 `Previews` 中解析旧绝对路径。源 SLDPRT 仍为外部引用：有哈希时重新绑定必须内容相同，无哈希时标记必须重新读取。

JSON 写入同目录唯一临时文件并刷新到磁盘，再用 `File.Replace` 原子替换并保留 `.bak`。主 JSON 损坏可回退备份，修复时不会把损坏内容覆盖进有效备份。`.lock` 排他锁防止同时写入；加载时保存的摘要阻止过期实例覆盖别人新存的项目。

恢复按 `Data/Recovery/<实例 ID>/<项目 ID>.swbody.json` 隔离；活跃实例持有独占租约，其他实例不会提供它的恢复项。选定的恢复项目成功另存后，只清理相应记录。项目保存最近任务请求和结果，相关缩略图也采用相对资源路径，以便移动项目后保留重试信息。程序目录不可写时改用 `%LOCALAPPDATA%/MasterMiao/<安装目录哈希>/Data` 并显示实际路径；启动异常写临时诊断日志。

### 身份与验证

扫描记录源 SHA-256、活动配置、`GetPersistReference3` 引用、原名称、体积/面积、精确边界及几何证据。工作开始及提交边界复核内容与内存状态，拒绝未保存修改或配置不一致。数组序号不单独识别实体；引用失效仅允许有证据的唯一匹配，多解或不足则要求重读。

体积、面积、面和边指纹仅筛选“疑似重复”，不证明孔距相同，也不证明材料/表面处理等生产属性相同。只有明确确认的组才折叠和联动，保留成员、排除、取消分组及所有来源。人工确认是用户生产判断，不是程序的精确等价证明。原位装配体与去重继续互斥，因为代表件不能恢复省略实例的原位置。

SLDPRT 重新打开验证单实体，并结合扫描证据、体积、面积、边界与 SolidWorks 几何重合检查。装配体检查数量、引用、固定状态及原位变换。STEP 文件头/结束标记只代表格式检查，必须实际导入验证实体数量、尺度、几何及位置；不能把“文件头通过”当“几何验证通过”。实机适用范围以 `VALIDATION.md` 为准，这里描述实现流程，不声明全部场景已通过实机验收。

数值使用 SI：长度米、面积平方米、体积立方米。`ExportIntegrity` 定义长度绝对容差 `1e-6 m`、相对容差 `1e-6`、面积绝对容差 `1e-10 m²`、体积绝对容差 `1e-12 m³`、变换系数容差 `1e-8`。标量使用绝对/相对容差较大值，禁止 NaN/Infinity；证据键对坐标按 `1e-9 m` 量化，它是身份复核证据，不是普遍形状相等判定。

### 任务结果与安全边界

整批先规划最终名称，包括自动编号、两套目录和装配体。STEP/装配体要求最终名称全局唯一。规范化输出路径不得与任何源路径相同。覆盖先在目标目录完成复制和摘要核验，再原子替换；旧目标进入同目录 `.MasterMiao-backups`，不先移走唯一有效文件。

结果区分本次成功、沿用且已验证、跳过未验证、失败、取消和未执行。跳过旧文件不是本次新成果，未验证的旧 SLDPRT 不作为新 STEP/装配体输入。记录计划与实际名称、数量、完整来源、格式状态和错误，供完成提示、Excel 和重试统一使用。

每件及关键边界写检查点。取消等待 SolidWorks 同步调用返回后的安全边界，不能承诺立即中断。STEP 失败保留已验证 SLDPRT。重试重新核查源身份并遵守冲突策略。用户会话保留；已经交还用户的读取会话不再作为程序临时进程关闭。程序不保存源 CAD、不安装服务、不改注册表，运行不需联网。

### 构建与测试

```powershell
.\build.ps1
.\build.ps1 -SolidWorksApiPath 'D:\SOLIDWORKS\api\redist'
# 也可设置 MASTER_MIAO_SW_API；必须同时具备 sldworks 与 swconst DLL。
```

构建只写 `build/`，依赖缺失报告完整路径。独立存储回归无需 SolidWorks：

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /platform:x64 /out:build\VerifyProjectStorage.exe /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll src\Models.cs src\ProjectStore.cs tests\VerifyProjectStorage.cs
.\build\VerifyProjectStorage.exe .\build\storage-test-new
```

测试在新隔离目录及子进程中验证迁移、移动、占用、损坏、写失败、不可变图片与恢复隔离，不是 SolidWorks 大模型测试。现有启动、逻辑、项目、截图和报表自检入口保留。真实 CAD 验收必须用本次新编译构建，程序与 SolidWorks 保持相同 Windows 权限级别；旧版结果不能当作本版通过。

---

## English

### Ownership and modules

Master Miao is an offline x64 .NET Framework WinForms application without NuGet, database or Office automation dependencies. The executable contains UI and worker entry points. Three focused helpers are added without a framework: `ProjectStore` for persistence, `UiWorkflow` for editing tools, and `ExportIntegrity` for identity, geometry and file commits. The table above describes all files in both languages.

`AppProject` is the sole committed editing state, shared by list and guided views. Unconfirmed editor text remains a draft. `ProjectStore` accepts snapshots and never reads controls or commits IME text. `WorkerRequest` fixes source identity, names, categories, settings and output location; result records describe actual files and feed reports, feedback and retry.

### Saving and recovery

A 2.5-second timer checks mutation revisions and moves snapshot disk/resource work to the background. Completion clears the dirty flag only for the matching project/revision. Explicit save/navigation/Finish naming commit edits; Esc discards the active name draft. Failures retain data and display throttled diagnostics plus the last successful save time.

Schema 3 adds project identity, hashes, configurations and persistent body references. Schema 2 uses safe defaults but missing identity requires rescan. Newer schemas are rejected. JSON stores relative preview paths resolved on load. Immutable `Previews/<prefix>/<SHA256>.png` assets ensure failed or stale saves cannot change images referenced by earlier JSON/backup versions. Metadata caches avoid repeated unchanged-resource work. Moved legacy projects find former absolute assets inside their own `Previews`. CAD sources remain external references; hashed source relinking requires identical bytes.

JSON is flushed to a unique same-directory temporary file and atomically replaced with a `.bak` backup. A damaged primary can fall back to the backup; repairing it does not replace good backup data with corrupt content. Exclusive locks reject simultaneous writers, while saved-content fingerprints reject stale writes. Recovery records use separate instance/project IDs and live-instance leases. Saving a chosen recovery retires only that record. The latest request/results and their relative thumbnail assets are saved with the project so retry information survives folder moves. Unwritable portable directories fall back to `%LOCALAPPDATA%/MasterMiao/<installation-path-hash>/Data` with a visible diagnostic.

### Identity and geometric evidence

Scanning records SHA-256, active configuration, `GetPersistReference3`, original name and geometry evidence. Source content, configuration and unsaved state are rechecked at processing and commit boundaries. An array index cannot identify a body alone; invalid references need a uniquely supported fallback or rescan.

Mass/surface/edge fingerprints identify possible duplicates only. Users must explicitly confirm groups before folding or linked editing; members, exclusions, ungrouping and all occurrences remain available. Confirmation is a user production decision, not a program proof of exact geometry or materials/finish equivalence. Deduplication remains mutually exclusive with in-place assembly generation.

SLDPRT validation reopens a single solid and checks source evidence and geometric congruence. Assembly validation checks references, component count, fixed state and placement. STEP header/end markers are basic format checks; actual import must verify solid count, scale, geometry and position. A header pass is never a geometry pass. `VALIDATION.md` records actual current-build coverage; architectural descriptions are not real-desktop test claims.

All geometry uses SI. Absolute length tolerance is `1e-6 m`, relative tolerance `1e-6`, area absolute tolerance `1e-10 m²`, volume absolute tolerance `1e-12 m³`, and transform coefficient tolerance `1e-8`. Scalars use the larger absolute/relative bound and reject NaN/Infinity. Evidence coordinates are quantized to `1e-9 m`; this supports identity, not general shape equivalence.

### Output and operational boundaries

Final names are planned for the whole task, including numbering, both folder trees and assemblies. STEP/assembly batches use globally unique final stems. Normalized output paths cannot equal any source. Complete copies are hashed before atomic replacement; the previous target remains in destination `.MasterMiao-backups`.

Results distinguish generated, verified reused, skipped-unverified, failed, cancelled and unexecuted. Unverified existing parts cannot feed new assemblies/STEP. Planned and actual names, quantities, all occurrences, per-format states and errors come from the same task result. Checkpoints preserve item progress. Cancellation waits for safe boundaries after synchronous SolidWorks calls. STEP failure preserves verified SLDPRT files; retry rechecks identities and respects conflict policies.

User sessions remain open, including successful scan sessions already handed back to the user. Source CAD is not saved; no services or registry changes are installed, and runtime does not require networking.

### Build and validation

Configure `build.ps1 -SolidWorksApiPath '<api\\redist>'` or `MASTER_MIAO_SW_API`; both SolidWorks `sldworks` and `swconst` assemblies are checked before compilation. Output remains in `build/`. The independent storage regression command above uses only Models/ProjectStore and real filesystem/process tests; it is not a CAD or large-model benchmark. Startup, logic, project, screenshot and report entry points remain available. End-to-end CAD acceptance must use the current compiled build with matching Windows integrity levels for Master Miao and SolidWorks. Historical releases do not establish current-release acceptance.
