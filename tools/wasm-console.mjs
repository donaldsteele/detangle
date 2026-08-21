#!/usr/bin/env node
// Loads a page in a headless Chromium and prints everything it wrote to the console.
//
// This exists to turn one manual check into a command. Confirming the WebAssembly SVG
// text defect used to mean serving the site, opening the demo by hand and reading
// SvgTextCapability.Diagnosis out of the browser's devtools. That is why the defect
// survived so long: nobody re-ran it, and every cheaper automated check answered a
// different question than the one being asked.
//
//     node tools/wasm-console.mjs                                   # demo self-test on :8000
//     node tools/wasm-console.mjs --url http://localhost:8000/demo/ # the plain one-line diagnosis
//     node tools/wasm-console.mjs --until "detangle: svg text" --timeout 120000
//
// It installs nothing. Chromium comes from whichever of Edge or Chrome is on the machine,
// and the DevTools Protocol is spoken over the WebSocket that Node has had built in since
// v22 — so there is no node_modules, no lockfile and nothing to keep up to date.
//
// Exit code 0 means the sentinel line appeared. Anything else means it did not, which is
// itself the finding: a demo that never prints its diagnosis is a demo that never started.

import { spawn } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const DEFAULT_URL = 'http://localhost:8000/demo/?selftest=1';

// Printed by Detangle.Browser's Program.Main once the whole variant matrix is out. Waiting
// for a specific line rather than a fixed delay is what makes the run deterministic: a
// slow machine takes longer and still passes, and a broken build fails immediately at the
// timeout instead of quietly scraping half a table.
const DEFAULT_SENTINEL = 'detangle: selftest complete';

// Every place a Chromium hides on Windows, then the names a POSIX box puts on PATH.
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

function parseArguments(argv) {
  const options = {
    url: DEFAULT_URL,
    until: DEFAULT_SENTINEL,
    timeout: 180000,
    browser: null,
    keepOpen: false,
  };

  for (let i = 0; i < argv.length; i++) {
    const flag = argv[i];
    const value = argv[i + 1];

    if (flag === '--url') { options.url = value; i++; }
    else if (flag === '--until') { options.until = value; i++; }
    else if (flag === '--timeout') { options.timeout = Number(value); i++; }
    else if (flag === '--browser') { options.browser = value; i++; }
    else if (flag === '--no-wait') { options.until = null; }
    else if (flag === '--help' || flag === '-h') { options.help = true; }
    else { throw new Error(`unknown argument: ${flag}`); }
  }

  return options;
}

function findBrowser(explicit) {
  if (explicit) {
    if (!existsSync(explicit)) {
      throw new Error(`no browser at ${explicit}`);
    }

    return explicit;
  }

  const found = CANDIDATES.find((path) => existsSync(path));

  if (!found) {
    throw new Error(
      'no Chromium found. Pass --browser <path> or set DETANGLE_BROWSER. '
      + 'Looked in:\n  ' + CANDIDATES.join('\n  '));
  }

  return found;
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

/**
 * Reads the port Chromium chose. Asking for port 0 and reading it back beats picking a
 * number, which collides with whatever else on the machine had the same idea.
 */
async function waitForPort(profile, deadline) {
  const file = join(profile, 'DevToolsActivePort');

  while (Date.now() < deadline) {
    if (existsSync(file)) {
      const port = readFileSync(file, 'utf8').split('\n')[0].trim();

      if (port) {
        return Number(port);
      }
    }

    await sleep(50);
  }

  throw new Error('the browser never opened a debugging port');
}

/** A minimal DevTools Protocol client: one socket, numbered requests, flat sessions. */
class Devtools {
  constructor(socket) {
    this.socket = socket;
    this.nextId = 1;
    this.pending = new Map();
    this.listeners = [];

    socket.addEventListener('message', (event) => {
      const message = JSON.parse(event.data);

      if (message.id !== undefined) {
        const settle = this.pending.get(message.id);

        this.pending.delete(message.id);

        if (settle) {
          message.error ? settle.reject(new Error(message.error.message)) : settle.resolve(message.result);
        }

        return;
      }

      for (const listener of this.listeners) {
        listener(message);
      }
    });
  }

  static async connect(url) {
    const socket = new WebSocket(url);

    await new Promise((resolve, reject) => {
      socket.addEventListener('open', resolve, { once: true });
      socket.addEventListener('error', () => reject(new Error(`could not connect to ${url}`)), { once: true });
    });

    return new Devtools(socket);
  }

  on(listener) {
    this.listeners.push(listener);
  }

  send(method, params = {}, sessionId) {
    const id = this.nextId++;

    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.socket.send(JSON.stringify(sessionId ? { id, method, params, sessionId } : { id, method, params }));
    });
  }

  close() {
    this.socket.close();
  }
}

/** Turns one consoleAPICalled or exceptionThrown payload into a printable line. */
function renderArguments(args = []) {
  return args
    .map((argument) => {
      if (argument.type === 'string') {
        return argument.value;
      }

      if ('value' in argument) {
        return String(argument.value);
      }

      return argument.description ?? argument.unserializableValue ?? `[${argument.type}]`;
    })
    .join(' ');
}

async function main() {
  const options = parseArguments(process.argv.slice(2));

  if (options.help) {
    console.log(readFileSync(new URL(import.meta.url), 'utf8').split('\n')
      .filter((line) => line.startsWith('//')).map((line) => line.slice(3)).join('\n'));

    return 0;
  }

  const browser = findBrowser(options.browser);
  const profile = mkdtempSync(join(tmpdir(), 'detangle-headless-'));
  const deadline = Date.now() + options.timeout;

  console.error(`# browser: ${browser}`);
  console.error(`# url:     ${options.url}`);

  const child = spawn(browser, [
    '--headless=new',
    '--disable-gpu',
    '--no-first-run',
    '--no-default-browser-check',
    '--disable-extensions',
    // A fresh profile every run, so nothing cached from last time can answer for the
    // build under test — the exact mistake that would make this instrument lie.
    `--user-data-dir=${profile}`,
    '--remote-debugging-port=0',
    'about:blank',
  ], { stdio: ['ignore', 'ignore', 'ignore'] });

  let devtools = null;
  let matched = false;

  try {
    const port = await waitForPort(profile, deadline);
    const version = await (await fetch(`http://127.0.0.1:${port}/json/version`)).json();

    devtools = await Devtools.connect(version.webSocketDebuggerUrl);

    const { targetId } = await devtools.send('Target.createTarget', { url: 'about:blank' });
    const { sessionId } = await devtools.send('Target.attachToTarget', { targetId, flatten: true });

    const done = new Promise((resolve) => {
      devtools.on((message) => {
        if (message.sessionId !== sessionId) {
          return;
        }

        let line = null;

        if (message.method === 'Runtime.consoleAPICalled') {
          line = renderArguments(message.params.args);
        } else if (message.method === 'Runtime.exceptionThrown') {
          const details = message.params.exceptionDetails;

          line = `[exception] ${details.exception?.description ?? details.text}`;
        } else if (message.method === 'Log.entryAdded') {
          line = `[${message.params.entry.source}] ${message.params.entry.text}`;
        }

        if (line === null) {
          return;
        }

        // The .NET runtime hands a multi-line Console.WriteLine over as one call, so the
        // table arrives whole and has to be split back out to stay readable.
        for (const part of line.split('\n')) {
          console.log(part);
        }

        if (options.until && line.includes(options.until)) {
          matched = true;
          resolve();
        }
      });
    });

    await devtools.send('Runtime.enable', {}, sessionId);
    await devtools.send('Log.enable', {}, sessionId);
    await devtools.send('Page.enable', {}, sessionId);
    await devtools.send('Page.navigate', { url: options.url }, sessionId);

    if (options.until) {
      const timeout = sleep(Math.max(0, deadline - Date.now()));

      await Promise.race([done, timeout]);
    } else {
      await sleep(Math.max(0, deadline - Date.now()));
      matched = true;
    }
  } finally {
    devtools?.close();
    child.kill();

    // Chromium takes a moment to let go of its profile on Windows.
    await sleep(300);

    try {
      rmSync(profile, { recursive: true, force: true });
    } catch {
      // A leftover temp profile is not worth failing the run over.
    }
  }

  if (!matched) {
    console.error(`# never saw "${options.until}" within ${options.timeout}ms`);

    return 1;
  }

  return 0;
}

try {
  process.exitCode = await main();
} catch (error) {
  console.error(`# ${error.message}`);
  process.exitCode = 2;
}
