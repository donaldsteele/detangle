#!/usr/bin/env python3
"""Run the WebAssembly SVG text self-test and print what the browser saw.

Issue #1 - diagram labels collapsing to one smudge in the WASM build - used to be
confirmable only by hand: serve the site, open the demo, open devtools, read
SvgTextCapability.Diagnosis. A check nobody runs is a check that stops being true, which
is most of why that defect lived as long as it did. This is the same check as one command:

    python tools/wasm-selftest.py                  # publish, serve, scrape, report
    python tools/wasm-selftest.py --no-build       # reuse the last publish (seconds)
    python tools/wasm-selftest.py --keep-serving   # leave the server up to look yourself

The demo prints the matrix twice: once through the bare platform, which on WebAssembly still
collapses twelve of sixteen cells, and once through the font lookup the reader actually draws
diagrams with, which does not. The second is what is graded, because it is what ships. The
first is left in the output because it is the evidence that the platform defect is still there
and the fix is still doing something.

Exit code 0 means every variant drew advancing glyphs in the shipped configuration. 1 means at
least one collapsed, which is the defect returning. 2 means the demo never reported at all.

It installs nothing: the browser is whichever Chromium is already on the machine (see
tools/wasm-console.mjs), driven over the DevTools Protocol by Node's built-in WebSocket.

One 404 in the console output is expected and is not a fault in the demo: the demo ships no
favicon and links none, and Chromium asks for /favicon.ico on its own regardless.
"""

import argparse
import functools
import http.server
import re
import socket
import subprocess
import sys
import threading
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PUBLISH = ROOT / "artifacts" / "demo"
SITE = PUBLISH / "wwwroot"
SCRAPER = ROOT / "tools" / "wasm-console.mjs"


class Handler(http.server.SimpleHTTPRequestHandler):
    """Serves the published demo with the MIME types WebAssembly insists on.

    A .wasm served as text/plain is the difference between the demo starting and the
    console reporting a streaming-compile failure, which would look exactly like the
    demo failing for the reason under test.
    """

    extensions_map = {
        **http.server.SimpleHTTPRequestHandler.extensions_map,
        ".wasm": "application/wasm",
        ".js": "text/javascript",
        ".mjs": "text/javascript",
        ".json": "application/json",
        ".css": "text/css",
        ".svg": "image/svg+xml",
        ".dat": "application/octet-stream",
        ".blat": "application/octet-stream",
        "": "application/octet-stream",
    }

    def end_headers(self):
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def log_message(self, format, *args):
        pass

    def handle_one_request(self):
        """Swallows the reset that killing the browser mid-response provokes.

        The run ends by killing Chromium, which routinely happens while a response is
        still on the wire, and http.server prints a ConnectionResetError traceback when
        that happens. It is entirely benign and it looks exactly like a crash, so it gets
        eaten here rather than left to alarm whoever reads the output next.
        """
        try:
            super().handle_one_request()
        except ConnectionResetError:
            self.close_connection = True


def publish() -> None:
    command = [
        "dotnet", "publish", "src/Detangle.Browser/Detangle.Browser.csproj",
        "--configuration", "Release", "--output", str(PUBLISH),
    ]

    print("$", " ".join(command), flush=True)
    subprocess.run(command, cwd=ROOT, check=True)


def free_port() -> int:
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        return probe.getsockname()[1]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--no-build", action="store_true", help="serve the existing publish")
    parser.add_argument("--keep-serving", action="store_true", help="leave the server running at the end")
    parser.add_argument("--timeout", type=int, default=180000, help="milliseconds to wait for the table")
    parser.add_argument("--browser", help="path to a Chromium, if the search does not find one")
    arguments = parser.parse_args()

    if not arguments.no_build:
        publish()
    elif not SITE.exists():
        print(f"nothing published at {SITE}; run without --no-build first", file=sys.stderr)
        return 2

    if not SITE.exists():
        print(f"the publish produced no {SITE}", file=sys.stderr)
        return 2

    port = free_port()
    server = http.server.ThreadingHTTPServer(
        ("127.0.0.1", port), functools.partial(Handler, directory=str(SITE)))

    threading.Thread(target=server.serve_forever, daemon=True).start()

    url = f"http://127.0.0.1:{port}/?selftest=1"
    command = ["node", str(SCRAPER), "--url", url, "--timeout", str(arguments.timeout)]

    if arguments.browser:
        command += ["--browser", arguments.browser]

    print(f"serving {SITE} at {url}\n", flush=True)

    scrape = subprocess.run(command, cwd=ROOT, capture_output=True, text=True)

    sys.stdout.write(scrape.stdout)
    sys.stderr.write(scrape.stderr)

    if arguments.keep_serving:
        print(f"\nstill serving {url} - ctrl-c to stop")
        try:
            threading.Event().wait()
        except KeyboardInterrupt:
            print()
    else:
        server.shutdown()

    if scrape.returncode != 0:
        print("\nthe demo never finished its self-test - see the console output above",
              file=sys.stderr)
        return 2

    return verdict(scrape.stdout)


def verdict(output: str) -> int:
    """Reads the tables' own summary lines rather than re-deciding what they meant."""
    counts = [(int(good), int(total)) for good, total
              in re.findall(r"(\d+)/(\d+) variants draw advancing glyphs", output)]

    if not counts:
        print("\nthe self-test table had no summary line", file=sys.stderr)
        return 2

    # The last table is the one drawn through the shipped font lookup. Grading the first
    # would fail a healthy build, since the bare platform is still broken and the whole
    # point of the fix is that the reader no longer goes near it.
    good, total = counts[-1]

    if len(counts) > 1:
        bare_good, bare_total = counts[0]

        print(f"\nbare platform: {bare_good}/{bare_total} advancing "
              f"({bare_total - bare_good} collapsed - the defect, still present)")
        print(f"as shipped:    {good}/{total} advancing")

    if good == total:
        print(f"\nPASS: {good}/{total} variants draw advancing glyphs in the browser.")
        return 0

    print(f"\nFAIL: {total - good} of {total} variants collapse in the browser "
          "in the configuration the reader ships. This is issue #1, returned.", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
