#!/usr/bin/env python3
"""Turn the GitHub releases into the changelog page's cards.

The page has always carried a comment saying the site workflow replaced its placeholder
once a version tag existed. Nothing did, so the changelog went on saying "Nothing is
tagged yet" through two releases — which is the most embarrassing kind of stale, because
it is the page whose entire job is being current.

    gh release list --json tagName,name,publishedAt,body --limit 25 > releases.json
    python tools/changelog.py releases.json public/changelog.html

Writing the cards at build time rather than fetching them in the browser is the same
decision the version badge already documents: the site's CSP forbids connect-src, and a
changelog that phoned home would be the one thing on it that did.
"""

import html
import json
import re
import sys
from pathlib import Path

PLACEHOLDER = re.compile(
    r'<div id="releases">.*?</div>\s*</div>\s*</section>',
    re.DOTALL)

# The release notes are written by .github/workflows/release.yml, which emits "### New"
# style headings and "- " bullets and nothing else. Anything richer is left as prose
# rather than half-rendered.
HEADING = re.compile(r"^#{1,6}\s+(.*)$")
BULLET = re.compile(r"^[-*]\s+(.*)$")
CODE = re.compile(r"`([^`]+)`")


def inline(text):
    """Escapes a line, then puts back the one bit of markup the notes use."""
    return CODE.sub(r"<code>\1</code>", html.escape(text.strip()))


def body_html(body):
    lines = []
    in_list = False

    for raw in (body or "").splitlines():
        line = raw.rstrip()

        if not line:
            continue

        heading = HEADING.match(line)
        bullet = BULLET.match(line)

        if bullet:
            if not in_list:
                lines.append("          <ul>")
                in_list = True

            lines.append(f"            <li>{inline(bullet.group(1))}</li>")
            continue

        if in_list:
            lines.append("          </ul>")
            in_list = False

        if heading:
            lines.append(f"          <h4>{inline(heading.group(1))}</h4>")
        elif not line.startswith("**Full changelog**"):
            lines.append(f"          <p>{inline(line)}</p>")

    if in_list:
        lines.append("          </ul>")

    return "\n".join(lines)


def card(release):
    tag = release.get("tagName", "")
    name = release.get("name") or tag
    published = (release.get("publishedAt") or "")[:10]
    url = f"https://github.com/donaldsteele/detangle/releases/tag/{tag}"

    return f"""      <div class="card">
        <h3>{html.escape(name)}</h3>
        <p class="release-date"><time datetime="{html.escape(published)}">{html.escape(published)}</time> · <a href="{html.escape(url)}">assets and checksums</a></p>
{body_html(release.get('body'))}
      </div>"""


def main(source, target):
    releases = json.loads(Path(source).read_text(encoding="utf-8"))

    if not releases:
        print("no releases; leaving the placeholder in place")
        return 0

    cards = "\n".join(card(release) for release in releases)
    page = Path(target).read_text(encoding="utf-8")

    replacement = f"""<div id="releases">
{cards}
    </div>
  </div>
</section>"""

    if not PLACEHOLDER.search(page):
        print("::error::the changelog's releases block was not found", file=sys.stderr)
        return 1

    Path(target).write_text(PLACEHOLDER.sub(replacement, page, count=1), encoding="utf-8")
    print(f"wrote {len(releases)} release cards")

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2]))
