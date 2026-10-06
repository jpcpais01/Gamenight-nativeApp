namespace GameNight.Link;

/// <summary>
/// The web controller the PC serves (see <see cref="WebLink"/>): one page, no files, for any
/// phone or tablet browser (made for the iPhone, which can't install the app). The same
/// controls as the app, drawn on a canvas, sending the app's INPUT packets over a WebSocket.
/// Add it to the home screen for a full-screen controller.
/// </summary>
public static class WebPage
{
    public const string Manifest = """
{"name":"GameNight controller","short_name":"GameNight","display":"fullscreen","orientation":"landscape","background_color":"#0d0b2a","theme_color":"#0d0b2a","start_url":"/"}
""";

    public const string Html = """
<!doctype html>
<html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no,viewport-fit=cover">
<meta name="apple-mobile-web-app-capable" content="yes">
<meta name="mobile-web-app-capable" content="yes">
<meta name="apple-mobile-web-app-status-bar-style" content="black-translucent">
<meta name="apple-mobile-web-app-title" content="GameNight">
<meta name="theme-color" content="#0d0b2a">
<link rel="manifest" href="/manifest.json">
<title>GameNight controller</title>
<style>
html,body{margin:0;height:100%;background:#0d0b2a;overflow:hidden;overscroll-behavior:none;touch-action:none;-webkit-user-select:none;user-select:none;-webkit-touch-callout:none;-webkit-tap-highlight-color:transparent}
canvas{position:fixed;left:0;top:0;width:100%;height:100%}
#safe{position:fixed;inset:0;pointer-events:none;padding:env(safe-area-inset-top) env(safe-area-inset-right) env(safe-area-inset-bottom) env(safe-area-inset-left)}
#rot{display:none;position:fixed;inset:0;color:#f4efe3;background:#0d0b2a;align-items:center;justify-content:center;text-align:center;font:800 24px system-ui,sans-serif;line-height:1.4}
@media (orientation:portrait){#rot{display:flex}}
</style></head>
<body><canvas id="c"></canvas><div id="safe"></div><div id="rot">Turn your phone sideways<br>to use it as the controller</div>
<script>
'use strict';
const cv = document.getElementById('c'), g = cv.getContext('2d'), safeEl = document.getElementById('safe');
const LABELS = [['PASS','THROUGH','KICK','SPRINT'],['TACKLE','SWITCH','','PRESS'],['WHIP','SHORT','FLOAT','SPRINT'],
  ['DRIVE','SHORT','FLOAT','SPRINT'],['DIVE','DIVE','DIVE','QUICK\nSTEP'],['KNEE\nSLIDE','AERO\nPLANE','SIUU','BACK\nFLIP']];
const OFF = [[183,61],[159,149],[77,181],[78,78]], RAD = [35,35,37,54];
const INK = '#f4efe3', ACCENT = '#ffd159', GOLD = '#ffd447', CYAN = '#5ef2ff';
let W = 0, H = 0, S = 1, inset = {l:0, r:0, t:0, b:0};
const now = () => performance.now();

// What the PC said last.
let mode = 0, picked = -1, live = false, statusAt = -1e9, ping = -1, pcName = '';
// The controls.
const nonce = (Math.floor(Math.random() * 0x7ffffffe) + 1) >>> 0;
let moveX = 0, moveY = 0, sprintDown = false, sprintSwipe = 0, sprintStart = null;
const held = [false,false,false], swipe = [false,false,false], holdT = [0,0,0], downAt = [0,0,0], startY = [0,0,0];
let events = [], nextId = 1, tackleId = 0, tackle = 0, backId = 0, clickId = 0, curX = 0, curY = 0, mouseDown = false;
const roles = new Map();
let joyId = null, joyC = {x:0, y:0}, knob = {x:0, y:0};
let padId = null, padLast = null, padStart = null, padAt = 0, padMoved = false, clickTouch = null;
let view = 'wait', pauseRect = null, padRect = null, clickRect = null, backRect = null;

function resize() {
  const dpr = window.devicePixelRatio || 1;
  W = innerWidth; H = innerHeight;
  cv.width = Math.round(W * dpr); cv.height = Math.round(H * dpr);
  g.setTransform(dpr, 0, 0, dpr, 0, 0);
  S = Math.min(W / 860, H / 400);
  const cs = getComputedStyle(safeEl);
  inset = {l: parseFloat(cs.paddingLeft) || 0, r: parseFloat(cs.paddingRight) || 0, t: parseFloat(cs.paddingTop) || 0, b: parseFloat(cs.paddingBottom) || 0};
  joyHome();
}
function joyHome() { joyC = {x: inset.l + Math.max(110 * S, W * 0.12), y: H - Math.max(100 * S, H * 0.26)}; knob = {x:0, y:0}; }
addEventListener('resize', resize);
resize();

// ------------------------------------------------------------------ the link
let ws = null;
function connect() {
  ws = new WebSocket('ws://' + location.host + '/ws');
  ws.binaryType = 'arraybuffer';
  ws.onmessage = e => {
    const d = new DataView(e.data);
    if (d.byteLength < 11 || d.getUint8(0) !== 71 || d.getUint8(1) !== 78 || d.getUint8(2) !== 4) return;
    const rtt = ((now() >>> 0) - d.getUint32(4, true)) >>> 0;
    if (rtt < 2000) ping = ping < 0 ? rtt : ping + (rtt - ping) * 0.1;
    live = d.getUint8(8) !== 0;
    mode = Math.min(5, d.getUint8(9));
    picked = d.getInt8(10);
    pcName = new TextDecoder().decode(new Uint8Array(e.data, 11));
    statusAt = now();
  };
  ws.onclose = () => { ws = null; setTimeout(connect, 500); };
  ws.onerror = () => { try { ws.close(); } catch (_) {} };
}
connect();
const connected = () => now() - statusAt < 1500;

function send() {
  if (!ws || ws.readyState !== 1) return;
  const ev = events.slice(-12);
  const buf = new ArrayBuffer(40 + ev.length * 5), d = new DataView(buf);
  let o = 0;
  for (const b of [71, 78, 3, 1]) d.setUint8(o++, b);
  d.setUint32(o, nonce, true); o += 4;
  d.setUint32(o, now() >>> 0, true); o += 4;
  d.setUint8(o++, view === 'controls' ? 0 : 1);
  d.setInt16(o, Math.round(moveX * 32767), true); o += 2;
  d.setInt16(o, Math.round(moveY * 32767), true); o += 2;
  let f = sprintDown ? 1 : 0;
  for (let i = 0; i < 3; i++) { if (held[i]) f |= 2 << i; if (swipe[i]) f |= 16 << i; }
  if (mouseDown) f |= 128;
  d.setUint8(o++, f);
  for (let i = 0; i < 3; i++) { d.setUint16(o, Math.min(65535, Math.round(holdT[i] * 1000)), true); o += 2; }
  d.setUint8(o++, ev.length);
  for (const e of ev) {
    d.setUint16(o, e.id & 0xffff, true); o += 2;
    d.setUint8(o++, e.btn | (e.up ? 4 : 0) | (e.sw ? 8 : 0));
    d.setUint16(o, Math.min(65535, Math.round(e.hold * 1000)), true); o += 2;
  }
  d.setUint16(o, tackleId & 0xffff, true); o += 2;
  d.setUint8(o++, tackle);
  d.setUint16(o, backId & 0xffff, true); o += 2;
  d.setUint16(o, clickId & 0xffff, true); o += 2;
  d.setFloat32(o, curX, true); o += 4;
  d.setFloat32(o, curY, true); o += 4;
  ws.send(buf);
}

// ------------------------------------------------------------------ the controls
const btnC = i => ({x: W - inset.r - OFF[i][0] * S, y: H - inset.b - OFF[i][1] * S});
const shown = i => !(i === 2 && mode === 1);
const inRect = (r, p) => r && p.x >= r.x && p.y >= r.y && p.x <= r.x + r.w && p.y <= r.y + r.h;

function press(i) {
  if (held[i]) return;
  held[i] = true; holdT[i] = 0; downAt[i] = now();
  events.push({id: nextId++, btn: i, up: 0, sw: 0, hold: 0, t: now()});
}
function release(i) {
  if (!held[i]) return;
  held[i] = false;
  events.push({id: nextId++, btn: i, up: 1, sw: swipe[i] ? 1 : 0, hold: (now() - downAt[i]) / 1000, t: now()});
  holdT[i] = 0; swipe[i] = false;
}
function releaseAll() {
  for (const id of [...roles.keys()]) up(id);
  for (let i = 0; i < 3; i++) release(i);
  moveX = moveY = 0; sprintDown = false; joyId = null; joyHome();
  padId = clickTouch = null; mouseDown = false;
}
function joyMove(p) {
  let dx = p.x - joyC.x, dy = p.y - joyC.y, len = Math.hypot(dx, dy);
  const R = 56 * S;
  if (len > R) { joyC.x += dx / len * (len - R); joyC.y += dy / len * (len - R); dx = dx / len * R; dy = dy / len * R; len = R; }
  knob = {x: dx, y: dy};
  const m = Math.min(1, len / R), mm = m < 0.12 ? 0 : (m - 0.12) / 0.88, n = Math.max(1e-6, len);
  moveX = dx / n * mm; moveY = -dy / n * mm;
}

function down(id, p) {
  if (view === 'controls') {
    if (inRect(pauseRect, p)) { backId++; return; }
    let hit = -1, best = 1e9;
    for (let i = 0; i < 4; i++) {
      const c = btnC(i), dd = Math.hypot(p.x - c.x, p.y - c.y);
      if (shown(i) && dd < (RAD[i] + 8) * S && dd < best) { best = dd; hit = i; }
    }
    if (hit >= 0 && hit < 3) { roles.set(id, hit); startY[hit] = p.y; press(hit); }
    else if (hit === 3) { roles.set(id, 'sprint'); sprintDown = true; sprintStart = p; sprintSwipe = 0; }
    else if (joyId === null && p.x < W * 0.46 && p.y > H * 0.22) { roles.set(id, 'joy'); joyId = id; joyC = {x: p.x, y: p.y}; joyMove(p); }
  } else if (view === 'pad') {
    if (inRect(backRect, p)) backId++;
    else if (inRect(clickRect, p)) { clickTouch = id; mouseDown = true; }
    else if (padId === null && inRect(padRect, p)) { padId = id; padLast = padStart = p; padAt = now(); padMoved = false; }
  }
}
function move(id, p) {
  if (view === 'pad') {
    if (id !== padId) return;
    const dx = p.x - padLast.x, dy = p.y - padLast.y;
    padLast = p;
    if (Math.hypot(p.x - padStart.x, p.y - padStart.y) > 10) padMoved = true;
    const gain = Math.min(2.6, Math.max(0.8, 0.8 + Math.hypot(dx, dy) * 0.06 / S)), w = Math.max(200, padRect.w);
    curX += dx * gain / w; curY += dy * gain / w;
    return;
  }
  const r = roles.get(id);
  if (r === undefined) return;
  if (r === 'joy') joyMove(p);
  else if (r === 'sprint' && mode === 1) {
    const dn = p.y - sprintStart.y, left = sprintStart.x - p.x, k = 28 * S;
    const stage = left > k && left > dn ? 2 : dn > k ? 1 : 0;
    if (stage > sprintSwipe) { sprintSwipe = stage; tackleId++; tackle = stage; }
  } else if (r === 0 || r === 1) swipe[r] = startY[r] - p.y > 26 * S;
}
function up(id) {
  if (id === padId) { if (!padMoved && now() - padAt < 350) clickId++; padId = null; }
  if (id === clickTouch) { clickTouch = null; mouseDown = false; }
  const r = roles.get(id);
  if (r === undefined) return;
  roles.delete(id);
  if (r === 'joy') { joyId = null; moveX = moveY = 0; joyHome(); }
  else if (r === 'sprint') { sprintDown = false; sprintSwipe = 0; }
  else release(r);
}

let wake = null, triedFull = false;
function keepAwake() {
  if (navigator.wakeLock && !wake) navigator.wakeLock.request('screen').then(l => { wake = l; l.onrelease = () => wake = null; }).catch(() => {});
}
document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') keepAwake(); else releaseAll(); });
function touches(e, fn) {
  e.preventDefault();
  for (const t of e.changedTouches) fn(t.identifier, {x: t.clientX, y: t.clientY});
  send();
}
cv.addEventListener('touchstart', e => { keepAwake(); touches(e, down); }, {passive: false});
cv.addEventListener('touchmove', e => touches(e, move), {passive: false});
const end = e => {
  touches(e, (id) => up(id));
  if (!triedFull && document.documentElement.requestFullscreen) { triedFull = true; document.documentElement.requestFullscreen().catch(() => {}); }
};
cv.addEventListener('touchend', end, {passive: false});
cv.addEventListener('touchcancel', end, {passive: false});

// ------------------------------------------------------------------ the loop
let last = now();
function frame() {
  const t = now(), dt = Math.min(0.1, (t - last) / 1000);
  last = t;
  const want = !connected() ? 'wait' : live ? 'controls' : 'pad';
  if (want !== view) { releaseAll(); view = want; }
  for (let i = 0; i < 3; i++) if (held[i]) holdT[i] += dt;
  events = events.filter(e => t - e.t < 500).slice(-12);
  send();
  draw(t / 1000);
  requestAnimationFrame(frame);
}
requestAnimationFrame(frame);

// ------------------------------------------------------------------ drawing
function font(px) { return '800 ' + Math.round(px) + 'px "Barlow Condensed","Arial Narrow",system-ui,sans-serif'; }
function label(x, y, s, px, col) {
  g.font = font(px); g.fillStyle = col; g.textAlign = 'center'; g.textBaseline = 'middle';
  const lines = s.split('\n');
  lines.forEach((l, k) => g.fillText(l, x, y + (k - (lines.length - 1) / 2) * px * 1.05));
}
function circle(x, y, r, fill, stroke, lw) {
  g.beginPath(); g.arc(x, y, r, 0, Math.PI * 2);
  if (fill) { g.fillStyle = fill; g.fill(); }
  if (stroke) { g.strokeStyle = stroke; g.lineWidth = lw || 2; g.stroke(); }
}
function box(r, fill, stroke) {
  g.fillStyle = fill; g.fillRect(r.x, r.y, r.w, r.h);
  if (stroke) { g.strokeStyle = stroke; g.lineWidth = 2; g.strokeRect(r.x + 1, r.y + 1, r.w - 2, r.h - 2); }
}
function draw(sec) {
  g.fillStyle = '#0d0b2a'; g.fillRect(0, 0, W, H);
  const grd = g.createLinearGradient(0, 0, 0, H);
  grd.addColorStop(0, '#0d0b2a'); grd.addColorStop(1, '#1f1a62');
  g.fillStyle = grd; g.fillRect(0, 0, W, H);
  if (view === 'wait') {
    label(W / 2, H * 0.42, 'GAMENIGHT CONTROLLER', 34 * S, INK);
    const dots = '.'.repeat(1 + Math.floor(sec * 3) % 3);
    label(W / 2, H * 0.55, (ws && ws.readyState === 1 ? 'Connecting to the PC' : 'Looking for GameNight on the PC') + dots, 20 * S, CYAN);
    label(W / 2, H * 0.66, 'Keep GameNight open on the PC, and this phone on the same Wi-Fi.', 14 * S, 'rgba(244,239,227,0.6)');
    return;
  }
  // Status, top right.
  g.font = font(15 * S); g.textAlign = 'right'; g.textBaseline = 'middle'; g.fillStyle = 'rgba(244,239,227,0.7)';
  g.fillText((pcName + (ping >= 0 ? '  ·  ' + Math.round(ping) + ' ms' : '')).toUpperCase(), W - inset.r - 30 * S, inset.t + 24 * S);
  g.fillStyle = '#6ff0a8'; g.fillRect(W - inset.r - 22 * S, inset.t + 20 * S, 8 * S, 8 * S);
  if (view === 'pad') return drawPad();
  pauseRect = {x: W / 2 - 50 * S, y: inset.t + 8 * S, w: 100 * S, h: 34 * S};
  box(pauseRect, 'rgba(28,24,70,0.92)', 'rgba(190,200,255,0.3)');
  label(W / 2, pauseRect.y + pauseRect.h / 2, 'PAUSE', 20 * S, GOLD);

  const a = joyId !== null ? 1 : 0.55;
  circle(joyC.x, joyC.y, 64 * S, `rgba(20,26,22,${0.18 * a})`, `rgba(244,239,227,${0.28 * a})`, 2);
  circle(joyC.x + knob.x, joyC.y + knob.y, 28 * S, `rgba(244,239,227,${0.85 * a})`);

  const labels = LABELS[mode], defend = mode === 1, cel = mode === 5;
  for (let i = 0; i < 4; i++) {
    if (!shown(i)) continue;
    const isDown = i < 3 ? held[i] : sprintDown, c = btnC(i), r = RAD[i] * S * (isDown ? 0.92 : 1);
    const size = (i === 3 ? 17 : i === 1 ? 12 : i === 2 ? 14 : 13) * S * 1.25;
    let fill = 'rgba(20,26,22,0.55)';
    if (cel) fill = picked === i ? '#e0a31c' : picked >= 0 ? 'rgba(120,80,10,0.25)' : `rgba(220,153,41,${0.45 + 0.2 * Math.sin(sec * 5.7 + i)})`;
    else if (i === 2 && !defend) fill = 'rgba(200,57,59,0.6)';
    else if (defend && (i === 0 || i === 3)) fill = 'rgba(35,52,94,0.6)';
    if (isDown) fill = 'rgba(244,239,227,0.32)';
    circle(c.x, c.y, r, fill, cel ? 'rgba(255,227,140,0.95)' : i === 2 && !defend ? 'rgba(255,219,209,0.6)' : i === 3 ? 'rgba(244,239,227,0.55)' : 'rgba(244,239,227,0.4)', 2);
    if (i < 2 && swipe[i]) { g.beginPath(); g.arc(c.x, c.y, r - 4, -Math.PI * 0.85, -Math.PI * 0.15); g.strokeStyle = ACCENT; g.lineWidth = 3; g.stroke(); }
    label(c.x, c.y + (defend && i === 3 ? -6 * S : 0), labels[i], size, cel && picked === i ? '#3b2100' : INK);
    if (defend && i === 3) label(c.x, c.y + 13 * S, '▼ TACKLE · ◀ SLIDE', 10 * S, INK);
  }
  if (!defend && !cel && held[2]) {
    const c = btnC(2), p = Math.min(1, holdT[2] / 0.85), r = (RAD[2] + 5) * S;
    g.lineWidth = 5; g.strokeStyle = 'rgba(255,255,255,0.12)'; g.beginPath(); g.arc(c.x, c.y, r, 0, Math.PI * 2); g.stroke();
    g.strokeStyle = ACCENT; g.beginPath(); g.arc(c.x, c.y, r, -Math.PI / 2, -Math.PI / 2 + Math.PI * 2 * p); g.stroke();
  }
  if (cel && picked < 0) label(W - inset.r - 140 * S, H - 240 * S, 'C E L E B R A T E', 15 * S, '#ffe08a');
}
function drawPad() {
  pauseRect = null;
  const top = inset.t + 52 * S, bot = H - inset.b - 18 * S, side = 134 * S, gap = 14 * S;
  padRect = {x: inset.l + 20 * S, y: top, w: W - inset.l - inset.r - 40 * S - side - gap, h: bot - top};
  clickRect = {x: W - inset.r - 20 * S - side, y: bot - 150 * S, w: side, h: 150 * S};
  backRect = {x: clickRect.x, y: top, w: side, h: clickRect.y - gap - top};
  box(padRect, 'rgba(0,0,0,0.28)', 'rgba(190,200,255,0.3)');
  label(padRect.x + padRect.w / 2, padRect.y + padRect.h / 2 - 10 * S, 'TOUCHPAD', 34 * S, 'rgba(244,239,227,0.22)');
  label(padRect.x + padRect.w / 2, padRect.y + padRect.h / 2 + 20 * S, 'SLIDE TO MOVE · TAP TO CLICK', 14 * S, 'rgba(244,239,227,0.3)');
  const on = clickTouch !== null;
  box(clickRect, on ? GOLD : 'rgba(28,24,70,0.92)', '#b37400');
  label(clickRect.x + side / 2, clickRect.y + 70 * S, 'CLICK', 30 * S, on ? '#1a1406' : GOLD);
  label(clickRect.x + side / 2, clickRect.y + 98 * S, 'HOLD TO DRAG', 12 * S, on ? '#1a1406' : 'rgba(244,239,227,0.62)');
  box(backRect, 'rgba(28,24,70,0.92)', 'rgba(190,200,255,0.3)');
  label(backRect.x + side / 2, backRect.y + backRect.h / 2, 'BACK', 28 * S, INK);
}
</script></body></html>
""";
}
