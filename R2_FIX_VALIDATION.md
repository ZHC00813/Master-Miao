# 0905-R2 修复与验证 / Fixes and validation

日期 / Date: 2026-09-05. FileVersion: `1.2.6.2`. 本地测试候选，不发布 GitHub / Local test candidate; no GitHub publication.

## 问题与修复 / Problems and fixes

1. **在 SW 中定位报文件占用。** 原实现高亮前使用 `FileShare.Read` 重新读取磁盘摘要，与 SW 已持有的可写文件句柄冲突；又错误地沿用了导出的待保存检查。现在只在已打开的源文档中匹配配置和持久实体引用；旧记录回退为唯一原名称加几何证据。先匹配全部请求，再激活、选择和缩放；不保存或关闭源文件。独立的导出检查仍严格拒绝未保存状态和不可信身份。
2. **去重勾选不折叠。** R1 加入的“先人工确认重复组”门槛改变了原交互。R2 恢复指纹分组即时折叠、取消即恢复，不从项目删除实体或改写其既有名称/分类。已排除成员保持独立；组内编辑同步。已有部分勾选时，显示首个被选成员，导出数量统计按组内是否有人被选计算。
3. **导出防误合并。** 计划保留全部被合并成员的身份。工作进程逐个核对源摘要、配置和实体，再要求 SW 内核的几何重合及正向刚体变换；允许同件旋转/平移，不接受镜像或缩放。无法核实时该组失败，不输出一个零件冒充全组。未比较材料、加工与表面处理。跨源文件核验只关闭本次临时打开的文档。
4. **共享只读检查。** 需要摘要的扫描/导出使用只读访问加兼容的文件共享，并检查摘要读取前后长度/写入时间；没有获得写源文件的权限。真正独占的文件仍失败，扫描后内容变化仍拒绝。
5. **关联 UI 修正。** 切换去重前先提交当前编辑；装配体选项自动关闭去重时也立即还原列表。未引入新的生产模块，复用现有定位、完整性与工作进程代码。

1. **File-in-use during location:** disk hashing used incompatible sharing and export-only pending-save checks. Location now resolves identities in the open document, using persistent references or a unique original name plus evidence for legacy records. It resolves all bodies before selecting and zooming, without saving or closing the source. Export remains strict.
2. **Checkbox did not fold duplicates:** R1's manual-confirmation gate changed the original workflow. R2 restores immediate reversible fingerprint folding without deleting records or overwriting existing edits. Exclusions stay independent; editing a group synchronizes members. A selected representative matches export scope.
3. **Safe duplicate export:** plans carry every folded member identity. The worker verifies each source and solid, then requires kernel congruence with a proper rigid transform. Translation/rotation are allowed for duplicate comparison, not reflection/scale. Unverified groups fail rather than silently losing parts. Materials and manufacturing attributes are outside this comparison.
4. **Shared read-only hashing:** scan/export hashes tolerate an existing writable CAD handle, with before/after metadata checks. This grants no source-writing access; exclusive locks and changed contents remain rejected.
5. **Related UI:** finish the current edit before changing groups; assembly's automatic dedup disable also restores rows. Existing production modules are reused.

## 本轮证据 / Evidence from this pass

| 测试 / Test | 结果 / Result |
|---|---|
| 编译、实际窗体构造/Shown / Build and actual startup | 通过；开发时发现并修正重复翻译键，修正后重跑通过 / Passed after catching and fixing a duplicate localization key |
| 已保存/未保存项目输入 / Saved and unsaved editing | 各11.2秒跨4个自动保存周期；Enter、明确提交与Esc通过 / 11.2 seconds each across four autosaves; Enter/commit/Esc passed |
| 逐项真实按钮 / Guided actual buttons | 三个实体保存并下一个、上一个、返回列表、保存再打开通过 / Three-body navigation and disk round-trip passed |
| 最新真实扫描数据列表 / Grid with freshly scanned data | **94 → 71 → 94 → 71**，直接切换真实复选框；项目实体未删除 / Actual checkbox folding/restoration; no records deleted |
| 分组排除与计划 / Exclusions and plans | 排除成员、人工组、部分勾选代表项、合并成员身份快照通过 / Exclusions, explicit groups, selected representative and member snapshot passed |
| 文件共享 / File sharing | 可写CAD式句柄下摘要可读；独占锁仍拒绝；内容变化仍拒绝 / Writable CAD-style handle tolerated, exclusive lock and content changes rejected |
| 定位及重合接口契约 / Location and congruence contracts | COM接口替身通过：无磁盘路径也能按持久引用解析；旧名+指纹回退、歧义拒绝；导出脏状态拒绝；内核不重合/镜像/缩放拒绝。**不是实机几何证明** / Interface doubles passed; not real CAD geometry proof |
| 真实SW读取 / Real SW scan | SW 2024 `32.0.1` 启动成功，独立副本保存后读到 **94实体**，本次关闭预览生成 / Owned instance scanned 94 solids from an isolated saved copy; previews disabled |
| 真实高亮与去重导出 / Live location and deduplicated export | **未完成**：调用活动对象连接返回 `0x800401E3 (MK_E_UNAVAILABLE)`；测试在此停止，未生成去重零件 / **Incomplete:** active-object attachment failed; the test stopped before exporting |
| 真内核反例、STEP与完整装配 / Kernel counterexamples, STEP and assemblies | 本轮未完成；不得借用旧版本的成功记录 / Not completed in this pass; historical success is not current acceptance |

测试辅助程序的 COM 清理还返回 `0x80010105`，正常关闭测试窗口的请求未成功，因此没有强制终止进程。没有修改注册表、系统权限或宏安全选项来规避限制；未关闭任何用户原有文档。扫描只处理工作区新建副本；用户源文件仅作读取。UI截图/真实CAD/运行数据留在本地验证目录，不放入软件包。

The test helper's COM cleanup also returned `0x80010105`, and a normal close request did not succeed. No process was forcibly terminated. Registry, system permissions and macro security were not changed to bypass the limitation. No pre-existing user document was closed. The scan used a newly created workspace copy; the original was only read. Private CAD and runtime data are not packaged.

## 仍需的桌面验收 / Remaining desktop acceptance

在与 SW 相同权限的正常桌面运行本包，打开源文件副本；点击定位检查多选高亮，再测试有待保存标志时的定位。核对去重的71行、取消后94行。最后以专用输出目录测试真正合并导出及不等价反例。真实输入法候选窗、完整STEP/装配体流程仍是单独验收项。

Run the package on a normal desktop at the same privilege level as SW. Verify single/multiple highlights in a source copy, including pending-save state; toggle 71/94 rows; then validate actual deduplicated export and negative geometry fixtures in a dedicated output directory. Native IME and complete STEP/assembly flows remain separate acceptance items.

复现源码 / Reproduction sources: `tests/VerifyEditingPersistence.cs`, `tests/VerifyLocationIdentity.cs`, `tests/VerifyStepFolderLayout.cs`, `tests/VerifyLocatorAndDedup.cs`, `tests/VerifySolidGeometry.cs`. The desktop harness requires no existing SW session and a NEW fixture directory; do not run it alongside production work.

API依据 / API references: [Microsoft FileShare](https://learn.microsoft.com/zh-cn/dotnet/api/system.io.fileshare), [SolidWorks GetCoincidenceTransform2](https://help.solidworks.com/2022/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IBody2~GetCoincidenceTransform2.html).
