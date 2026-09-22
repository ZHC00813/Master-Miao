# MasterMiao v1.2.6 · 0922-R5

本版修复读取完成后导出提示源零件未保存，以及另存/重新添加后丢失已完成命名的问题。

## 主要改版

- 新增“保存源文件并重读”“重新关联”，保留可唯一匹配实体的命名、分类和勾选。
- 重读前备份项目与预览；明确提示同名不同路径、只读和文件占用。
- 保存时处理读取/预览产生的待保存状态，并重新核对几何和文件内容身份。
- 修复 STEP 回读同名冲突、对称实体重合误判和导入模板交互；装配 STEP 主文件包含完整几何。

## 文件包

- 文件：`MasterMiao-v1.2.6-0922-R5.zip`
- 主程序文件版本：`1.2.6.5`
- 大小：**3,469,892 字节**，共 **51 个文件**。
- 内容：Windows x64 程序、依赖、STEP 宏、源代码、构建脚本和说明。
- SHA-256：`e92eed252d7a5086e2b021424b13dbf65ae9049ed44ab5eaa9b1ddcaf6b07f00`
- 此附件是本地实测并校验的原始压缩包；CI 核对完整性后直接发布，不重新编译替换。

完整解压后运行 `MasterMiao.exe`。需要 .NET Framework 4.8 和本机 SolidWorks。旧版先保存项目并关闭，再在新版打开原项目；保留 `Previews` 和既有 `Data`。

## 实测范围

SolidWorks 2024 SP0.1，独立七实体模型：7 个命名恢复；7 个 SLDPRT、7 个 STEP、1 个原位装配体和 1 个装配 STEP 均成功并通过相应几何检查；最终批次无需手动选择模板。位移 1 mm、非对称镜像的错误实体被拒绝，六项导入设置恢复通过。

其他模型、SolidWorks 版本及全部历史测试不在此次验证声明内。个人 CAD 文件、命名项目和运行数据不随发布上传。

---

## English

R5 (file version **1.2.6.5**) fixes source-save/rescan and Save As recovery without restarting body naming. It adds explicit save-and-rescan and relink actions, pre-rescan backups, exact-path conflict diagnostics, and STEP import/in-place geometry fixes.

This release distributes the exact locally tested **3,469,892-byte / 51-file** archive, verified by the SHA-256 above. Extract the complete package and run `MasterMiao.exe`; Windows x64, .NET Framework 4.8 and local SolidWorks are required.

Acceptance used an isolated seven-body model on SolidWorks 2024 SP0.1: seven names retained, seven native parts, seven STEP parts, one native assembly and one assembly STEP verified. Incorrect translated/mirrored geometry was rejected and six import preferences restored. CI verifies the archive and source alignment; it does not perform CAD acceptance. Other models, versions and all historical tests are not claimed as validated.
