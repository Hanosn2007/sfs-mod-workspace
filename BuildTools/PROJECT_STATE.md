# BuildTools 项目状态

- 目标：修复 Part JSON 输入冲突，并把 Part Editor、Build Settings、Part JSON 集中到同一位置切换。
- 当前：BT-015 多选输入体验候选已安装并加载，待人工验收；上一版多选已实测 2 个 Fuel Tank + 1 个 Engine Titan 的 `Applies n/N`、Fuel 40 部分写入与一次撤销。本候选将逐行 Apply 改为按数据类型即时生效，并把原始字段收进可展开区域，详见 `PROJECT_LOG.md` 的 BT-015。BT-014 的窗口缩放、三页布局、单零件 Fuel/Engine 控件等此前已由用户在游戏内验收；BT-006 原生 Q/E 1°旋转与 Build 页 Save 即时写盘、BT-005 坐标同步/微调不跳回也已确认。当前 Mac 的 Snap to Parts、Part Adaptation、Grid Snap 仍因 Harmony 补丁跳过而无效，见 `notes/2026-09-23-bug-audit.md`。Windows 手动包仍为 BT-014，含 Workspace 0.4.0 的三 Mod 包亦为 BT-014，Windows 实机未验证。
- 约束：游戏运行中先保存进度再安装或重启；不改 BlueprintWorkspace、VPS、其他共享蓝图工作，不清理蓝图或运行数据。
- 后续：先验收 BT-015 多选输入、混合值和原始字段展开；确认后更新 Windows 手动包，再由 Windows 端核对游戏安装、前置 Mod 并做实机验收。BT-006 既有的倒置开关、重启后的 1°配置加载、JSON 原生键及非 90°翻转仍待查。上版安装件备份见 `notes/backups/BuildTools-installed-before-multi-input-20260924.dll`。
