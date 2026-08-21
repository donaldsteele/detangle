// The demo's whole host page: start the .NET runtime, show what it is actually doing
// while the runtime arrives, hand it the canvas. There is no framework here and nothing
// is fetched from anywhere but this origin — which is the claim the demo exists to make.
import { dotnet } from './_framework/dotnet.js';

// Why the progress is counted here rather than taken from the runtime: dotnet.js does
// expose onDownloadResourceProgress, but it increments when a response's *headers*
// arrive, and the runtime starts all eighty downloads at once — so it reaches 100%
// inside one round trip while megabytes are still in flight. The boot config inlined
// into dotnet.js carries only names, hashes and cache hints; there are no byte counts in
// it anywhere. Counting bodies as they land is the only honest signal available.
//
// EXPECTED_BYTES is the uncompressed size of the assets this callback sees for an
// English visitor: every *.wasm except the zh-Hans satellite, plus the single ICU shard
// the runtime picks. Re-measure it over a publish with:
//   find wwwroot/_framework \( -name '*.wasm' -o -name 'icudt_EFIGS*.dat' \) \
//     ! -path '*zh-Hans*' -printf '%s\n' | paste -sd+ | bc
// It is the decompressed figure because that is what is counted below; the ~11 MB quoted
// for this page is the same payload after the server has gzipped it. A stale value is
// harmless — the total is revised upward if the download overruns it, and the bar always
// finishes when the app paints.
const EXPECTED_BYTES = 26448871;

// dotnet.js asserts that a 'dotnetjs' resource resolves to a URL string and throws if it
// is handed a Response; 'manifest' is never requested at all, because the boot config is
// inlined into dotnet.js. Returning undefined for those two means "fetch it normally".
const PASS_THROUGH = { dotnetjs: true, manifest: true };

const panel = document.getElementById('loading');
const bar = document.getElementById('progress-bar');
const phase = document.getElementById('progress-phase');
const percent = document.getElementById('progress-percent');
const detail = document.getElementById('progress-detail');

let requested = 0;
let finished = 0;
let received = 0;
let expected = EXPECTED_BYTES;
let settled = false;

function paint() {
  if (settled) {
    return;
  }

  // The build got bigger than the constant above. Rather than pin the bar at 99% for the
  // rest of the download, move the goalposts and keep it moving.
  if (received > expected * 0.99) {
    expected = received / 0.99;
  }

  const value = Math.min(Math.round((received / expected) * 100), 99);

  bar.style.width = value + '%';
  bar.setAttribute('aria-valuenow', String(value));
  percent.textContent = value + '%';
  detail.textContent = finished + ' of ' + requested + ' files';
}

function settle(text) {
  settled = true;
  bar.style.width = '100%';
  bar.setAttribute('aria-valuenow', '100');
  percent.textContent = '100%';
  phase.textContent = text;
  detail.textContent = finished + ' files, and nothing further';
}

function loadBootResource(type, name, uri, hash) {
  if (PASS_THROUGH[type]) {
    return undefined;
  }

  requested += 1;
  paint();

  // force-cache is parity with the runtime, not an improvement on it: every downloadable
  // entry in the inlined boot config already carries cache: "force-cache", and the
  // runtime's own fetch honours that before falling back to no-cache. It is repeated here
  // because this callback replaces that fetch entirely, and dropping it would quietly
  // turn a second visit into eighty revalidation round trips. Safe for these assets
  // either way: each one carries its content hash in its filename, so a cached copy can
  // never be the wrong one.
  //
  // Integrity is passed through unchanged, which does diverge from the runtime in one
  // respect: it checks config.disableIntegrityCheck first, and this does not. Nothing
  // sets that flag today. If something ever does — surviving a host that re-encodes
  // bodies is the usual reason — the opt-out will not reach here, and the failure looks
  // like the red "runtime did not load" panel with no explanation.
  const init = { cache: 'force-cache' };

  if (hash) {
    init.integrity = hash;
  }

  return fetch(uri, init).then(function (response) {
    if (!response.ok) {
      finished += 1;
      paint();

      return response;
    }

    // The byte count is read from a clone and the original response is handed back
    // untouched. Rebuilding a Response around an ArrayBuffer would work out to the same
    // bytes, but dotnet.js only takes the WebAssembly.compileStreaming path when
    // Content-Type is exactly "application/wasm", and it would mean withholding the 11 MB
    // native module from the runtime until it had been buffered here first. Cloning
    // costs a second reader on the same body and changes nothing the runtime sees.
    response.clone().arrayBuffer().then(function (buffer) {
      received += buffer.byteLength;
      finished += 1;
      paint();
    }, function () {
      // A body that cannot be read twice is a counting problem, not a loading one — the
      // runtime still has its own copy. Keep the file counter honest and move on.
      finished += 1;
      paint();
    });

    return response;
  });
}

let app;

try {
  app = await dotnet.withResourceLoader(loadBootResource).create();
} catch (error) {
  panel.classList.add('failed');
  phase.textContent = 'The runtime did not load';
  percent.textContent = '';
  detail.textContent = 'Reload the page, or use the desktop build.';

  throw error;
}

const config = app.getConfig();

settle('Starting Detangle');

// "?page=wiki/schema" opens the demo on that page, so the website can link straight to
// the diagram it is talking about. The value is handed over as a command-line argument
// rather than through interop: the runtime already has a way to pass one.
const query = new URLSearchParams(location.search);
const page = query.get('page');

// "?selftest=1" additionally prints the SVG text variant matrix to the console — the
// sixteen rows behind issue #1. It is a diagnostic for one platform-specific defect, not
// something a visitor wants to read, so it stays off unless it is asked for.
// tools/wasm-selftest.py drives this headlessly and reads the verdict out.
const args = [];

if (page) {
  args.push(page);
}

if (query.get('selftest') === '1') {
  args.push('--selftest');
}

// Main never returns — it awaits StartBrowserAppAsync — so runMain cannot be what
// dismisses the panel. The panel waits for the canvas Avalonia creates instead, which is
// the first moment there is something behind it worth looking at. Removing it before
// that, as this file used to, left a blank rectangle for the whole of Avalonia's startup.
const out = document.getElementById('out');

function dismiss() {
  panel.classList.add('done');
  setTimeout(function () {
    panel.remove();
  }, 300);
}

if (out.querySelector('canvas')) {
  dismiss();
} else {
  const watcher = new MutationObserver(function () {
    if (out.querySelector('canvas')) {
      watcher.disconnect();

      // Two frames: one for the canvas to be laid out, one for it to have drawn.
      requestAnimationFrame(function () {
        requestAnimationFrame(dismiss);
      });
    }
  });

  watcher.observe(out, { childList: true, subtree: true });

  // A backstop, so a canvas that never turns up cannot leave the panel sitting over a
  // working app for ever.
  setTimeout(function () {
    watcher.disconnect();
    dismiss();
  }, 15000);
}

await app.runMain(config.mainAssemblyName, args);
