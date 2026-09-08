# V1.2.6 验证记录 / Validation record

最新界面修订为 **0905-R4 / 1.2.6.4**。后台不变；界面测试、代码范围核对和使用说明以 [R4_UI_NOTES.md](R4_UI_NOTES.md) 为准。R4 is UI-only; the linked notes supersede the current-interface status below. R3/R2 records remain historical evidence.

当前修订为 **0905-R3 / 1.2.6.3**，新增功能与本轮测试以 [R3_CHANGES_AND_TESTS.md](R3_CHANGES_AND_TESTS.md) 为准。本页下方保留历史；R2去重和真实扫描历史见 [R2_FIX_VALIDATION.md](R2_FIX_VALIDATION.md)。Current evidence is in the R3 record; the following sections and R2 scan results are historical, not proof of current production export acceptance.

## 状态 / Status

**测试候选版，未完成生产导出验收。 / Test candidate; production export acceptance is incomplete.**

本轮日期：2026-09-05。以用户保存的 V1.2.5 为基线；本页只记录本轮新代码的证据，不继承 V1.1.2/V1.2.5 的 STEP 成功结论。测试源代码保留在 `tests/`，私有模型、运行日志和界面快照保留在本地忽略的 `validation-v126/`，不随包公开。

Date: 2026-09-05. The supplied V1.2.5 is the baseline. This page records current evidence only, not inherited STEP success. Test source is distributed; private CAD, logs and UI captures remain local.

## 已通过 / Passed

| 检查 / Check | 本轮证据 / Current evidence |
|---|---|
| 编辑 / Editing | 同一真实 WinForms 反射回归复现 V1.2.5 自动保存打断；新版独立编辑框声明 Enter 为输入键，已保存/未保存项目各持续输入11.2秒，跨4次自动保存，Enter不提交、明确完成提交、Esc取消。Same harness reproduces the old bug and passes both current save states with an editor that owns Enter. |
| 逐项与列表 / Guided/list | 实际点击逐项窗体“保存并下一个/上一个”，故意保留陈旧列表并等待3.3秒跨越自动保存；名称、分类、返回列表模型及最终项目JSON一致。Actual button clicks survive a stale list and autosave, including saved JSON. |
| V1.2.5读取兼容 / Scan compatibility | 工作进程输入恢复为仅含源文件身份；扫描后按索引和几何指纹线性恢复名称、分类和勾选；失败/未访问源不覆盖原模型。静态检查确认扫描不再执行导出阶段内存拒绝或历史实体嵌套COM匹配。Worker input and edit restoration match the linear V1.2.5 contract; export checks remain strict. |
| 存储 / Storage | 25项实际文件系统/独立进程回归：schema2迁移、项目移动、相对预览、备份、锁定、过期写入、不可变预览、多实例恢复、源SHA重新绑定及持久任务预览。25 filesystem/process checks, not mock save success. |
| 路径/输出 / Paths/commit | 同目录/镜像树、编号后全局唯一、装配体名称预留、输入路径碰撞阻止、原子备份与失败保旧、跳过非成功、非法变换、截断STEP、无SW不得仅凭头部确认几何。Filesystem/plan tests; no CAD success inferred. |
| 构建DLL / Build DLL handling | 同哈希锁定DLL跳过，不同哈希锁定时失败，不同哈希可写时替换，3项通过。Three actual copy/lock cases. |
| 新读取项目续作 / Fresh-scan project roundtrip | 新版94实体扫描结果保存再打开，通过项目自检；全部预览仍存在，3个选择及改名分类保持。Fresh 94-body project saves and reopens with previews and selections intact. |
| 界面布局 / UI layout | 中英1540/1100、紧凑与逐项模式共8张离屏截图；实际控件绑定检查，关键图已目视核对。Eight offscreen layouts, real control bindings and visual inspection. |
| Excel结构 / Workbook structure | 中英文25列表，18张不同比例图片的OOXML嵌入尺寸、全部实例、逐格式失败、缺失预览、数值数量、文字不转公式、冻结及筛选；锁定覆盖失败保留旧文件。Bilingual structural tests and locked overwrite preservation. |
| Excel独立检查 / Independent workbook check | 另用表格导入器检查实际程序产物，未发现公式错误；识别全部图片锚点。直接从保存的OOXML媒体与尺寸生成图片检查图，宽高比正确。Independent importer plus saved-image/anchor inspection. |
| SolidWorks真实读取 / Real SW scan | 较早的 V1.2.6 候选在 SW2024 Revision32.0.1 对独立副本读取94实体、生成282张PNG、预览失败0并保留源文档；本次兼容性读取改动尚未占用用户当前会话复测，不能把旧结果等同于当前二进制验收。An earlier candidate scanned 94 bodies; the revised scan path still needs a desktop retest. |

输入回归使用真实控件与消息泵，但模拟输入/Enter，不是真实中文输入法候选窗。Native IME composition remains a separate acceptance item.

2026-09-05 编辑修订追加回归：从主窗体实际打开模态逐项整理窗口，连续点击三个实体的“保存并下一个”，跨越自动保存周期，核对末项、返回列表后的名称/分类/勾选和保存后重新读取的项目；全部通过。Enter 通过控件原生消息预处理及键盘消息验证，未调用全局键盘输入。该结果仍不等同于真实输入法候选窗验收。

The editing revision additionally passed the production modal entry point, three real Save-and-next button clicks across autosave, last-item behavior, main-list synchronization, Back to list and disk reopen. Enter was exercised through native control message preprocessing and key messages without global keyboard input. This remains separate from actual IME composition.

独立表格渲染器导入后没有绘制嵌入图片，尽管图片及锚点检查正常；因此不把该渲染器截图当作本机 Excel 图片显示验收。The independent renderer did not paint imported images; it is not native Excel visual acceptance.

## 真实 CAD 测试边界 / CAD test boundaries

原样本只读打开后立即被 SW 标记需保存；较早候选因此拒绝读取。当前兼容修订恢复 V1.2.5 行为：读取阶段允许查看和分类并显示警告，正式导出仍拒绝不确定内存状态。此前测试仅在工作区复制原文件，再由 SW 保存副本以稳定重建状态；副本完整扫描成功。原始文件没有保存或改写，最终 SHA-256 与测试前一致：
`6BCFA7DBF75148AD16267DCC11891F09C4F2703A0EEE0D36A1A1981F69E43279`。

The original sample became dirty immediately after read-only opening and an earlier candidate rejected it. The compatibility revision now permits scan/organization with a warning while export remains fail-closed. A separate workspace copy was previously saved in SW and scanned successfully; the original SHA-256 remained unchanged. That earlier scan does not replace a current-binary desktop retest.

扫描结束后 SW 窗口仍保留，但后续独立进程无法连接：`Marshal.GetActiveObject` 返回 `0x800401E3 / MK_E_UNAVAILABLE`，只读枚举可见 ROT 条目数为0。执行环境为沙箱账户；查询会话所有者也被系统拒绝。没有更改权限、注册表或宏安全设置，没有尝试绕过边界，没有强制结束交还用户的会话。

After handoff the window remained open, but subsequent automation attachment failed with 0x800401E3 and no visible running objects. The sandbox account could not inspect the session owner either. No system/security changes or forced shutdown were attempted.

因此本轮**没有完成**：重新连接后持久引用恢复、真实SLDPRT/STEP/装配体导出、STEP几何重新导入验证、真实取消/干扰恢复、自动打开与复用两条完整桌面路径。`VerifySolidGeometry` 已编译，但连接失败发生在几何测试之前，不能记为几何反例通过。

Consequently, reopened persistent references, real part/STEP/assembly export, imported STEP geometry, CAD cancellation/interference and both complete desktop session paths remain unverified. The Modeler counterexample helper was compiled, but could not run after attachment failed.

另一个待验风险：严格检查内核返回的原位重合矩阵可能保守拒绝对称体；未用未验证的替代算法放松校验。Strict kernel transform acceptance may conservatively reject symmetric solids; this requires real export testing.

## 性能 / Performance

同一已保存94实体项目，使用 `tests/MeasureUi.ps1` 启动并绘制界面，不调用SW；单次观测：

| 版本 / Version | 耗时 / Seconds | 峰值工作集 / Peak working set |
|---|---:|---:|
| V1.2.5 | 3.03 | 214.04 MB |
| V1.2.6 最终构建 / Final build | 1.499 | 90.46 MB |

这是同机单次 UI 观测，不是基准统计，也不代表 CAD 扫描/导出加速；背景SW工作、文件缓存等会影响数值。新源文件从9个增至12个，增加三个职责明确的辅助文件。稳定性覆盖使总行数增加，未声称整体代码量减少。

Single-run UI observations, not a statistical benchmark or CAD throughput claim. Three focused helpers increase source-file count from nine to twelve; additional safety coverage increases total code volume.

## 最小桌面验收 / Minimum desktop acceptance

1. 用普通用户双击测试包，与SW保持相同权限。使用工程副本，先确认保存；测试自动启动确认、手动启动回退、已有SW复用，以及读取后保留与定位。
2. 已保存/未保存项目各用真实中文输入法连续选字超过10秒；测试明确完成、Esc、逐项改名分类与保存再打开。
3. 选择三个实体、改名、分配两个不同子目录，启用SLDPRT+STEP+镜像树+装配体+报表。预期3个零件、3个零件STEP、1个装配体、1个装配体STEP、1份报表，独立重开检查位置和几何。
4. 在独立测试件上运行真实重复反例：相同、平移、旋转、镜像、质量/面积近似相同而孔位不同。确认候选不自动合并；原位几何检验不把错误位置当正确。
5. 更改副本配置/实体顺序或在SW中制造未保存修改，确认阻止旧数据导出；项目搬移后验证图片、源重新绑定和续做。
6. 用专用输出目录测试锁定、覆盖、失败重试、取消、会话干扰，检查旧文件、完整结果清单与恢复行为；在本机Excel打开清单核对三张图与各状态。
7. 记录每项通过/失败和源副本前后SHA，再决定是否用于生产。磁盘满、进程强制中断、大型装配及跨SW版本兼容仍需进一步覆盖。

Run the same seven cases under a normal same-privilege desktop account: session consent and reuse; native IME editing; three-part full mirrored export (nine output files); difficult geometry counterexamples; stale-source/configuration protection; controlled output faults/cancellation and native Excel review; then record results and source hashes before production use.

## 重现脚本 / Reproducibility

- `build.ps1`: 当前主程序与编译宏 / app and macro.
- `tests/VerifyEditingPersistence.cs`: 对指定EXE运行真实控件回归，可加baseline参数复现旧缺陷 / real-control regression against a specified EXE.
- `tests/VerifyProjectStorage.cs`: 与Models和ProjectStore一起编译 / compile with model/storage sources.
- `tests/VerifyStepFolderLayout.cs`: 引用当前EXE的纯路径/文件验证 / current-assembly filesystem/plan test.
- `tests/VerifyBuildInterop.ps1`: 真实构建复制循环 / actual build-copy loop.
- `tests/VerifyExcelReport.cs`: 当前报表写入器与OOXML检查 / actual writer and structural checks.
- `tests/VerifySolidGeometry.cs`: 需要可连接的指定SW进程；仅建立临时体，不修改文档 / requires an accessible expected SW PID and uses transient solids.
- `tests/PrepareCadFixture.cs`、`PrepareIntegration.cs`、`InspectSession.cs`: 测试准备与只读连接诊断；前者只能使用全新独立副本 / fixture/request preparation and read-only attachment diagnostics.
