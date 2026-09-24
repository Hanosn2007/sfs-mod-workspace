# SFS Flight Visualizer

Windows 本地静态网页工具，用来查看 Spaceflight Simulator Flight Logger 导出的 CSV。

## 使用

1. 打开 `FlightVisualizer/index.html`。
2. 把 `flight_*.csv` 拖到页面中，或点击 `选择 CSV`。
3. 在 `任务回放` 中拖动时间轴查看轨迹、事件、HUD 和曲线联动。
4. `任务回放` 下方曲线默认显示当前阶段，可切换为当前天体或全任务。
5. 在 `物理分析` 中按阶段查看起飞、入轨、转移、再入和着陆数据。

## 特性

- 不需要 Node、Python、服务器或联网。
- 图表库在 `vendor/minicharts.js`，没有 CDN 依赖。
- 自动识别 `mission_time_s` 回退，并在事件时间轴标记 `rewind detected`。
- 曲线遇到时间回退、SOI 切换或长时间断档会断开，不会硬连成竖线。
- 支持新版 Flight Logger 字段：`body_radius_m`、`local_gravity_mps2`、`velocity_angle_deg`、`apoapsis_altitude_m`、`periapsis_altitude_m`。

## 注意

- 工具只在浏览器内读取 CSV，不会修改原始文件。
- 如果打开旧版 CSV，缺少的字段会在页面顶部提示，相关图表会留空。
