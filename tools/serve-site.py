#!/usr/bin/env python3
"""Build the website locally and serve it.

The site cannot be opened from a file:// URL. main.js is an ES module, the docs
search fetches its index, and the demo loads WebAssembly - browsers refuse all
three from the filesystem. So this assembles the same tree .github/workflows/site.yml
deploys and serves it over HTTP.

    python tools/serve-site.py              # build everything, serve on :8000
    python tools/serve-site.py --no-demo    # skip the WASM build (much faster)
    python tools/serve-site.py --port 9000

The demo needs the wasm-tools workload:  dotnet workload install wasm-tools
"""

import argparse
import http.server
import shutil
import socketserver
import subprocess
import sys
import webbrowser
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PUBLIC = ROOT / "artifacts" / "site"


def run(*command: str) -> None:
    print("$", " ".join(command))
    subprocess.run(command, cwd=ROOT, check=True)


def build(with_demo: bool) -> None:
    if PUBLIC.exists():
        shutil.rmtree(PUBLIC)

    PUBLIC.mkdir(parents=True)
    shutil.copytree(ROOT / "site", PUBLIC, dirs_exist_ok=True)

    # The docs are published by Detangle's own exporter, exactly as the deploy does it.
    run(
        "dotnet", "run", "--project", "src/Detangle.Desktop/Detangle.Desktop.csproj",
        "--configuration", "Release",
        "--", "--export-site", "docs", str(PUBLIC / "docs"), "--title", "Detangle Docs",
    )

    if not with_demo:
        # Without the demo the iframe is empty; leave a note rather than a blank box.
        (PUBLIC / "demo").mkdir()
        (PUBLIC / "demo" / "index.html").write_text(
            "<!doctype html><meta charset=utf-8>"
            "<body style='font:15px system-ui;padding:2rem;color:#8a92a3;background:#0a0c11'>"
            "Built with --no-demo. Re-run without it to include the WebAssembly demo.",
            encoding="utf-8",
        )
        return

    publish = ROOT / "artifacts" / "demo"
    run(
        "dotnet", "publish", "src/Detangle.Browser/Detangle.Browser.csproj",
        "--configuration", "Release", "--output", str(publish),
    )

    shutil.copytree(publish / "wwwroot", PUBLIC / "demo", dirs_exist_ok=True)


class Handler(http.server.SimpleHTTPRequestHandler):
    """Serves the built site with the MIME types WebAssembly needs.

    Python's table does not always know .wasm, and a wrong type is the difference
    between the demo starting and the console reporting a streaming-compile failure.
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

    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=str(PUBLIC), **kwargs)

    def end_headers(self):
        # Local development only: never cache, so a rebuild is visible on reload.
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def log_message(self, format, *args):
        if "404" in (args[1] if len(args) > 1 else ""):
            super().log_message(format, *args)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=8000)
    parser.add_argument("--no-demo", action="store_true", help="skip the WebAssembly build")
    parser.add_argument("--no-build", action="store_true", help="serve what is already built")
    parser.add_argument("--no-open", action="store_true", help="do not open a browser")
    args = parser.parse_args()

    if not args.no_build:
        build(with_demo=not args.no_demo)
    elif not PUBLIC.exists():
        print("nothing built yet; run without --no-build first", file=sys.stderr)
        return 1

    url = f"http://localhost:{args.port}/"
    print(f"\nserving {PUBLIC}\n  {url}\n  {url}demo/\n  {url}docs/\n\nctrl-c to stop")

    if not args.no_open:
        webbrowser.open(url)

    socketserver.TCPServer.allow_reuse_address = True

    with socketserver.TCPServer(("127.0.0.1", args.port), Handler) as server:
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            print()

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
