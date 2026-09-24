# BuildTools 项目状态

- 目标：修复 Part JSON 输入冲突，并把 Part Editor、Build Settings、Part JSON 集中到同一位置切换。
- 当前：BT-017 已在 Mac 游戏加载，构建件与安装件 SHA-256 均为 `35cf8bbce44070afe47e124bd1ce9f9d83698fec68c78260814cf45c6efdad44`。Part 多选页固定共用 Position/Orientation，其下按实际选中零件类型动态显示保存变量，数值行和方形开关沿用原版风格，适用数写在字段名后。单选与多选都将 `fuel_percent`、`force_percent` 作为百分比；外观原始字段在“更多选项”，非有限保存值只读。只读样本审计覆盖 29 种零件、93 个按类型区分的 `N/B/T` 字段组合，见 `notes/2026-09-24-part-field-coverage.md`。用户详细测评 BT-016 后表示无明显问题，BT-017 的 RA LES/Fairing/Separator 百分比、混合类型、开关和更多选项样例也回复“目前看没问题”。**未逐个实测所有官方及第三方零件，非有限状态只读仍待人工确认。** GitHub 公开源码快照 `https://github.com/Hanosn2007/sfs-mod-workspace` 正在同步当前源码；Pro 报告 `notes/2026-09-24-Pro-multi-input-review.md` 是较早方案的评审资料。Windows 手动包更新为 BT-017，含 Workspace 0.4.0 的三 Mod 包也更新为 BT-017；Windows 实机未验证。当前 Mac 的 Snap to Parts、Part Adaptation、Grid Snap 仍因 Harmony 补丁跳过而无效，见 `notes/2026-09-23-bug-audit.md`。
- 约束：不改 Workspace 服务/VPS 或其他共享蓝图工作，不清理蓝图、存档和运行数据。Windows 交接只手动安装，保留前置 UITools 与 Workspace，不引入自动安装器。
- 后续：核验两个 BT-017 手动包的文件、SHA-256 和源码一致性，并同步公开仓库；Windows 端依目标游戏安装状态检查前置 Mod、加载日志和交互。Mac 日常使用中继续留意未知零件、非有限值、混合值/部分适用、输入草稿、撤销和窄窗。BT-006 既有的倒置开关、重启后的 1°配置加载、JSON 原生键及非 90°翻转仍待查。
