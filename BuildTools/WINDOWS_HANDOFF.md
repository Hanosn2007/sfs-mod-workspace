# Windows Codex 交接：Build Tools + UITools

请先阅读本文件和 `README.md`，再检查目标 Windows 电脑的游戏安装状态。**本包没有自动安装器；由 Windows 端 Codex 根据实机证据手动安装，并只修复复现的兼容性问题。**

## 交付物和基线

ZIP 根目录的 `Mods/BuildTools/BuildTools.dll` 是 2026-09-23 macOS 游戏内已加载并进行主要 UI 验收的 BT-014 版本；`Mods/UITools/UITools.dll` 是它实际使用的前置 Mod，版本 `1.1.6`。`Source/BuildTools` 和 `Source/UITools` 是当前可编辑源码。UITools 重新编译后哈希可能不同；Windows 端若采用重编译产物，应将其视作新版本重新验收。`SHA256SUMS.txt` 给出两个 DLL 哈希。包内不含游戏、存档、蓝图、账号或本机个性化配置。

Build Tools 的 `ModNameID` 为 `buildtools`，显示名为 `Build Tools`，声明最低游戏版本 `1.6.00.16` 和依赖 `UITools 1.1.6`。其 `ModVersion` 字符串仍为 `0.1-macos`；这是当前程序集元数据，不能据此认为 DLL 是 macOS 原生程序。UITools 的 `ModNameID` 为 `UITools`，显示名为 `UI Tools`。两份都是托管 DLL，但**尚未在 Windows 游戏中验证**。

## Windows 端先核查

1. 确认目标是 PC Steam 版，记录游戏版本、`Spaceflight Simulator.exe` 所在目录、游戏是否自带 Mod Loader，以及 `Spaceflight Simulator_Data/Managed/Assembly-CSharp.dll` 是否存在。不要猜测路径或直接覆盖文件。
2. 保存游戏进度并完全退出游戏。记录 `Mods` 中已有的 BuildTools、UITools、PartEditor、PartText、BuildSettings 及散装同名 DLL；同时查看 `Saving/Settings/ModsSettings.txt`。若另有依赖 UITools 的 Mod，保留其运行所需版本和配置。
3. 对任何准备替换的 DLL、旧 Mod 目录和 `ModsSettings.txt` 先做可恢复备份，备份放在游戏 `Mods` **之外**。保留目标机器现有 `BuildTools/config.txt`、`BuildTools/Settings.txt`、`BuildTools/BuildSettings.txt` 和 `UITools/positions.txt`，除非有具体故障证明需要调整。
4. 核对 ZIP 内 DLL 与 `SHA256SUMS.txt`。检查游戏版本和 Mod Loader 的实际加载规则；此前从本机加载器核对的 Windows 布局为 EXE 同级的 `Spaceflight Simulator Game/Mods/<Mod 名>/<同名 DLL>`，但仍以目标机器现场为准。

PowerShell 中可用 `Get-FileHash -Algorithm SHA256 .\Mods\BuildTools\BuildTools.dll` 和 `Get-FileHash -Algorithm SHA256 .\Mods\UITools\UITools.dll`，逐一与 `SHA256SUMS.txt` 对照。

## 手动安装

在确认布局后，把包内 `Mods/UITools/UITools.dll` 放入游戏 `Mods/UITools/UITools.dll`，把 `Mods/BuildTools/BuildTools.dll` 放入 `Mods/BuildTools/BuildTools.dll`。先处理 UITools，后处理 BuildTools。若有旧的独立 `PartEditor`、`PartText`、`BuildSettings` 或相应散装 DLL，把它们完整移出 `Mods` 并保留备份，避免重复窗口和补丁冲突。**不要仅在 Mods 内重命名为 `.disabled`**：本机日志显示加载器仍扫描此类目录并报缺少同名 DLL。

游戏若没有自动启用两个 Mod，备份后只在 `Saving/Settings/ModsSettings.txt` 的 `modsActive` 中设置 `"UITools": true` 和 `"buildtools": true`；保留其他键和值。不要复制本机的设置文件到 Windows。若需要回退，退出游戏后从备份恢复 DLL、旧 Mod 目录和设置。

## 验收与定点修复

1. 启动游戏，查看 Mod Loader 列表或日志，确认 `Loaded UI Tools` 与 `Loaded Build Tools`，且无 Windows 特有的程序集/依赖加载错误。
2. 进入建造界面，确认只出现一个 Build Tools 面板；Part、Build、JSON 三页能切换，滚动 Part 参数时顶部切页按钮仍可见。拖动右下角 `//` 可连续调整宽高，切页后保持。缩小 Build 页高度后可滚到 Defaults/Save/Reset；放大后 JSON 页字号行、文本框和 Save Part 不重叠且可点击。收起窗口时，固定切页按钮和缩放手柄应一起隐藏，展开后恢复。
3. 在 Part 页选中测试零件，拖动后确认 X/Y 立即更新；随后点击 X/Y 加减按钮，应从新位置微调，不跳回拖动前坐标。手工编辑数值时，正在输入的内容不应被自动刷新覆盖。
4. 在 Build 页将 Rotation Degrees 设为 1，Invert Rotate Keybinds 关闭；用测试零件检查原生 Q/E 分别旋转约 1°，再点击 Save 并重启确认该值保留。补丁失败时会启用可回退的原生 Q/E 回调替代路径；检查日志中是否出现 `BuildTools rotation fallback bound native Q/E to Rotation Degrees.`。
5. 未选零件时，窄窗口的 JSON 页应完整显示 `Select a part...`。选中一个零件后点击字号 `- / +`，确认只有 JSON 字体立即变化，重启后保留。再输入与编辑 JSON：聚焦时方向键只移动文本光标、建造快捷键不触发；文本区滚轮滚动文本而不缩放画布；切页回来草稿仍在。失焦后方向键和画布滚轮恢复游戏行为。
6. 在 Part 页单选测试 Fuel Tank，确认 `Fuel (%)` 及其余原始字段可见并可编辑；单选 Engine Titan，检查 `Engine enabled`、`Gimbal enabled`、`Heat effect (creative)` 仅在实际字段存在时显示，切换后状态正确。Mac 用户已确认这三个开关可切换。未知零件只保留原始字段，不猜其含义。多选参数写入尚未实现，见 `multi-part-editor-design.md`，不要将设计方案当成现有功能。
7. 抽查 Part 参数修改与 Build 设置，保存测试蓝图后重开验证，避免在原有重要蓝图上做破坏性试验。
8. 如出现异常，保留游戏版本、完整错误段、复现步骤和本次改动前后的 DLL 哈希。先区分 Mod 加载失败、Harmony 补丁失败、UI 显示、输入冲突，再在 `Source/` 中定点修复并重新编译。不要把 macOS 上存在的警告直接当作 Windows 新故障，也不要为没有复现的假设做大范围重写。

本机 macOS 日志中，BuildTools 已加载，但多个 `BuildSettings` 和 `PartText` Harmony 补丁出现 `IL Compile Error` 并被安全跳过；其影响尚未逐项确认。Windows 端应依据自己的日志和功能复现决定是否修复。还出现过 `PartEditor.disabled-by-buildtools` 被扫描的错误；按上面的步骤移出旧目录即可排除该命名问题。

本机已确认：Snap to Parts、Part Adaptation、Grid Snap 在补丁被跳过时无效；非 90°翻转/镜像补丁也未生效。详见随包的 `bug-audit.md`。这些功能在 Windows 上是否可用，必须以目标机日志和操作验收为准。

## Windows 上重编译

在目标机安装适合 `net48` 项目的 .NET SDK/Framework 开发组件后，从 ZIP 根目录运行 PowerShell，并按实机路径设置变量：

```powershell
$GameDir = 'D:\SteamLibrary\steamapps\common\Spaceflight Simulator\Spaceflight Simulator Game'
$Managed = Join-Path $GameDir 'Spaceflight Simulator_Data\Managed'
$PackageMods = Join-Path (Get-Location) 'Mods'
dotnet build 'Source\BuildTools\BuildTools.csproj' -c Release -p:SFSGameRoot="$GameDir" -p:SFSManagedDir="$Managed" -p:SFSModsDir="$PackageMods"
```

只有需要修改前置 Mod 时才构建 `Source/UITools/UITools.csproj`，使用同一 `SFSGameRoot`、`SFSManagedDir` 属性，并先解决编译引用。BuildTools 的 `SFSModsDir` 指向已有 UITools DLL 的目录。游戏 DLL 只用于本机编译引用，不放进交接包。修复后先在测试目录或备份保护下替换，再进行上面的 Windows 实机验收。

交付 Windows 端结果时说明：安装路径、游戏版本、实际 DLL 哈希、载入日志、各项手动验收结果、做过的修复及尚未验证的项目。
