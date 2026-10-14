# `.ai/ideas`

Design notes that are **not implemented** (or only partially implemented). They capture the
research, the intended architecture, file layout, edge cases and open questions so a future
implementation change can start from a reviewed plan instead of a fresh investigation.

Conventions:

- One feature per file, kebab-case (`highroller-table.md`).
- Every doc starts with a short status block: **Status**, **Scope**, **Touched areas**.
- Record the codebase anchors the design relies on (file paths, system names, prototype ids)
  rather than line numbers.
- An idea is not architecture: once it is implemented, fold the durable parts into
  `.ai/systems/*.md` and mark the idea `Implemented` (or delete it).
- Nothing here is a changelog entry; idea docs are internal.

| Idea | Status |
|---|---|
| [`highroller-table.md`](highroller-table.md) — Buckshot Roulette table | Proposed / not implemented |
| [`coyote-bayou-weapons.md`](coyote-bayou-weapons.md) — Coyote Bayou (BYOND) weapon port backlog | Proposed / not implemented |
| [`bluespace-dock.md`](bluespace-dock.md) — Bluespace Dock, Ship Registrar and ship preview | In progress (`ps-bluespace-dock`) |
