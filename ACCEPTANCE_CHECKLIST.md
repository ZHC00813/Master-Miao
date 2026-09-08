# V1.2.6 需求验收表 / Requirement acceptance checklist

最新为 **0905-R4** 界面优化，后台未改。中英文布局、菜单、侧栏、分类工作区和编辑回归见 [R4_UI_NOTES.md](R4_UI_NOTES.md)。The latest R4 revision changes the UI only; see its current evidence and limitations before the historical records below.

当前修订为 **0905-R3**，四项新增功能与本轮回归见 [R3_CHANGES_AND_TESTS.md](R3_CHANGES_AND_TESTS.md)。下方保留历史；真实 STEP 完整导出未验收。Current R3 scope/tests are in the linked record; the content below is historical, and real end-to-end STEP export is not accepted.

0905-R2：真实 SW 2024 扫描94体通过；真实列表控件去重94→71、取消恢复94通过；锁文件和定位身份接口回归通过。真实高亮的活动对象连接失败，完整定位和合并导出未验收。下表保留前次历史，以 [R2_FIX_VALIDATION.md](R2_FIX_VALIDATION.md) 为最新证据。R2 passed a real 94-solid scan and reversible 94/71 UI folding; desktop attachment prevented full location/export acceptance. See the linked current record; the table retains earlier history.

状态区分实现、自动测试与实机验收；“部分”不是完全通过。详见 [VALIDATION.md](VALIDATION.md)。Implementation, automated tests and desktop acceptance are distinct; partial does not mean fully accepted.

| 清单项 / Requirement | 已落实 / Delivered | 验收状态与剩余项 / Evidence and remaining work |
|---|---|---|
| 1 P0 编辑 / Editing | 输入法安全独立编辑框、稳定实体引用、提交/Esc、逐项原子提交、模型自动保存 / IME-safe editor, stable reference, atomic guided commit | 真实按钮/控件自动回归通过，旧版覆盖缺陷复现；真实IME候选窗待测 / button/control regression passed; native IME pending |
| 2 P0 身份与读取 / Identity and scan | V1.2.5线性读取、SHA、配置、持久引用、重新绑定 / linear scan, hash, configuration and references | 较早候选94体扫描通过；当前兼容修订和跨重开/配置/顺序变更实机待测 / earlier scan passed; revised path and reopen cases pending |
| 3 P1 重复 / Duplicates | 疑似候选、人工确认、成员排除/解组、数量和来源 / candidates and explicit reviewed groups | UI不自动误合并通过；真实几何反例未运行；生产属性不在范围 / UI passed; CAD counterexamples pending |
| 4 P1 设置 / Settings | 实时保存、逐项路径同步、请求快照、互斥、忙时禁用 / live settings and frozen requests | 控件模型回归通过；完整运行中的人工交互待测 / model regression passed; busy desktop interaction pending |
| 5 P1 项目 / Projects | 相对不可变预览、旧schema迁移、原子备份、多实例恢复 / movable projects and safe persistence | 25项文件/进程测试通过；真实磁盘满未注入 / 25 passed; actual disk-full injection pending |
| 6 P1 冲突 / Output conflicts | 最终统一编号、未验证跳过、原子替换、会话保护 / global plans and verified-only inputs | 文件/规划测试通过；真实重复导出与桌面会话回收待测 / filesystem passed; CAD reuse pending |
| 7 P1 几何 / Geometry validation | 重开SLDPRT、装配体变换、STEP重导入与分级验证 / reopen and geometry validation | 已实现但完整CAD导出未通过验收；对称体保守误拒风险待测 / implemented, unaccepted; symmetry risk |
| 8 P1 取消恢复 / Cancel/recovery | 逐件检查点、完整清单、失败重试、取消安全边界 / checkpoints and failed-only retry | 数据/状态逻辑检查完成；同步CAD取消、异常退出和重试实机待测 / logic covered; CAD fault paths pending |
| 9 P2 报表 / Report | 三图等比、25列、数量、来源、逐格式状态、中英 / aspect-preserved bilingual reports | 实际OOXML及独立导入检查通过；本机Excel视觉待测 / structural/import passed; native Excel pending |
| 10 P2 界面 / UI | 紧凑/三图、筛选搜索、撤销、命名预览、目录保护、较大加深字体 / compact/list workflows and clearer type | 真实控件回归与中英1540/1100离屏截图检查通过；完整人工交互待测 / control/layout checks passed; complete interactive acceptance pending |
| 11 P2 简化 / Maintainability | 存储/完整性/UI三个helper、缓存、后台保存、构建诊断 / three focused helpers and bounded caching | 94体同机单次UI耗时/内存下降；不承诺所有规模加速或总行数下降 / observed UI improvement, not a universal claim |
| 12 P2 验证交付 / Verification/delivery | V1.2.6程序、源码、双语文档与本表 / candidate package, source and bilingual docs | 真实读取通过；沙箱阻止复用会话，完整9文件输出待同权限桌面验收 / scan passed, full desktop export pending |

本次不宣称“清单全部验收通过”。不包括DXF、材料/工艺自动识别、BOM或在线协作。This is not a claim that every acceptance case passed; peripheral features remain out of scope.
