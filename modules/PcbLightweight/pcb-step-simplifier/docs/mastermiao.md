# 喵师傅 PCB模型轻量化扩展
版本：1.1.0-preview（2026-11-16）

增加局部圆柱包络、圆柱包含性检查、元件类别与接口外体积排名。plan.json 新增category、rank、suggested_shape、simplify_shape。
新增build --multi-solid：保留多实体，不融合、不增加连接。默认命令行仍兼容原有单实体流程。
喵师傅界面默认保留接口和前10个其他元件，保留多实体、完整参数曲线，导出为具有独立名称的单零件。原版说明中的前5个与省略曲线是原始算法默认值，不是喵师傅界面默认值。
用户可以逐项选择keep或envelope及auto/box/cylinder；有体积的元件不允许omit。board始终keep。
参考原始README与integration.md了解基础接口。SolidWorks实测以交付测试记录为准。
