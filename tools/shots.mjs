#!/usr/bin/env node
// Drives the WebAssembly demo in a headless Chromium and captures the screenshots the
// website uses.
//
// The site's screenshots have to be real. A mock-up of the Link Doctor would be a picture
// of a claim rather than evidence for it, and this product's whole argument is that it
// tells you things other readers cannot — which is only worth saying if the picture is
// the actual program saying them. So this drives the real build, over the real sample
// vault, and photographs the result.
//
//     python tools/serve-site.py            # in another terminal, builds and serves :8000
//     node tools/shots.mjs --script tools/shots/doctor.json
//     node tools/shots.mjs --script ... --out site/images
//
// A script is a JSON array of steps, run in order:
//
//     {"do":"wait",  "ms": 1200}                     pause
//     {"do":"click", "x": 24, "y": 300}              left click at viewport coordinates
//     {"do":"move",  "x": 24, "y": 300}              hover, for tooltips and previews
//     {"do":"key",   "key":"Enter"}                  a key press
//     {"do":"type",  "text":"doctor"}                text into whatever has focus
//     {"do":"shot",  "name":"doctor"}                writes <out>/doctor.png
//     {"do":"shot",  "name":"x", "clip":[0,0,400,300]}   a region rather than the page
//
// Coordinates are unavoidable: the reader is one Skia canvas, so there is no DOM to query
// for a button. That is why the viewport is pinned — the same script gives the same
// picture on any machine, and a layout change shows up as a wrong screenshot rather than
// as a silent miss.
//
// Chromium comes from whichever of Edge or Chrome is installed, and the DevTools Protocol
// is spoken over Node's built-in WebSocket, so there is nothing to install.

import { spawn } from 'node:child_process';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';

const CANDIDATES = [
  process.env.DETANGLE_BROWSER,
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
  'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
  'C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe',
  process.env.LOCALAPPDATA && join(process.env.LOCALAPPDATA, 'Google\\Chrome\\Application\\chrome.exe'),
  process.env.LOCALAPPDATA && join(process.env.LOCALAPPDATA, 'Microsoft\\Edge\\Application\\msedge.exe'),
  '/usr/bin/google-chrome',
  '/usr/bin/chromium',
  '/usr/bin/microsoft-edge',
  '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
  '/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge',
].filter(Boolean);

function options(argv) {
  const result = {
    url: 'http://localhost:8000/demo/',
    script: null,
    out: 'site/images',
    width: 1256,
    height: 808,
    scale: 2,
    ready: 'detangle: svg text',
    timeout: 180000,
  };

  for (let i = 0; i < argv.length; i++) {
    const value = argv[i + 1];

    switch (argv[i]) {
      case '--url': result.url = value; i++; break;
      case '--script': result.script = value; i++; break;
      case '--out': result.out = value; i++; break;
      case '--width': result.width = Number(value); i++; break;
      case '--height': result.height = Number(value); i++; break;
      case '--scale': result.scale = Number(value); i++; break;
      case '--ready': result.ready = value; i++; break;
      case '--timeout': result.timeout = Number(value); i++; break;
      default: throw new Error(`unknown option ${argv[i]}`);
    }
  }

  if (!result.script) {
    throw new Error('--script is required');
  }

  return result;
}

function browser() {
  const found = CANDIDATES.find(path => existsSync(path));

  if (!found) {
    throw new Error('no Chromium found; set DETANGLE_BROWSER to one');
  }

  return found;
}

async function endpoint(port, deadline) {
  while (Date.now() < deadline) {
    try {
      const response = await fetch(`http://127.0.0.1:${port}/json/version`);
      const body = await response.json();

      if (body.webSocketDebuggerUrl) {
        return body.webSocketDebuggerUrl;
      }
    } catch {
      // The browser has not opened the port yet.
    }

    await new Promise(resolve => setTimeout(resolve, 150));
  }

  throw new Error('the browser never opened its debugging port');
}

// The browser endpoint speaks only the Target and Browser domains; Page, Runtime and
// Input all live on a page's own session, which is what sessionId selects.
class Session {
  constructor(socket, sessionId = null) {
    this.socket = socket;
    this.sessionId = sessionId;
    this.next = 1;
    this.pending = new Map();
    this.console = [];

    socket.addEventListener('message', event => {
      const message = JSON.parse(event.data);

      if (message.id && this.pending.has(message.id)) {
        const { resolve, reject } = this.pending.get(message.id);
        this.pending.delete(message.id);
        message.error ? reject(new Error(message.error.message)) : resolve(message.result);
        return;
      }

      if (message.method === 'Runtime.consoleAPICalled') {
        this.console.push((message.params.args ?? []).map(a => a.value ?? a.description ?? '').join(' '));
      }
    });
  }

  send(method, params = {}) {
    const id = this.next++;
    const envelope = { id, method, params };

    if (this.sessionId) {
      envelope.sessionId = this.sessionId;
    }

    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.socket.send(JSON.stringify(envelope));
    });
  }

  saw(text) {
    return this.console.some(line => line.includes(text));
  }
}

async function run() {
  const config = options(process.argv.slice(2));
  const steps = JSON.parse(readFileSync(config.script, 'utf8'));
  const profile = mkdtempSync(join(tmpdir(), 'detangle-shots-'));
  const port = 9400 + Math.floor(process.pid % 400);

  const child = spawn(browser(), [
    '--headless=new',
    `--remote-debugging-port=${port}`,
    `--user-data-dir=${profile}`,
    `--window-size=${config.width},${config.height}`,
    `--force-device-scale-factor=${config.scale}`,
    '--hide-scrollbars',
    '--no-first-run',
    '--no-default-browser-check',
    '--disable-extensions',
    '--disable-gpu',
    'about:blank',
  ], { stdio: 'ignore' });

  const stop = () => {
    child.kill();
    try { rmSync(profile, { recursive: true, force: true }); } catch { /* the OS will */ }
  };

  try {
    const deadline = Date.now() + config.timeout;
    const socket = new WebSocket(await endpoint(port, deadline));

    await new Promise((resolve, reject) => {
      socket.addEventListener('open', resolve);
      socket.addEventListener('error', () => reject(new Error('could not attach to the browser')));
    });

    const browserSession = new Session(socket);

    const { targetId } = await browserSession.send('Target.createTarget', { url: 'about:blank' });
    const { sessionId } = await browserSession.send('Target.attachToTarget', { targetId, flatten: true });

    const session = new Session(socket, sessionId);

    // The console lines arrive on the page session but are dispatched to whichever
    // listener sees them first, so both share the socket's message handler.
    session.console = browserSession.console;

    await session.send('Runtime.enable');
    await session.send('Page.enable');
    await session.send('Emulation.setDeviceMetricsOverride', {
      width: config.width,
      height: config.height,
      deviceScaleFactor: config.scale,
      mobile: false,
    });

    await session.send('Page.navigate', { url: config.url });

    // Waiting for a line the application prints, rather than for a fixed delay, is what
    // makes this deterministic: a slow machine takes longer and still gets the picture,
    // and a broken build fails at the timeout instead of photographing a blank canvas.
    // An empty sentinel means the page prints nothing worth waiting for - the website
    // itself, rather than the demo - so the script's own waits are the only timing.
    while (config.ready !== '' && !session.saw(config.ready)) {
      if (Date.now() > deadline) {
        throw new Error(`the demo never printed "${config.ready}"`);
      }

      await new Promise(resolve => setTimeout(resolve, 250));
    }

    mkdirSync(config.out, { recursive: true });

    for (const step of steps) {
      await perform(session, step, config);
    }

    console.log('detangle: shots complete');
  } finally {
    stop();
  }
}

async function perform(session, step, config) {
  const pause = ms => new Promise(resolve => setTimeout(resolve, ms));

  switch (step.do) {
    case 'wait':
      await pause(step.ms ?? 500);
      break;

    case 'move':
      await session.send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: step.x, y: step.y });
      await pause(step.settle ?? 400);
      break;

    case 'click':
      await session.send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: step.x, y: step.y });
      await pause(60);
      await session.send('Input.dispatchMouseEvent', {
        type: 'mousePressed', x: step.x, y: step.y, button: 'left', clickCount: 1,
      });
      await pause(40);
      await session.send('Input.dispatchMouseEvent', {
        type: 'mouseReleased', x: step.x, y: step.y, button: 'left', clickCount: 1,
      });
      await pause(step.settle ?? 500);
      break;

    case 'key':
      await session.send('Input.dispatchKeyEvent', {
        type: 'rawKeyDown', key: step.key, code: step.code ?? step.key,
        windowsVirtualKeyCode: step.vk ?? 0, modifiers: step.modifiers ?? 0,
      });
      await pause(40);
      await session.send('Input.dispatchKeyEvent', {
        type: 'keyUp', key: step.key, code: step.code ?? step.key,
        windowsVirtualKeyCode: step.vk ?? 0, modifiers: step.modifiers ?? 0,
      });
      await pause(step.settle ?? 350);
      break;

    case 'type':
      for (const character of step.text) {
        await session.send('Input.dispatchKeyEvent', { type: 'char', text: character });
        await pause(30);
      }
      await pause(step.settle ?? 400);
      break;

    case 'shot': {
      const clip = step.clip
        ? { x: step.clip[0], y: step.clip[1], width: step.clip[2], height: step.clip[3], scale: config.scale }
        : undefined;

      const { data } = await session.send('Page.captureScreenshot', {
        format: 'png', captureBeyondViewport: false, ...(clip ? { clip } : {}),
      });

      const path = join(config.out, `${step.name}.png`);

      mkdirSync(dirname(path), { recursive: true });
      writeFileSync(path, Buffer.from(data, 'base64'));

      console.log(`detangle: wrote ${path}`);
      break;
    }

    default:
      throw new Error(`unknown step ${step.do}`);
  }
}

run().catch(error => {
  console.error(`detangle: ${error.message}`);
  process.exit(1);
});
