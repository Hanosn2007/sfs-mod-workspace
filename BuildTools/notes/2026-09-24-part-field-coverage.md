# Part 字段覆盖核对（2026-09-24）

## 样本与边界

- 只读扫描本机 `Saving/Blueprints/*/Blueprint.txt` 的当时快照：14 份蓝图、1339 个零件实例、29 种零件名称。按 `(零件名, 保存类别, 字段键)` 去重后有 93 个字段：数值 `N` 48、布尔 `B` 26、文本 `T` 19。蓝图在游戏运行时可能继续变化；这里不记录蓝图名称或内容。
- 样本 `parts[]` 的顶层键为 `n/p/o/t/N/B/T`。Part 页固定提供位置 `p` 和朝向 `o`；动态区从当前选中零件的 `doubleVariables`、`boolVariables`、`stringVariables` 保存字典读取 `N/B/T`，按零件名称与字段类型、字段键分组。因此不依赖这 29 个名称的硬编码名单；无保存变量的零件也会明确显示。`n` 是分组身份，`t` 等完整原始序列化数据仍可在 JSON 页查看，不将它们伪装成普通零件参数。
- 本样本不能证明游戏内未出现过的官方零件或第三方 Mod 字段取值范围。新零件的保存字典仍由通用类型控件呈现；语义不明的字段保留原始键名，不猜测枚举值或自动施加范围。

## 样本零件与字段

下表只列保存变量；所有零件另有顶层 `n/p/o/t`。`—` 表示样本中该保存类别没有字段。

| 零件 | `N` 数值 | `B` 布尔 | `T` 文本 |
| --- | --- | --- | --- |
| Capsule | temperature | — | — |
| Cone、Cone Round、Cone Side | size | — | color_tex、shape_tex |
| Docking Port | force_multiplier、sep_force_multiplier、width | — | — |
| Engine Frontier、Hawk、Kolibri、Titan、Valiant | — | engine_on、gimbal_on、heat_on__for_creative_use | — |
| Fairing | force_percent、height、width_a、width_b、width_original | adapt_to_tank、detach_edge、occupied_a | color_tex、fragment、shape_tex |
| Fairing Cone | force_percent、height、width、width_original | adapt_to_tank、detach_edge | color_tex、fragment、shade_tex |
| Fairing Cone Round | force_percent、width、width_original | adapt_to_tank、detach_edge | color_tex、fragment、shade_tex |
| Fuel Tank | fuel_percent、height、width_a、width_b、width_original | — | color_tex、shape_tex |
| Heat Shield | shield_temp、width、width_original | — | — |
| Landing Leg | state、state_target | — | — |
| Parachute | animation_state、deploy_state、temperature | — | — |
| Parachute Side | animation_state、deploy_state | — | — |
| Placeholder ION | — | engine_on | — |
| Probe | width | — | — |
| RA LES | fuel_percent、sequence | auto_detach | — |
| RCS | — | — | — |
| Separator | force_percent、height、height_max、width、width_b | — | color_tex |
| Side Separator | force_percent | — | fragment |
| Solar Array 2、Solar Array 3 | state、state_target | — | — |
| Strut | size | — | — |
| Wheel Big、Wheel Medium | — | wheel_on | — |

## 输入规则

| 保存数据 | Part 页控件与写入 |
| --- | --- |
| `N` 中的 `fuel_percent`、`force_percent` | 显示 0–100%，步进 1%；写回 0–1，输入与步进均限界。规则按键名适用于 Fuel Tank、RA LES、Fairing、Separator 等实际带字段的零件。 |
| 其他有限 `N` | 数值输入与左右步进；多选混合值保留各自值，点击步进分别相对调整，输入数值则统一到指定值。未知范围不擅自限界。 |
| 非有限 `N`（样本中的 `temperature`、`shield_temp` 为 `-Infinity`） | 显示原始状态且只读，避免无效的加减按钮和意外写回；可在 JSON 页查看完整数据。 |
| `B` | 方形单击开关；混合状态明确标示，只修改实际带字段的零件。 |
| `T` | 文本输入；`color_tex`、`shape_tex`、`shade_tex`、`fragment` 默认在“更多选项”，避免与游戏原生外观控件重复。没有证据支持的候选值不做下拉框。 |

## 尚需游戏内确认

- 选一件 RA LES，看 `Fuel (%)` 是否按 100% 显示并能单击步进；选一件 Fairing 或 Separator，看 `Force (%)` 是否按 50% 显示。
- 多选不同类型（例如 Fuel Tank、Fairing、Engine、RCS），确认每种类型分组、适用数、开关和“无保存变量”提示；展开“更多选项”确认外观字段仍可找到。
- 选带 `temperature`/`shield_temp` 的零件，确认 `-Infinity` 以只读状态显示且不产生无效步进。

这些是本机源码和蓝图结构的覆盖核对；具体游戏内控件手感、游戏原生联动及 Windows 实机仍需验收。
