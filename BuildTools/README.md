# Build Tools for Spaceflight Simulator

Build Tools 把原来分散的 Part Editor、Build Settings 和 Part JSON 放进建造界面的同一面板，以 **Part / Build / JSON** 三页切换。Part 页编辑零件参数，Build 页调整建造设置，JSON 页编辑当前零件的 JSON。三页共享位置、尺寸、标题和收起按钮。

## 当前基线

- 面向带内置 Mod Loader 的《Spaceflight Simulator》PC Steam 版；Mod 声明最低游戏版本 `1.6.00.16`。
- `BuildTools.dll` 声明依赖 **UITools 1.1.6**。源码使用 .NET Framework 4.8 和游戏自带的 Unity、Harmony、Mod Loader 程序集。
- 2026-09-23 在 macOS Steam 版构建、安装并加载成功；用户已在游戏内确认 Part、Build、JSON 三页显示正常。BT-002 版本实测过 JSON 输入焦点、滚轮和草稿保留；统一外框后的 BT-003 仍建议在正常编辑中留意这些操作。
- Part 页坐标、朝向和数值变量现在会随所选零件刷新。用户已实测拖动后 X/Y 更新，继续点坐标加减不会跳回旧位置；手工输入时暂停该数值框的自动刷新。
- Build 页原生 Q/E 在当前 Mac 游戏的 Harmony 补丁失败时改用 Rotation Degrees；用户已实测 1°双向旋转。Build Settings 现在独立保存到 `BuildSettings.txt`，用户已核对 Save 立即写盘。当前 Mac 上 Snap to Parts、Part Adaptation、Grid Snap 的相关补丁仍失败，检查边界见 [Bug 审查记录](notes/2026-09-23-bug-audit.md)。
- BT-017 已安装：保留 JSON 字号、固定切页、拖角缩放和短窗口滚动。Part 页多选时固定显示共用的 Position/Orientation，下面按选中零件类型动态读取保存变量；部分适用字段只写入相应零件，一次操作可撤销。单选和多选都将 `fuel_percent`、`force_percent` 显示为百分比，布尔字段用方形开关，原始外观标识在“更多选项”，非有限状态只读。用户在 Mac 游戏内确认 Fuel Tank、Engine、RA LES、Fairing/Separator 及混合多选样例目前正常。样本范围见 [字段覆盖核对](notes/2026-09-24-part-field-coverage.md)。
- Windows 的 DLL 加载、Harmony 补丁、输入和界面尚未实机验证。Windows 交接见 [WINDOWS_HANDOFF.md](WINDOWS_HANDOFF.md)。

## 目录

- `src/`：当前整合版源码。`src/Main.cs` 是 Mod 入口，`src/TabbedPanel.cs` 管理三页。
- `BuildTools.csproj`：构建项目；编译时引用游戏程序集和 UITools DLL。
- `bin/Release/BuildTools.dll`：当前 BT-017 构建产物，与 Mac 游戏安装件一致。
- `PROJECT_STATE.md`、`PROJECT_LOG.md`：本项目的状态和已验证记录。
- `outputs/BuildTools-Windows-handoff-BT-017.zip`：Build Tools + 前置 UITools 的 Windows 手动交接包；包含两个 DLL、两份项目源码、说明及校验值，不含自动安装器。若需要同时交付 Workspace 0.4.0，使用 `../BlueprintWorkspace/outputs/SFS-Windows-Manual-Handoff-0.4.0-BT-017-2026-09-24.zip`。旧包保留作回退基线。

## 与旧 Mod 的关系

Build Tools 已整合独立的 PartEditor、PartText 和 BuildSettings 功能。Windows 安装前应检查目标机器是否有这些旧 Mod；如有，把整个旧目录移出游戏 `Mods` 目录并留作备份。仅把目录改名为 `.disabled` 仍会被当前加载器扫描。其他 Mod、存档、蓝图及设置不应随包覆盖或清理。

来源和作者署名：Build Tools 的整合实现由 Codex 基于 CucumberSpace、Astro The Rabbit、StarMods 相关项目完成；UITools 原项目作者为 StarMods。
