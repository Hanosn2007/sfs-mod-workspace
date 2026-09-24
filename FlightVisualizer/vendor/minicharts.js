(function () {
  function finite(v) { return typeof v === "number" && Number.isFinite(v); }

  function nice(n) {
    if (!finite(n)) return "";
    const a = Math.abs(n);
    if (a >= 1e9) return (n / 1e9).toFixed(2) + "G";
    if (a >= 1e6) return (n / 1e6).toFixed(2) + "M";
    if (a >= 1e3) return (n / 1e3).toFixed(1) + "k";
    if (a >= 10) return n.toFixed(1);
    if (a >= 1) return n.toFixed(2);
    if (a === 0) return "0";
    return n.toExponential(1);
  }

  function resizeCanvas(canvas) {
    const dpr = window.devicePixelRatio || 1;
    const rect = canvas.getBoundingClientRect();
    const w = Math.max(320, Math.floor(rect.width * dpr));
    const h = Math.max(180, Math.floor(rect.height * dpr));
    if (canvas.width !== w || canvas.height !== h) {
      canvas.width = w;
      canvas.height = h;
    }
    return { width: w, height: h, dpr };
  }

  function drawLineChart(canvas, series, opts) {
    const ctx = canvas.getContext("2d");
    const size = resizeCanvas(canvas);
    const w = size.width, h = size.height;
    const pad = { left: 58, right: 18, top: 20, bottom: 34 };
    ctx.clearRect(0, 0, w, h);
    ctx.fillStyle = "#070b11";
    ctx.fillRect(0, 0, w, h);

    const normalized = opts?.normalize === true;
    const displaySeries = normalized ? normalizeSeries(series, opts?.robust === true) : series;
    const all = [];
    for (const s of displaySeries) for (const p of s.points) if (finite(p.x) && finite(p.y)) all.push(p);
    if (!all.length) {
      ctx.fillStyle = "#8fa1b7";
      ctx.font = `${13 * size.dpr}px sans-serif`;
      ctx.fillText("无可用数据", pad.left, h / 2);
      return;
    }
    const xMin = opts?.xMin ?? Math.min(...all.map(p => p.x));
    const xMax = opts?.xMax ?? Math.max(...all.map(p => p.x));
    let yMin = normalized ? 0 : (opts?.yMin ?? Math.min(...all.map(p => p.y)));
    let yMax = normalized ? 1 : (opts?.yMax ?? Math.max(...all.map(p => p.y)));
    if (yMin === yMax) { yMin -= 1; yMax += 1; }

    const sx = x => pad.left + (x - xMin) / (xMax - xMin || 1) * (w - pad.left - pad.right);
    const sy = y => h - pad.bottom - (y - yMin) / (yMax - yMin || 1) * (h - pad.top - pad.bottom);

    ctx.strokeStyle = "#1f2d3d";
    ctx.lineWidth = 1;
    ctx.fillStyle = "#8fa1b7";
    ctx.font = `${11 * size.dpr}px sans-serif`;
    for (let i = 0; i <= 4; i++) {
      const y = pad.top + i / 4 * (h - pad.top - pad.bottom);
      ctx.beginPath();
      ctx.moveTo(pad.left, y);
      ctx.lineTo(w - pad.right, y);
      ctx.stroke();
      const val = yMax - i / 4 * (yMax - yMin);
      ctx.fillText(normalized ? `${Math.round(val * 100)}%` : nice(val), 8, y + 4 * size.dpr);
    }

    for (const s of displaySeries) {
      ctx.strokeStyle = s.color || "#42d392";
      ctx.lineWidth = 1.6 * size.dpr;
      for (const segment of splitSegments(s.points)) {
        ctx.beginPath();
        let started = false;
        const pts = downsample(segment, Math.floor(w / 1.8));
        for (const p of pts) {
          const x = sx(p.x), y = sy(p.y);
          if (!started) { ctx.moveTo(x, y); started = true; }
          else ctx.lineTo(x, y);
        }
        ctx.stroke();
      }
    }

    if (opts?.cursorX != null && finite(opts.cursorX)) {
      const x = sx(opts.cursorX);
      ctx.strokeStyle = "#ffffff99";
      ctx.lineWidth = 1 * size.dpr;
      ctx.beginPath();
      ctx.moveTo(x, pad.top);
      ctx.lineTo(x, h - pad.bottom);
      ctx.stroke();
    }

    let lx = pad.left;
    for (const s of series) {
      ctx.fillStyle = s.color || "#42d392";
      ctx.fillRect(lx, 8 * size.dpr, 10 * size.dpr, 3 * size.dpr);
      ctx.fillStyle = "#cbd7e6";
      const label = normalized && s.rawMin != null ? `${s.name} (${nice(s.rawMin)}-${nice(s.rawMax)})` : s.name;
      ctx.fillText(label, lx + 16 * size.dpr, 13 * size.dpr);
      lx += (label.length * 7 + 44) * size.dpr;
    }
  }

  function normalizeSeries(series, robust) {
    return series.map(s => {
      const vals = s.points.map(p => p.y).filter(finite);
      if (!vals.length) return { ...s, points: [] };
      const sorted = [...vals].sort((a, b) => a - b);
      const rawMin = sorted[0];
      const rawMax = sorted[sorted.length - 1];
      const min = robust ? quantile(sorted, 0.02) : rawMin;
      const max = robust ? quantile(sorted, 0.98) : rawMax;
      const span = max - min || 1;
      return {
        ...s,
        rawMin,
        rawMax,
        points: s.points.map(p => ({
          ...p,
          y: finite(p.y) ? clamp((p.y - min) / span, 0, 1) : p.y
        }))
      };
    });
  }

  function quantile(sorted, q) {
    if (!sorted.length) return 0;
    const pos = (sorted.length - 1) * q;
    const base = Math.floor(pos);
    const rest = pos - base;
    const next = sorted[base + 1];
    return next == null ? sorted[base] : sorted[base] + rest * (next - sorted[base]);
  }

  function clamp(v, min, max) {
    return Math.max(min, Math.min(max, v));
  }

  function splitSegments(points) {
    const segments = [];
    let current = [];
    for (const p of points) {
      if (!finite(p.x) || !finite(p.y)) continue;
      if (p.breakBefore && current.length) {
        segments.push(current);
        current = [];
      }
      current.push(p);
    }
    if (current.length) segments.push(current);
    return segments;
  }

  function downsample(points, limit) {
    if (points.length <= limit || limit < 50) return points;
    const out = [];
    const bucket = Math.ceil(points.length / limit);
    for (let i = 0; i < points.length; i += bucket) {
      const slice = points.slice(i, i + bucket);
      if (!slice.length) continue;
      out.push(slice[0]);
      let min = slice[0], max = slice[0];
      for (const p of slice) {
        if (p.y < min.y) min = p;
        if (p.y > max.y) max = p;
      }
      if (min !== slice[0]) out.push(min);
      if (max !== slice[0] && max !== min) out.push(max);
      const last = slice[slice.length - 1];
      if (last !== slice[0]) out.push(last);
    }
    out.sort((a, b) => a.x - b.x);
    return out;
  }

  function drawEventTimeline(canvas, rows, events, cursorIndex) {
    const ctx = canvas.getContext("2d");
    const size = resizeCanvas(canvas);
    const w = size.width, h = size.height;
    ctx.clearRect(0, 0, w, h);
    ctx.fillStyle = "#070b11";
    ctx.fillRect(0, 0, w, h);
    if (!rows.length) return;
    const x0 = rows[0].time_s, x1 = rows[rows.length - 1].time_s;
    const sx = x => 36 * size.dpr + (x - x0) / (x1 - x0 || 1) * (w - 72 * size.dpr);
    const cy = h * 0.55;
    ctx.strokeStyle = "#263546";
    ctx.lineWidth = 2 * size.dpr;
    ctx.beginPath();
    ctx.moveTo(36 * size.dpr, cy);
    ctx.lineTo(w - 36 * size.dpr, cy);
    ctx.stroke();

    for (const ev of events) {
      const x = sx(ev.time_s);
      ctx.fillStyle = ev.color || "#f2b84b";
      ctx.beginPath();
      ctx.arc(x, cy, 4 * size.dpr, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillStyle = "#aebdd0";
      ctx.font = `${10 * size.dpr}px sans-serif`;
      const label = ev.label.length > 22 ? ev.label.slice(0, 22) + "…" : ev.label;
      ctx.save();
      ctx.translate(x + 5 * size.dpr, cy - 10 * size.dpr);
      ctx.rotate(-0.35);
      ctx.fillText(label, 0, 0);
      ctx.restore();
    }

    const row = rows[cursorIndex] || rows[0];
    const cx = sx(row.time_s);
    ctx.strokeStyle = "#ffffffcc";
    ctx.beginPath();
    ctx.moveTo(cx, 10 * size.dpr);
    ctx.lineTo(cx, h - 10 * size.dpr);
    ctx.stroke();
  }

  function drawTrajectory(canvas, rows, cursorIndex, bodyFilter, colors) {
    const ctx = canvas.getContext("2d");
    const size = resizeCanvas(canvas);
    const w = size.width, h = size.height;
    ctx.clearRect(0, 0, w, h);
    ctx.fillStyle = "#05080d";
    ctx.fillRect(0, 0, w, h);

    const filtered = rows.filter(r => bodyFilter === "All" || r.current_body === bodyFilter);
    const pts = filtered.filter(r => finite(r.position_x) && finite(r.position_y));
    if (!pts.length) return;
    const xs = pts.map(p => p.position_x), ys = pts.map(p => p.position_y);
    let minX = Math.min(...xs), maxX = Math.max(...xs), minY = Math.min(...ys), maxY = Math.max(...ys);
    if (minX === maxX) { minX -= 1; maxX += 1; }
    if (minY === maxY) { minY -= 1; maxY += 1; }
    const pad = 34 * size.dpr;
    const scale = Math.min((w - pad * 2) / (maxX - minX), (h - pad * 2) / (maxY - minY));
    const ox = (w - (minX + maxX) * scale) / 2;
    const oy = (h + (minY + maxY) * scale) / 2;
    const sx = x => ox + x * scale;
    const sy = y => oy - y * scale;

    ctx.strokeStyle = "#172231";
    ctx.lineWidth = 1 * size.dpr;
    for (let i = 0; i < 5; i++) {
      const x = pad + i / 4 * (w - pad * 2);
      ctx.beginPath(); ctx.moveTo(x, pad); ctx.lineTo(x, h - pad); ctx.stroke();
      const y = pad + i / 4 * (h - pad * 2);
      ctx.beginPath(); ctx.moveTo(pad, y); ctx.lineTo(w - pad, y); ctx.stroke();
    }

    const byBody = new Map();
    for (const r of filtered) {
      if (!finite(r.position_x) || !finite(r.position_y)) continue;
      if (!byBody.has(r.current_body)) byBody.set(r.current_body, []);
      byBody.get(r.current_body).push(r);
    }
    for (const [body, bodyRows] of byBody) {
      ctx.strokeStyle = colors[body] || "#42d392";
      ctx.lineWidth = 1.4 * size.dpr;
      ctx.beginPath();
      let started = false;
      for (const r of downsample(bodyRows, 1800)) {
        const x = sx(r.position_x), y = sy(r.position_y);
        if (!started) { ctx.moveTo(x, y); started = true; }
        else ctx.lineTo(x, y);
      }
      ctx.stroke();
    }

    const cur = rows[cursorIndex];
    if (cur && (bodyFilter === "All" || cur.current_body === bodyFilter) && finite(cur.position_x) && finite(cur.position_y)) {
      ctx.fillStyle = "#ffffff";
      ctx.beginPath();
      ctx.arc(sx(cur.position_x), sy(cur.position_y), 5 * size.dpr, 0, Math.PI * 2);
      ctx.fill();
    }
  }

  window.MiniCharts = { drawLineChart, drawEventTimeline, drawTrajectory, nice };
})();
