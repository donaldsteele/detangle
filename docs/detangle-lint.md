---
title: detangle-lint
type: guide
updated: 2026-08-22
---

# detangle-lint

The Link Doctor, pointed at a terminal. Same scan, same resolution chain, same findings —
printed as JSON so the thing that wrote the wiki can read the report on the wiki it wrote.

## Where it comes from

It ships inside the portable archive (`detangle-<version>-<rid>.zip` or `.tar.gz`) rather
than in the installers. The installers are for somebody who wants to read a wiki; this is
for a pipeline. Unpack the archive and `detangle-lint` is beside `detangle`.

```
$ detangle-lint samples/
$ detangle-lint . --fail-on warning --compact
$ detangle-lint ./wiki --output findings.json
```

## Options

| Option | Means |
|---|---|
| `--fail-on <severity>` | Exit 1 when any finding is this severe or worse: `error` (the default), `warning`, `info`, or `never` |
| `--output`, `-o <path>` | Write the report to a file instead of standard output |
| `--compact` | One line of JSON rather than an indented document |
| `--help`, `-h` | The usage text |

Exit codes are `0` for a clean run, `1` for findings at or past the threshold, and `2`
when the vault could not be read at all — a missing folder, a permission error, or a
report that could not be written. A `2` is never a finding about your wiki; it means the
tool did not run.

## Why `--fail-on` exists

Only a broken link is an `Error`. Everything else — ambiguity, a fragment that matches no
heading, a link that resolved by a late rule — is a `Warning` or `Info`, because none of
them stop a reader getting to the page.

That makes the default gate almost useless on its own: `--fail-on error` means "fail on
broken links", which is what every link checker written in the last twenty years already
does. The interesting findings are the advisory ones, and `--fail-on warning` is what a
pipeline that cares about them should use.

## The part worth reading

```json
{
  "counts": {
    "error": 4, "warning": 6, "info": 17,
    "byKind": {
      "brokenLink": 4, "ambiguousLink": 2, "duplicateSlug": 4,
      "nonCanonicalLink": 17, "brokenAnchor": 1, "anchorDialectDrift": 1 } },
  "rules": {
    "wiki": {
      "noteRelativePath": 1, "pathSuffix": 1, "caseInsensitiveStem": 1,
      "normalizedName": 3, "alias": 2 } }
}
```

The `findings` array is the ordinary part: kind, severity, path, line, message, and the
rewrite or heading the tool would suggest. Every finding is suggested here, unlike in the
application, where the edit-distance search is deferred until a reader opens one — a report
has no reader who will open one later, and one that says a link is broken without saying
what it probably meant leaves its consumer to do the search again.

`rules` is the part no other tool can print. It is a histogram of which step of the
resolution chain each folder's links needed. In the block above, eight links written inside
`wiki/` resolved, and exactly one of them did so by a rule that is not a rescue. A folder
whose links all needed step 6 or later is a folder whose naming does not match the vault it
was written into — and that is a fact a generator can act on, unlike "seven links are
broken", which only says the generator already failed.

## Using it in CI

```yaml
- name: Check the wiki's links
  run: ./detangle-lint wiki/ --fail-on warning --output findings.json

- name: Keep the report
  if: always()
  uses: actions/upload-artifact@v4
  with:
    name: link-findings
    path: findings.json
```

The report identifies itself: `schema` is its version, `vault` the folder it read, and
`flavor` the wiki convention that was detected. A consumer should check `schema` before
trusting the shape of anything else.

## What it does not do

It never writes to the vault. There is no `--fix`, and there will not be one: applying a
rewrite is a decision that wants the before-and-after line in front of a person, which is
what the [Link Doctor](link-doctor.md) panel is for. The CLI reports; the application acts.
