# Windows Codex 交接：Shared Blueprints + Build Tools + UITools

> 本文件记录 0.3.3 历史包。当前最新的多工作区 0.4.0 + BT-006 手动包为 [SFS-Windows-Manual-Handoff-0.4.0-BT-006-2026-09-23.zip](outputs/SFS-Windows-Manual-Handoff-0.4.0-BT-006-2026-09-23.zip)，请以包内 `START_HERE.md` 为准。

本交接包供 Windows 端 Codex 根据目标电脑的实际游戏安装状态**手动安装和定点修复**。包内没有自动安装器。请先解压到游戏目录之外，阅读此文件，再操作游戏文件。

## 包内项目与版本

| 游戏内 Mod | 本包 DLL | 关系 | 本次采用的基线 |
| --- | --- | --- | --- |
| Shared Blueprints / Workspace | `Mods/BlueprintWorkspace/BlueprintWorkspace.dll` | 独立 Mod，不声明依赖 BuildTools 或 UITools | 0.3.3，已在 Mac 游戏内加载，账号登录、发布、导入副本已由用户实测；顶部入口和无遮罩面板的最终视觉验收尚未完成 |
| Build Tools | `Mods/BuildTools/BuildTools.dll` | 依赖 UITools 1.1.6 | 三页统一外框版，Mac 游戏内已加载，Part、Build、JSON 页面显示由用户确认 |
| UI Tools | `Mods/UITools/UITools.dll` | Build Tools 的前置 Mod | 1.1.6，采用 Mac 游戏内实际加载的 DLL |

三份 DLL 的 SHA-256 见 `SHA256SUMS.txt`。`Source/BuildTools`、`Source/UITools` 提供当前源码。`Source/BlueprintWorkspace/client` 是较新的 **0.4.0 开发源码**，与包内已验收的 0.3.3 DLL 不对应；它包含工作区扩展，在服务端版本和完整行为核对前，**不要直接重编译并覆盖 0.3.3**。如需修复 0.3.3 的 Windows 兼容问题，应先确认线上服务 API 和源码差异，再形成可验证的新版本。

此包不含游戏、服务端部署文件、账号密码、加入码、令牌、存档、蓝图或本机个性化配置。成员网站和现有服务地址为 `https://hanson07101.top/blueprints/`；Windows 客户端沿用这一服务，不在 Windows 电脑上启动另一个服务端。

## 安装前核查

1. 确认是带内置 Mod Loader 的《Spaceflight Simulator》PC Steam 版，记录游戏版本。三个 Mod 的目标最低游戏版本为 `1.6.00.16`；若目标机版本不同，先判断兼容性。
2. 找到实际 `Spaceflight Simulator.exe` 所在目录，以及同级 `Mods` 和 `Saving/Settings/ModsSettings.txt`。此前检查过的 Windows 布局为 `Spaceflight Simulator Game/Mods/<Mod 名>/<同名 DLL>`，以目标电脑现场为准。
3. 保存进度并完全退出游戏。清点已有 `BlueprintWorkspace`、`BuildTools`、`UITools`、旧独立 `PartEditor`、`PartText`、`BuildSettings` 及散装 DLL。若目标机器已有更新版本，先保留并分析差异，不盲目降级。
4. 将准备替换的 DLL、设置和旧 Mod 目录备份到游戏 `Mods` **之外**。保留 Windows 机上的 `BuildTools/config.txt`、`BuildTools/Settings.txt`、`UITools/positions.txt` 及 Workspace 配置，不用包内文件覆盖。备份存档和蓝图以便测试回退。
5. 用 PowerShell 的 `Get-FileHash -Algorithm SHA256` 对照 `SHA256SUMS.txt` 核验三个 DLL。

## 手动安装与回退

在确认加载布局后，只把 `Mods/` 下三个同名 DLL 复制到游戏对应 Mod 目录，**不要把 `Source/` 复制到游戏**。先放 UITools，再放 BuildTools 和 BlueprintWorkspace。BuildTools 已整合独立的 PartEditor、PartText、BuildSettings；若目标机存在这些旧 Mod，把整个旧目录或散装 DLL 移出 `Mods` 并留存备份。仅在 `Mods` 内改名加 `.disabled` 仍可能被加载器扫描；本机曾因此出现缺少同名 DLL 的错误。

启动后若三个 Mod 未自动启用，先备份 `Saving/Settings/ModsSettings.txt`，再只把 `modsActive` 中的 `UITools`、`buildtools`、`BlueprintWorkspace` 设为 `true`；保留其他 Mod 和资源设置。安装失败时退出游戏，从备份恢复 DLL、旧目录和设置。不要删除任何蓝图、世界或其他 Mod 数据。

## Windows 实机验收

1. Mod Loader 或游戏日志显示 `Loaded UI Tools`、`Loaded Build Tools`、`Loaded Shared Blueprints`；没有新的依赖加载或补丁错误。
2. 建造页的 Build Tools 只有一个面板，Part / Build / JSON 三页可切换；JSON 聚焦时方向键和滚轮作用于文本，切页后草稿仍在，失焦后建造输入恢复。
3. New 左侧可见 Workspace 入口，`F8` 可打开；面板可通过标题拖动，底部文字完整，打开时不会误拖火箭，关闭后建造输入恢复。
4. 使用自己的成员账号登录，查看已保存蓝图和共享库；用测试蓝图发布、导入副本，确认原有蓝图未被覆盖。断网时只应报错，本地蓝图仍可使用。
5. 若失败，保留游戏版本、三份 DLL 哈希、日志错误段和复现步骤。先定位加载、UI/输入还是服务 API；只修复已复现的问题。Windows 加载与交互目前均未实测通过，不要把 ZIP 校验当作游戏内验收。

## 后续更新

版本管理器以后再考虑。当前每次更新应使用带版本号的新交接包、哈希清单、变更说明和可恢复备份，由 Windows 端 Codex 核对后替换。当前 UITools 源码会检查其上游更新，并在用户确认后覆盖本地 DLL；在确认新上游版与 BuildTools 兼容前，不应直接接受这个更新提示。Workspace 和 BuildTools 当前没有对应的自动更新渠道。

BuildTools 的细节及编译参数见 `ProjectDocs/BuildTools/WINDOWS_HANDOFF.md`；Workspace 的产品与服务说明见 `ProjectDocs/BlueprintWorkspace/README.md`。交付 Windows 端结果时记录安装路径、游戏版本、实际 DLL 哈希、验收结果与未解决问题。
