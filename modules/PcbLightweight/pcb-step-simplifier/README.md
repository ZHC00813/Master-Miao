# PCB STEP 简化模块

版本：1.0.0 · 整理日期：2026-11-16

将 PCB STEP/STP 装配模型转换为用于结构装配和空间避让的轻量单实体。保留板体、孔槽、主要接口、开关，以及体积最大的 5 个其他元件；将其他小元件替换为局部坐标包络块。

## 包内内容

| 文件 | 用途 |
|---|---|
| `scripts/pcb_simplify.py` | 可独立运行的算法，分析与构建两个阶段 |
| `requirements.txt` | 固定版本的几何依赖 |
| `docs/integration.md` | 模块接入、参数、输入输出、异常处理 |
| `examples/module_adapter.py` | Python 调用适配器，不依赖 Codex |
| `tests/smoke_test.py` | 自动生成测试装配，验证变换、孔位和导出 |
| `SKILL.md`、`agents/openai.yaml` | 用于支持该格式的 AI 工具中的工作流描述 |
| `references/validation.md` | 本次算法回归结果与适用边界 |
| `MANIFEST.json` | 包内文件与 SHA-256 校验清单，不包含自身 |

## 环境与安装

已测试环境为 **Windows x64 + Python 3.12**。压缩包包含源代码和文档，**不包含 Python、Open CASCADE/VTK 二进制库，也不包含用户电路板文件**。首次使用需安装依赖；其他操作系统/运行版本需自行验证可用的对应 wheel。

在本目录执行：

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
$env:PYTHONUTF8 = '1'
```

下文的 `python` 请替换成该环境的 Python 路径。也可将依赖安装到专用目录，然后在算法命令的子命令前添加 `--ocp-path <依赖目录>`。依赖使用各自的许可证，本包没有重新分发其二进制文件。

## 最短使用流程

```powershell
python scripts/pcb_simplify.py analyze "input.step" --work "work/job001" --keep-largest 5
# 检查 work/job001/plan.json：接口、板体、大元件与 review 项。
python scripts/pcb_simplify.py build --work "work/job001" --output "outputs/pcb_light.stp"
```

工作目录应为本任务新目录，输出应为新文件名。算法不覆盖原模型，也不自动覆盖已有计划或输出文件。

分析后的 `plan.json` 含 `components` 数组：

- `action=keep`：保留原形状。
- `action=envelope`：转换为简化包络。
- `action=omit`：仅用于明确可忽略的无实体内容，不能用于删除有体积的元件。
- `review=true`：仍需检查，构建会拒绝执行。确认具体分类并记录理由后再设置为 `false`，不要批量跳过。

## 默认算法

1. 读取 STEP 产品名称、物理元件分组与装配变换，识别板体。
2. 优先保护主要接口及开关/结构件，再对其余元件按净实体体积排名，保留前 5 个。
3. 将剩余小元件替换成其定义局部坐标中的包络块，再应用完整装配变换；避免斜放元件因世界坐标包围盒而被放大。
4. 融合实体。如存在小间隙，默认仅允许半径 0.03 mm、跨隙最多 0.2 mm 的最近位置连接，并记录新增体积及坐标。
5. 做有限几何修复，使用毫米 STEP AP214 导出；默认省略冗余曲面参数曲线以缩小文件。
6. 回读导出文件，验证 1 个有效实体、无独立曲面、导出前后体积和边界一致，写入 `report.json`。

该算法处理的是**结构避让近似**，并非保持全部原始细节的制造模型。小元件的内部空腔、引脚间隙和圆角会消失；接口和保护的大元件仍保留原有几何。脚本不会生成原生 SLDPRT，也不会控制 SolidWorks。

几何校验通过不代表已经在 SolidWorks 中验证。要求实际打开时，应另行导入、检查实体、保存原生零件并重新打开。报告的 `solidworks_verified` 默认始终为 `false`。

## 自检

```powershell
python tests/smoke_test.py
# 若依赖位于指定目录：
python tests/smoke_test.py --ocp-path "path/to/cadlibs"
```

自检在临时目录生成合成模型，不需要用户电路板。测试覆盖嵌套旋转/平移、局部包络尺寸、接口保护、体积排名、安装孔保留、单实体 STEP 回读及未完成检查时阻止输出。

详细接入方式见 [docs/integration.md](docs/integration.md)。
