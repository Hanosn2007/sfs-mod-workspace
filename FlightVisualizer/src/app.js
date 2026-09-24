const COLORS = {
  Earth: "#58a6ff",
  Mars: "#ff7a45",
  Sun: "#ffd166",
  Moon: "#c8d0da"
};

const state = {
  rows: [],
  events: [],
  phases: [],
  cursor: 0,
  selectedPhase: 0,
  bodyFilter: "",
  activeTab: "replay",
  chartScope: "phase",
  charts: []
};

const numberColumns = new Set([
  "time_s", "mission_time_s", "body_radius_m", "local_gravity_mps2",
  "altitude_m", "terrain_altitude_m", "speed_mps", "velocity_x", "velocity_y",
  "position_x", "position_y", "vertical_speed_mps", "horizontal_speed_mps",
  "angle_deg", "velocity_angle_deg", "acceleration_x", "acceleration_y",
  "acceleration_mps2", "mass_t", "thrust", "throttle", "fuel_percent", "twr",
  "apoapsis_m", "periapsis_m", "apoapsis_altitude_m", "periapsis_altitude_m",
  "orbit_eccentricity", "semi_major_axis_m", "active_part_count"
]);

const boolColumns = new Set(["is_engine_on", "is_parachute_deployed", "is_in_atmosphere", "is_landed"]);

window.addEventListener("DOMContentLoaded", () => {
  const input = document.getElementById("fileInput");
  const drop = document.getElementById("dropZone");
  input.addEventListener("change", e => loadFile(e.target.files[0]));
  drop.addEventListener("dragover", e => { e.preventDefault(); drop.classList.add("dragover"); });
  drop.addEventListener("dragleave", () => drop.classList.remove("dragover"));
  drop.addEventListener("drop", e => {
    e.preventDefault();
    drop.classList.remove("dragover");
    loadFile(e.dataTransfer.files[0]);
  });
  document.querySelectorAll(".tab").forEach(btn => btn.addEventListener("click", () => setTab(btn.dataset.tab)));
  document.getElementById("timeSlider").addEventListener("input", e => setCursor(Number(e.target.value)));
  document.getElementById("bodyFilter").addEventListener("change", e => {
    state.bodyFilter = e.target.value;
    renderTrajectory();
  });
  document.getElementById("chartScope").addEventListener("change", e => {
    state.chartScope = e.target.value;
    drawGlobalCharts();
  });
  window.addEventListener("resize", () => renderAll());

  const params = new URLSearchParams(window.location.search);
  const csvUrl = params.get("csv");
  if (csvUrl) {
    fetch(csvUrl)
      .then(r => {
        if (!r.ok) throw new Error("CSV load failed: " + r.status);
        return r.text();
      })
      .then(text => loadCsvText(text, csvUrl.split("/").pop() || csvUrl))
      .catch(err => {
        document.getElementById("warnings").innerHTML =
          `<div class="warning">无法自动加载 CSV：${err.message}</div>`;
      });
  }
});

function loadFile(file) {
  if (!file) return;
  const reader = new FileReader();
  reader.onload = () => {
    loadCsvText(String(reader.result), file.name);
  };
  reader.readAsText(file);
}

function loadCsvText(text, fileName) {
  const parsed = parseCsv(text);
  const rows = cleanRows(parsed);
  state.rows = rows;
  state.events = collectEvents(rows);
  state.phases = buildPhases(rows);
  state.cursor = 0;
  state.selectedPhase = 0;
  state.activeTab = "replay";
  state.bodyFilter = firstBody(rows);
  document.getElementById("fileName").textContent = fileName || "flight.csv";
  document.getElementById("dropZone").classList.add("hidden");
  document.getElementById("app").classList.remove("hidden");
  document.getElementById("timeSlider").max = Math.max(0, rows.length - 1);
  renderAll();
  return rows.length;
}

function parseCsv(text) {
  const rows = [];
  let row = [], cell = "", quoted = false;
  for (let i = 0; i < text.length; i++) {
    const ch = text[i], next = text[i + 1];
    if (quoted) {
      if (ch === '"' && next === '"') { cell += '"'; i++; }
      else if (ch === '"') quoted = false;
      else cell += ch;
    } else {
      if (ch === '"') quoted = true;
      else if (ch === ",") { row.push(cell); cell = ""; }
      else if (ch === "\n") { row.push(cell); rows.push(row); row = []; cell = ""; }
      else if (ch !== "\r") cell += ch;
    }
  }
  if (cell.length || row.length) { row.push(cell); rows.push(row); }
  const header = rows.shift() || [];
  return rows.filter(r => r.length > 1).map(r => Object.fromEntries(header.map((h, i) => [h, r[i] ?? ""])));
}

function cleanRows(raw) {
  if (!raw.length) return [];
  const firstMission = toNumber(raw[0].mission_time_s) ?? 0;
  return raw.map((r, i) => {
    const out = { _index: i };
    for (const [k, v] of Object.entries(r)) {
      if (numberColumns.has(k)) out[k] = toNumber(v);
      else if (boolColumns.has(k)) out[k] = String(v).toLowerCase() === "true";
      else out[k] = v || "";
    }
    out.mission_elapsed_s = out.mission_time_s != null ? out.mission_time_s - firstMission : null;
    return out;
  });
}

function toNumber(v) {
  if (v == null || v === "") return null;
  const n = Number(v);
  return Number.isFinite(n) ? n : null;
}

function collectEvents(rows) {
  const events = [];
  for (let i = 1; i < rows.length; i++) {
    if (rows[i].mission_time_s != null && rows[i - 1].mission_time_s != null && rows[i].mission_time_s < rows[i - 1].mission_time_s - 0.25) {
      events.push({ index: i, time_s: rows[i].time_s, label: `rewind ${format(rows[i - 1].mission_time_s - rows[i].mission_time_s)}s`, color: "#ff6b6b" });
    }
    if (rows[i].native_event) {
      events.push({ index: i, time_s: rows[i].time_s, label: rows[i].native_event, color: COLORS[rows[i].current_body] || "#f2b84b" });
    }
  }
  return events;
}

function buildPhases(rows) {
  if (!rows.length) return [];
  const phases = [];
  let start = 0;
  for (let i = 1; i < rows.length; i++) {
    const r = rows[i], p = rows[i - 1];
    const boundary = r.current_body !== p.current_body ||
      Boolean(r.native_event && /changed_soi|atmosphere|orbit:|landed|takeoff/.test(r.native_event));
    if (boundary && i - start > 5) {
      phases.push(makePhase(rows, start, i - 1));
      start = i;
    }
  }
  phases.push(makePhase(rows, start, rows.length - 1));
  return phases;
}

function makePhase(rows, start, end) {
  const a = rows[start], b = rows[end];
  const event = rows.slice(start, Math.min(end + 1, start + 20)).find(r => r.native_event)?.native_event || "";
  let name = a.current_body || "Unknown";
  if (event) name += " / " + event.split("|")[0];
  return { start, end, name, body: a.current_body, event };
}

function setTab(name) {
  state.activeTab = name;
  document.querySelectorAll(".tab").forEach(b => b.classList.toggle("active", b.dataset.tab === name));
  document.querySelectorAll(".tab-panel").forEach(p => p.classList.toggle("active", p.id === name));
  document.getElementById("tabHint").textContent = "当前：" + (name === "replay" ? "任务回放" : "物理分析");
  requestAnimationFrame(() => {
    if (name === "replay") renderReplay();
    else renderPhases();
  });
}

function setCursor(index) {
  state.cursor = Math.max(0, Math.min(state.rows.length - 1, index));
  state.selectedPhase = phaseIndexForCursor(state.cursor);
  renderReplay();
}

function renderAll() {
  if (!state.rows.length) return;
  renderSummary();
  renderWarnings();
  renderBodyFilter();
  if (state.activeTab === "analysis") renderPhases();
  else {
    renderReplay();
    renderPhases();
  }
}

function renderSummary() {
  const rows = state.rows;
  const bodies = [...new Set(rows.map(r => r.current_body).filter(Boolean))].join(" → ");
  const cards = [
    ["记录行数", rows.length.toLocaleString()],
    ["Logger 时长", fmtTime(last(rows).time_s)],
    ["Mission 时长", fmtTime(last(rows).mission_elapsed_s)],
    ["经过天体", bodies],
    ["最大速度", fmtUnit(max(rows, "speed_mps"), "m/s")],
    ["最大高度", fmtUnit(max(rows, "altitude_m"), "m")],
    ["最大推力", fmtUnit(max(rows, "thrust"), "")]
  ];
  document.getElementById("summaryCards").innerHTML = cards.map(([label, value]) =>
    `<div class="card"><div class="label">${label}</div><div class="value">${value}</div></div>`).join("");
}

function renderWarnings() {
  const rewinds = state.events.filter(e => e.label.startsWith("rewind"));
  const missing = requiredColumns().filter(c => !(c in state.rows[0]));
  const html = [];
  html.push(`<div class="warning">轨迹图默认按当前天体局部坐标显示。不要用 All 判断真实太阳系全局轨道，SFS CSV 中的 position_x/y 会随 SOI 切换参考系。</div>`);
  html.push(`<div class="warning">多指标曲线默认显示当前阶段，并使用稳健相对比例显示；切到“全任务”时会保留回退和阶段断点，不再硬连接不连续区间。</div>`);
  if (rewinds.length) html.push(`<div class="warning">rewind detected：发现 ${rewinds.length} 次 mission_time_s 回退，图中已用红色事件点标记。</div>`);
  if (missing.length) html.push(`<div class="warning">缺少字段：${missing.join(", ")}。相关图表会自动隐藏或留空。</div>`);
  document.getElementById("warnings").innerHTML = html.join("");
}

function requiredColumns() {
  return ["position_x", "position_y", "current_body", "time_s", "mission_time_s", "altitude_m", "speed_mps"];
}

function renderBodyFilter() {
  const select = document.getElementById("bodyFilter");
  const bodies = ["All", ...new Set(state.rows.map(r => r.current_body).filter(Boolean))];
  select.innerHTML = bodies.map(b => `<option value="${b}">${b}</option>`).join("");
  if (!bodies.includes(state.bodyFilter)) state.bodyFilter = bodies[1] || "All";
  select.value = state.bodyFilter;
  document.getElementById("bodyLegend").innerHTML = bodies.filter(b => b !== "All").map(b =>
    `<span class="legend-item"><i class="dot" style="background:${COLORS[b] || "#42d392"}"></i>${b}</span>`).join("");
}

function renderReplay() {
  renderTrajectory();
  renderHud();
  MiniCharts.drawEventTimeline(document.getElementById("eventCanvas"), state.rows, state.events, state.cursor);
  document.getElementById("timeSlider").value = state.cursor;
  const row = state.rows[state.cursor];
  document.getElementById("cursorLabel").textContent = row ? `${fmtTime(row.time_s)} / mission ${fmtTime(row.mission_elapsed_s)}` : "";
  drawGlobalCharts();
}

function renderTrajectory() {
  MiniCharts.drawTrajectory(document.getElementById("trajectoryCanvas"), state.rows, state.cursor, state.bodyFilter, COLORS);
}

function renderHud() {
  const r = state.rows[state.cursor] || {};
  const items = [
    ["天体", r.current_body],
    ["Logger", fmtTime(r.time_s)],
    ["Mission", fmtTime(r.mission_elapsed_s)],
    ["高度", fmtUnit(r.altitude_m, "m")],
    ["离地高度", fmtUnit(r.terrain_altitude_m, "m")],
    ["速度", fmtUnit(r.speed_mps, "m/s")],
    ["推力", fmtUnit(r.thrust, "")],
    ["TWR", format(r.twr)],
    ["燃料", fmtUnit(r.fuel_percent, "%")],
    ["质量", fmtUnit(r.mass_t, "t")],
    ["大气", r.is_in_atmosphere ? "是" : "否"],
    ["着陆", r.is_landed ? "是" : "否"]
  ];
  document.getElementById("hudGrid").innerHTML = items.map(([k, v]) => `<div class="hud-item"><span>${k}</span><span>${v ?? ""}</span></div>`).join("");
}

function drawGlobalCharts() {
  const rows = chartRows(), cursorX = state.rows[state.cursor]?.time_s;
  const label = document.getElementById("chartScopeLabel");
  if (label) label.textContent = chartScopeText(rows);
  line("chartAltitudeSpeed", [
    serie(rows, "altitude_m", "高度", "#58a6ff"),
    serie(rows, "speed_mps", "速度", "#42d392")
  ], cursorX, true);
  line("chartThrust", [
    serie(rows, "thrust", "推力", "#ff7a45"),
    serie(rows, "twr", "TWR", "#ffd166"),
    serie(rows, "mass_t", "质量", "#c8d0da")
  ], cursorX, true);
  line("chartOrbit", [
    serie(rows, "apoapsis_altitude_m", "远地点高度", "#58a6ff"),
    serie(rows, "periapsis_altitude_m", "近地点高度", "#ff7a45")
  ], cursorX, true);
  line("chartLanding", [
    serie(rows, "terrain_altitude_m", "离地高度", "#c8d0da"),
    serie(rows, "speed_mps", "速度", "#42d392"),
    serie(rows, "acceleration_mps2", "加速度", "#ffd166")
  ], cursorX, true);
}

function renderPhases() {
  const list = document.getElementById("phaseList");
  list.innerHTML = state.phases.map((p, i) => {
    const a = state.rows[p.start], b = state.rows[p.end];
    return `<div class="phase-row ${i === state.selectedPhase ? "active" : ""}" data-i="${i}">
      <div class="phase-name">${p.name}</div>
      <div class="phase-meta">${fmtTime(a.time_s)} - ${fmtTime(b.time_s)} · ${p.end - p.start + 1} rows</div>
    </div>`;
  }).join("");
  list.querySelectorAll(".phase-row").forEach(el => el.addEventListener("click", () => {
    state.selectedPhase = Number(el.dataset.i);
    state.cursor = state.phases[state.selectedPhase].start;
    renderPhases();
    renderReplay();
  }));
  renderPhaseCharts();
}

function renderPhaseCharts() {
  const phase = state.phases[state.selectedPhase] || state.phases[0];
  if (!phase) return;
  const rows = state.rows.slice(phase.start, phase.end + 1);
  document.getElementById("phaseTitle").textContent = phase.name;
  document.getElementById("phaseRange").textContent = `${fmtTime(rows[0].time_s)} - ${fmtTime(last(rows).time_s)}`;
  const metrics = [
    ["最大速度", fmtUnit(max(rows, "speed_mps"), "m/s")],
    ["最大高度", fmtUnit(max(rows, "altitude_m"), "m")],
    ["最大加速度", fmtUnit(max(rows, "acceleration_mps2"), "m/s²")],
    ["燃料变化", `${format(rows[0].fuel_percent)}% → ${format(last(rows).fuel_percent)}%`]
  ];
  document.getElementById("phaseMetrics").innerHTML = metrics.map(([k, v]) =>
    `<div class="metric"><div class="label">${k}</div><div class="value">${v}</div></div>`).join("");
  line("phaseForceChart", [
    serie(rows, "thrust", "推力", "#ff7a45"),
    serie(rows, "twr", "TWR", "#ffd166"),
    serie(rows, "mass_t", "质量", "#c8d0da"),
    serie(rows, "acceleration_mps2", "加速度", "#42d392")
  ], null, true);
  line("phaseOrbitChart", [
    serie(rows, "apoapsis_altitude_m", "远地点高度", "#58a6ff"),
    serie(rows, "periapsis_altitude_m", "近地点高度", "#ff7a45"),
    serie(rows, "orbit_eccentricity", "偏心率", "#ffd166")
  ], null, true);
  line("phaseFlightChart", [
    serie(rows, "altitude_m", "高度", "#58a6ff"),
    serie(rows, "terrain_altitude_m", "离地高度", "#c8d0da"),
    serie(rows, "speed_mps", "速度", "#42d392")
  ], null, true);
}

function line(id, series, cursorX, normalize = false) {
  MiniCharts.drawLineChart(document.getElementById(id), series.filter(s => s.points.length), { cursorX, normalize, robust: true });
}

function serie(rows, key, name, color) {
  const points = [];
  let prev = null;
  for (const r of rows) {
    if (r.time_s == null || r[key] == null) continue;
    points.push({ x: r.time_s, y: r[key], breakBefore: prev && isDiscontinuous(prev, r) });
    prev = r;
  }
  return { name, color, points };
}

function chartRows() {
  const rows = state.rows;
  if (!rows.length || state.chartScope === "all") return rows;
  const cur = rows[state.cursor] || rows[0];
  if (state.chartScope === "body") return rows.filter(r => r.current_body === cur.current_body);
  const phase = state.phases[phaseIndexForCursor(state.cursor)] || state.phases[0];
  return phase ? rows.slice(phase.start, phase.end + 1) : rows;
}

function chartScopeText(rows) {
  if (!rows.length) return "";
  const a = rows[0], b = last(rows);
  const body = a.current_body || "Unknown";
  const range = `${fmtTime(a.time_s)} - ${fmtTime(b.time_s)}`;
  if (state.chartScope === "all") return `显示全任务：${range}`;
  if (state.chartScope === "body") return `显示 ${body} 天体段：${range}`;
  return `显示当前阶段：${body}，${range}`;
}

function phaseIndexForCursor(index) {
  const found = state.phases.findIndex(p => index >= p.start && index <= p.end);
  return found >= 0 ? found : 0;
}

function isDiscontinuous(a, b) {
  if (a.current_body !== b.current_body) return true;
  if (a.mission_time_s != null && b.mission_time_s != null && b.mission_time_s < a.mission_time_s - 0.25) return true;
  if (a.time_s != null && b.time_s != null) {
    const dt = b.time_s - a.time_s;
    if (dt <= 0 || dt > 30) return true;
  }
  return false;
}

function max(rows, key) {
  const vals = rows.map(r => r[key]).filter(v => typeof v === "number" && Number.isFinite(v));
  return vals.length ? Math.max(...vals) : null;
}

function firstBody(rows) {
  return rows.find(r => r.current_body)?.current_body || "All";
}

function last(rows) {
  return rows[rows.length - 1] || {};
}

function fmtTime(s) {
  if (s == null || !Number.isFinite(s)) return "";
  if (Math.abs(s) >= 86400) return `${(s / 86400).toFixed(2)} d`;
  if (Math.abs(s) >= 3600) return `${(s / 3600).toFixed(2)} h`;
  if (Math.abs(s) >= 60) return `${(s / 60).toFixed(1)} min`;
  return `${s.toFixed(1)} s`;
}

function fmtUnit(v, unit) {
  if (v == null || !Number.isFinite(v)) return "";
  return `${format(v)}${unit ? " " + unit : ""}`;
}

function format(v) {
  if (v == null || !Number.isFinite(v)) return "";
  return MiniCharts.nice(v);
}

window.FlightVisualizer = { loadCsvText };
