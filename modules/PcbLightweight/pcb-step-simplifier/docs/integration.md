# 接入其他文件模块

## 推荐调用方式

算法作为独立子进程运行，避免 CAD 库的进程级参数与宿主模块冲突。每个处理任务使用独立工作目录；初期建议同时只运行一个 CAD 任务，再根据内存用量调整并发。

将整个 `pcb-step-simplifier` 文件夹放入你的模块资源目录，保持相对目录结构。宿主程序可以调用 `examples/module_adapter.py` 中的 `analyze()`、`build()`，或者自行执行命令行。适配器仅使用 Python 标准库，CAD 依赖安装在它调用的 Python 环境中。

```python
from pathlib import Path
from examples.module_adapter import analyze, build

job = Path("work/pcb-0001").resolve()
plan = analyze("input.step", job, keep_largest=5)

# 在界面展示 components 的 name/action/reason/review。
# 宿主程序或操作人员完成分类检查后，保存修改后的 plan.json。
# 不要仅为了继续构建而无条件清空 review 标记。

result = build(job, "outputs/pcb_light.stp")
print(result["output"], result["output_bytes"], result["solid_count"])
```

如果算法依赖与宿主程序不在同一个 Python 环境，传入 `python_executable`；如果依赖通过 `pip --target` 安装，则传入 `ocp_path`。默认单阶段超时 600 秒，适配器可调整；超时会结束该算法子进程，保留任务目录用于诊断。

## 命令行参数

`--ocp-path` 是全局选项，放在 `analyze` 或 `build` **之前**。

| 阶段 | 参数 | 默认/含义 |
|---|---|---|
| analyze | 输入路径 | `.step` 或 `.stp` 文件 |
| analyze | `--work` | 必填；新任务目录 |
| analyze | `--keep-largest` | `5`；接口等优先保护项之外，再保留多少个大元件 |
| analyze | `--keep-pattern` | 附加保护名称的正则表达式 |
| analyze | `--board-pattern` | 板体名称正则；自动识别不明确时指定 |
| build | `--work` | 必填；包含审核后的计划及几何缓存 |
| build | `--output` | 必填；尚不存在的输出文件 |
| build | `--max-gap` | `0.2` mm；可设 0 禁止跨越正间隙，范围 0–0.2 |
| build | `--bridge-radius` | `0.03` mm；范围大于 0 且不超过 0.1 |
| build | `--pcurves` | 开启完整参数曲线导出，用于兼容性重试 |

即使 `--max-gap=0`，相切或仅边缘接触的实体仍可能加入小连接球。需要完全不增加几何的工作流，应另行调整连接策略，而不是把本参数解释为“禁止所有连接”。

## 输入、缓存和输出

`plan.json` 包含版本、源文件绝对路径、源文件 SHA-256、单位、板体 ID 和每个组件的名称/形状数据/动作/保护理由。`geometry/` 保存原形状和包络 BREP；`result.brep` 是构建后的几何。构建时会核对源文件 SHA-256，因此分析和构建之间不要移动或修改源文件。

成功输出 STEP 后，`report.json` 包含：

- `output`、`input_bytes`、`output_bytes`；
- `original_faces`、`final_faces`、`solid_count`、`valid`；
- `preserved`、`simplified`、`omitted_non_solids`；
- `volume_mm3`、`bbox_mm`；
- `bridges`、`bridge_added_volume_mm3`；
- `include_pcurves`、`solidworks_verified`。

控制台含 CAD 内核日志，不应当作完整 JSON 解析；以计划和报告文件为结构化接口。退出码 0 表示对应阶段完成，非 0 表示停止或失败。报告可能包含本地路径和元件名称，宿主模块按自身数据管理规则保存。

## 失败处理

| 情况 | 建议处理 |
|---|---|
| 没有可分组的组件/只有整体实体 | 回到分元件的源装配，或另行适配分组，不能把整板变成包络块 |
| 板体缺失或多个候选 | 检查名称或装配层级，指定准确 `--board-pattern`，用新任务目录重试 |
| `review=true` | 展示待检查项，确认类别后保存计划 |
| 输入变化/缓存读取失败 | 新建任务目录重新分析 |
| 存在超过 0.2 mm 的断开实体 | 检查安装关系，不用扩大公差或连接长度强行融合 |
| 回读校验失败 | 不交付 `.candidate.stp`；检查日志，必要时使用新输出名加 `--pcurves` 重试 |
| 内核校验通过但 SW 无法打开 | 在 SW 中检查导入设置/诊断，记录实际结果，不能仅凭 `valid=true` 宣称兼容 |

脚本名称识别是启发式规则，不是完整电子元件数据库。无名或无法分类元件默认保留且要求检查。文件体积没有固定保证：保护的大元件或接口自身可能很复杂。

`SKILL.md` 是可选的 AI 工作流入口，不是运行依赖。放入支持该格式的技能目录时无需安装 CAD 程序本体；真正执行算法仍需满足 `requirements.txt`。不要把技能中本用户的授权语句当作其他用户的授权，集成到多用户模块时应使用各自会话的确认和权限规则。
