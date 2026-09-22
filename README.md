# MasterMiao V1.2.6 · 0922-R5

SolidWorks 多实体零件整理工具：读取、三视图预览、命名、分类，导出独立零件、STEP 和原位装配体。

[English](README.en.md) · [下载 R5 完整版](https://github.com/ZHC00813/Master-Miao/releases/tag/v1.2.6-0922-R5) · [修复与验证](R5_FIX_NOTES.md) · [发布说明](RELEASE_NOTES_R5.md)

## R5 改进

文件版本 **1.2.6.5**。本次解决“已读取但导出提示未保存，以及另存后重新添加丢失命名”的恢复流程，并修复同名文件冲突、STEP 回读与原位几何验证问题。

- **保存源文件并重读**：经确认后保存并重新读取，处理读取过程产生的待保存状态，保留可唯一匹配实体的名称、分类和勾选。
- **重新关联**：源文件另存或移动后，在原记录上选择新路径，无需删除后重新命名。
- 重读前自动备份项目与预览；几何变化或身份不明确的实体需人工检查。
- 明确提示同名不同路径、只读和文件占用；导出继续检查文件内容、配置及几何。
- STEP 回读避开同名参照冲突，修正对称体的重合误判，生成包含完整几何的装配 STEP。

## 下载与使用

从 [Releases](https://github.com/ZHC00813/Master-Miao/releases/tag/v1.2.6-0922-R5) 下载 `MasterMiao-v1.2.6-0922-R5.zip`，完整解压后运行 `MasterMiao.exe`。包大小 **3,469,892 字节**，包含 51 个文件；程序、依赖、STEP 宏、源码和说明齐全。

要求 Windows x64、.NET Framework 4.8，以及可正常运行的本机 SolidWorks。程序与 SolidWorks 使用相同权限级别。

旧版先保存项目并退出，新版使用“打开项目”继续。保留项目配套的 `Previews` 文件夹；在原目录升级时保留 `Data`。

SHA-256：

```text
e92eed252d7a5086e2b021424b13dbf65ae9049ed44ab5eaa9b1ddcaf6b07f00
```

包及校验文件也保存在 [releases/v1.2.6-0922-R5](releases/v1.2.6-0922-R5)。不包含个人项目、CAD 模型或运行数据。

## 验证范围

在 SolidWorks 2024 SP0.1 中使用七实体模型的独立副本完成：保存重读后保留 7 个命名；7 个 SLDPRT、7 个 STEP、1 个原位装配体、1 个装配 STEP 均导出并通过相应几何校验。最终批次无需手动选择模板。位移和非对称镜像实体被拒绝，六项导入设置恢复通过。

本次没有宣称所有历史测试、任意模型或其他 SolidWorks 版本都已验证。旧 R2/R3/R4 文档保留为历史记录，当前结果以 [R5 修复说明](R5_FIX_NOTES.md) 为准。GitHub Actions 只验证并发布本地实测的原始文件包，不运行 SolidWorks。

## 构建

仓库根目录执行：

```powershell
.\build.ps1
# 可指定 SolidWorks API 程序集位置：
.\build.ps1 -SolidWorksApiPath 'D:\SOLIDWORKS\api\redist'
# 生成新的完整包（不会覆盖已有包）：
.\tools\Package.ps1 -OutputDirectory 'D:\MasterMiao-release'
```

源码位于 `src`，STEP 宏位于 `macro`，测试源码位于 `tests`。查看 [第三方组件说明](THIRD_PARTY_NOTICE.md) 和 [历史架构说明](ARCHITECTURE.md)。
