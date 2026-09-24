# Spaceflight Simulator Mod 工作区源码快照

这是供项目接续和设计审阅的**源码快照**，来源于本机《Spaceflight Simulator》Mod 工作区。运行数据、账号数据、蓝图、DLL 备份、构建产物、部署状态和历史交接 ZIP 均未纳入。

| 目录 | 内容 |
| --- | --- |
| `BuildTools/` | 正在开发的 Part / Build / JSON 一体面板；本次 Pro 分析重点是多选 Part 页输入体验 |
| `BlueprintWorkspace/` | 共享蓝图库客户端、服务端和 Windows 手动交接说明 |
| `UITools/` | BuildTools 使用的本地前置 Mod 源码快照 |
| `FlightLogger/`、`FlightVisualizer/` | 工作区内其他辅助项目 |

独立旧版 PartEditor、BuildSettings、PartText 源码来自各自上游仓库；整合版实现位于 `BuildTools/src/`，本快照不重复收入旧版仓库。构建时仍需游戏程序集和前置 Mod，参见各子目录 README。Windows 实机兼容性尚未验证。

当前体验问题、已验证边界和请 Pro 回答的问题见 [PRO_REVIEW_BRIEF.md](PRO_REVIEW_BRIEF.md)。
