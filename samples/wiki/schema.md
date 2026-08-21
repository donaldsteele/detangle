---
title: Vault Schema
type: reference
tags: [demo, data]
updated: 2026-08-20
---

# Vault Schema

A DBML fence, parsed here and drawn as a Mermaid ER diagram. No dbdocs account, no
network call, no Node.

```dbml
Project detangle_demo {
  database_type: 'SQLite'
  Note: 'The sidecar index Detangle keeps beside a vault.'
}

Table document {
  id integer [pk, increment]
  relative_path varchar [not null, unique, note: 'Vault-relative, "/" separators']
  stem varchar [not null]
  title varchar
  flavor varchar [default: 'generic']
  modified_at timestamp [not null]

  Note: 'One row per file found by the scanner.'

  Indexes {
    (relative_path) [unique]
    stem
  }
}

Table link {
  id integer [pk, increment]
  source_id integer [not null]
  target_id integer
  raw_target varchar [not null]
  rule varchar [not null, note: 'Which of the thirteen steps resolved it']
  line integer [not null]
}

Table heading {
  id integer [pk, increment]
  document_id integer [not null]
  slug varchar [not null]
  text varchar [not null]
  level integer [not null]
}

Ref: link.source_id > document.id
Ref: link.target_id > document.id
Ref: heading.document_id > document.id
```

The panel under the diagram carries what an `erDiagram` cannot say — defaults, notes and
column settings — because the Mermaid form is lossy and pretending otherwise would hide
half the schema.

Back to [[index]].
