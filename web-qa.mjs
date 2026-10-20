// GPU-free smoke test of the WebGL build: headless Chromium with SwiftShader (CPU WebGL),
// driven over the DevTools protocol with Node's built-in WebSocket. No npm packages needed.
// Usage: [MODE=play|autoplay|mobile] node web-qa.mjs "http://localhost:PORT/neon-hunger/" <screenshot-dir>
import { spawn } from "node:child_process";
import { mkdtempSync, writeFileSync, mkdirSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

const [url, outDir] = process.argv.slice(2);
if (!url || !outDir) { console.error("usage: node web-qa.mjs <url> <dir>"); process.exit(1); }
mkdirSync(outDir, { recursive: true });
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const port = 9300 + Math.floor(Math.random() * 500);
const profile = mkdtempSync(join(tmpdir(), "nh-chrome-"));

const env = { ...process.env };
delete env.WAYLAND_DISPLAY; delete env.DISPLAY;
// keep every graphics library away from the NVIDIA driver
env.__EGL_VENDOR_LIBRARY_FILENAMES = "/usr/share/glvnd/egl_vendor.d/50_mesa.json";
env.__GLX_VENDOR_LIBRARY_NAME = "mesa";
if (process.env.LVP_ICD) env.VK_DRIVER_FILES = env.VK_ICD_FILENAMES = process.env.LVP_ICD;

const chrome = spawn(process.env.CHROME || "chromium", [
  "--headless=new", `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`,
  "--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist",
  "--disable-vulkan", "--mute-audio", "--autoplay-policy=no-user-gesture-required",
  "--window-size=1280,720", "--no-first-run", "--no-default-browser-check", "about:blank",
], { env, stdio: ["ignore", "ignore", "pipe"] });
let chromeErr = "";
chrome.stderr.on("data", (d) => { chromeErr += d; });

let ws, nextId = 1;
const pending = new Map();
const logs = [];
function send(method, params = {}, sessionId) {
  const id = nextId++;
  ws.send(JSON.stringify({ id, method, params, sessionId }));
  return new Promise((res, rej) => pending.set(id, { res, rej }));
}

async function main() {
  let ver;
  for (let i = 0; i < 50 && !ver; i++) {
    try { ver = await (await fetch(`http://127.0.0.1:${port}/json/version`)).json(); } catch { await sleep(200); }
  }
  if (!ver) throw new Error("chromium did not start: " + chromeErr.slice(0, 400));
  ws = new WebSocket(ver.webSocketDebuggerUrl);
  await new Promise((r) => ws.addEventListener("open", r));
  ws.addEventListener("message", (ev) => {
    const m = JSON.parse(ev.data);
    if (m.id && pending.has(m.id)) {
      const p = pending.get(m.id); pending.delete(m.id);
      m.error ? p.rej(new Error(m.error.message)) : p.res(m.result);
    } else if (m.method === "Runtime.consoleAPICalled") {
      logs.push(`[${m.params.type}] ` + m.params.args.map((a) => a.value ?? a.description ?? "").join(" "));
    } else if (m.method === "Runtime.exceptionThrown") {
      logs.push("[exception] " + (m.params.exceptionDetails.exception?.description || m.params.exceptionDetails.text));
    }
  });
  const { targetId } = await send("Target.createTarget", { url: "about:blank" });
  const { sessionId } = await send("Target.attachToTarget", { targetId, flatten: true });
  const s = (m, p) => send(m, p, sessionId);
  await s("Runtime.enable"); await s("Page.enable");
  await s("Emulation.setDeviceMetricsOverride", { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false });
  const evalJs = async (expr) => (await s("Runtime.evaluate", { expression: expr, returnByValue: true, awaitPromise: true })).result.value;
  const shot = async (name) => {
    const { data } = await s("Page.captureScreenshot", { format: "png" });
    writeFileSync(join(outDir, name + ".png"), Buffer.from(data, "base64"));
    console.log("shot", name);
  };
  const key = async (type, code, keyName, vk, text) =>
    s("Input.dispatchKeyEvent", { type, code, key: keyName, windowsVirtualKeyCode: vk, nativeVirtualKeyCode: vk, text });

  const mode = process.env.MODE || "play";
  const t0 = Date.now();
  const cmd = (c) => evalJs(`window.unityInstance.SendMessage("QA", "WebCommand", "${c}")`);
  const lastLog = (re) => { const m = logs.filter((l) => re.test(l)); return m.length ? m[m.length - 1] : null; };
  const state = async () => { await cmd("state"); await sleep(600); return lastLog(/NH_STATE/) || ""; };
  const pos = (st) => { const m = st.match(/pos=([\d.]+),([\d.]+)/); return m ? [Number(m[1]), Number(m[2])] : [NaN, NaN]; };
  const waitUnity = async () => {
    let ready = false;
    for (let i = 0; i < 240 && !ready; i++) { ready = await evalJs("!!window.unityInstance"); if (!ready) await sleep(1000); }
    console.log("unity ready:", ready, "after", Math.round((Date.now() - t0) / 1000), "s");
    if (!ready) { await shot("99_stuck"); throw new Error("Unity never started"); }
  };
  const keys = { Enter: [13, "Enter"], ArrowLeft: [37, "ArrowLeft"], ArrowUp: [38, "ArrowUp"], ArrowRight: [39, "ArrowRight"], ArrowDown: [40, "ArrowDown"], Space: [32, " "] };
  const keyDown = (k) => s("Input.dispatchKeyEvent", { type: "keyDown", code: k, key: keys[k][1], windowsVirtualKeyCode: keys[k][0], nativeVirtualKeyCode: keys[k][0] });
  const keyUp = (k) => s("Input.dispatchKeyEvent", { type: "keyUp", code: k, key: keys[k][1], windowsVirtualKeyCode: keys[k][0], nativeVirtualKeyCode: keys[k][0] });
  const press = async (k, ms = 120) => { await keyDown(k); await sleep(ms); await keyUp(k); };
  const check = (name, ok, detail = "") => console.log(`NH_WEB ${name} ${ok ? "PASS" : "FAIL"} ${detail}`);
  const swipe = async (x, y, dx, dy) => {
    await s("Input.dispatchTouchEvent", { type: "touchStart", touchPoints: [{ x, y }] });
    // a realistic finger: about half a second, so it spans several frames even at software-rendering speed
    for (let i = 1; i <= 8; i++) { await sleep(70); await s("Input.dispatchTouchEvent", { type: "touchMove", touchPoints: [{ x: x + dx * i / 8, y: y + dy * i / 8 }] }); }
    await sleep(60);
    await s("Input.dispatchTouchEvent", { type: "touchEnd", touchPoints: [] });
  };

  if (mode === "autoplay") {
    await s("Page.navigate", { url: url + (url.includes("?") ? "&" : "?") + "autoplay" });
    await waitUnity();
    const limit = Number(process.env.LIMIT_S || 900);
    for (let i = 0; ; i++) {
      await sleep(20000);
      await shot(`a_run_${String(i).padStart(2, "0")}`);
      if (logs.some((l) => /NH_RUN/.test(l)) || (Date.now() - t0) / 1000 > limit) break;
    }
    logs.filter((l) => /NH_/.test(l)).forEach((l) => console.log(l.slice(0, 500)));
    return;
  }

  if (mode === "mobile") {
    await s("Emulation.setDeviceMetricsOverride", { width: 844, height: 390, deviceScaleFactor: 2, mobile: true });
    await s("Emulation.setTouchEmulationEnabled", { enabled: true, maxTouchPoints: 5 });
    await s("Emulation.setUserAgentOverride", { userAgent: "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Mobile Safari/537.36" });
  }
  await s("Page.navigate", { url });
  await sleep(1500);
  await shot(mode === "mobile" ? "m00_loading" : "00_loading");
  await waitUnity();
  await sleep(6000);
  await shot(mode === "mobile" ? "m01_title" : "01_title");
  if (mode === "mobile") {
    await cmd("play");
    await sleep(2500);
    let st = await state();
    const [x0, y0] = pos(st);
    // swipe left across the middle of the screen: the creature heads left
    await swipe(500, 200, -160, 0);
    await sleep(1500);
    st = await state();
    const [x1] = pos(st);
    check("touch_swipe_steers", x1 < x0 - 0.5, `x ${x0} -> ${x1} ${st}`);
    await shot("m02_playing");
    return;
  }
  // desktop: the title has keyboard focus on PLAY; Enter starts a run
  await s("Input.dispatchMouseEvent", { type: "mousePressed", x: 640, y: 700, button: "left", clickCount: 1 });
  await s("Input.dispatchMouseEvent", { type: "mouseReleased", x: 640, y: 700, button: "left", clickCount: 1 });
  await sleep(500);
  await press("Enter");   // re-selects PLAY after the click cleared it
  await sleep(400);
  await press("Enter");
  await sleep(6000);
  let st = await state();
  check("enter_starts_game", /phase=(Intro|Playing)/.test(st), st);
  const [x0] = pos(st);
  await keyDown("ArrowLeft"); await sleep(1500); await keyUp("ArrowLeft");
  st = await state();
  const [x1] = pos(st);
  check("arrow_keys_steer", x1 < x0 - 0.5, `x ${x0} -> ${x1}`);
  await shot("02_playing");
  const audio = await evalJs("(() => { try { const ctx = window.unityInstance && window.unityInstance.Module && window.unityInstance.Module.WEBAudio && window.unityInstance.Module.WEBAudio.audioContext; return ctx ? ctx.state : 'unknown'; } catch (e) { return 'error ' + e; } })()");
  check("audio_context_after_input", audio === "running" || audio === "unknown", `state=${audio}`);
  const fps = await evalJs(`new Promise(r => { let n = 0; const t = performance.now(); (function f() { n++; if (performance.now() - t < 3000) requestAnimationFrame(f); else r(Math.round(n / 3)); })(); })`).catch(() => null);
  console.log("page rAF fps (software rendering):", fps);
}

main()
  .then(() => { console.log("RESULT ok"); })
  .catch((e) => { console.log("RESULT fail:", e.message); process.exitCode = 1; })
  .finally(() => {
    const bad = logs.filter((l) => /exception|error/i.test(l));
    console.log(`console lines: ${logs.length}, errors: ${bad.length}`);
    bad.slice(0, 15).forEach((l) => console.log("  ", l.slice(0, 300)));
    writeFileSync(join(outDir, "console.log"), logs.join("\n"));
    chrome.kill("SIGTERM");
  });
