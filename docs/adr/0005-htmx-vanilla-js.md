# ADR-0005: HTMX + vanilla JS for UI interactivity

- **Status:** Accepted
- **Date:** 2026-08-05 (retroactively documented 2026-08-13)

## Context

The Create Supplement / UpdateNutrients / Review flows each need
server-backed partial rendering. Options considered:

- A SPA framework (React/Vue) — bundler, separate build pipeline,
  more surface to maintain, fights the ASP.NET MVC layout.
- Plain MVC POST + redirect — works; loses the inline enrichment
  experience where the supplement list shows nutrients immediately
  after the LLM responds without a page navigation.
- HTMX — small JS lib, harnesses plain MVC actions returning
  partials, ships HTML over the wire.
- Hotwire Turbo — similar role to HTMX, but less adopted when the
  decision was taken.

`AGENTS.md` already mandated 'No inline JavaScript' under a strict
CSP `script-src 'self' cdn.jsdelivr.net` (review §2.1 documented that
all js must be self-hosted external files so re-execution on swap
works).

## Decision

Use **HTMX 2.0** for the partial swap flows (`hx-post`/`hx-target`/
`hx-swap`/`hx-swap-oob`). All other UI behaviour (row add/remove,
checkbox selection, alert dismissal) is **external vanilla
JavaScript** under `wwwroot/js`. **No SPA framework**, no
bundler pipeline.

HTMX is **self-hosted** at `/lib/htmx/htmx.min.js` (commit f8f0af9;
unblocked CSP `script-src 'self'`). No CDN runtime dependency.
Partials include their own `<script src="/js/...">` tags because
htmx re-executes external scripts on swap.

## Consequences

- One htmx ~50K JS file under wwwroot; Bootstrap stays on CDN
  (`cdn.jsdelivr.net`, the other half of `script-src`).
- Each new partial that needs JS must have its own external
  `.js` (inline scripts are silently dropped by CSP in Release,
  see VitaTrack.Web/AGENTS.md Static Files).
- New interactive surface = small JS file + htmx partial in most
  cases. Heavier patterns (live graphs, etc.) warrant a separate
  ADR.
- If page-level rich interaction eventually dominates (drag-drop
  comparison, family-tree viz), HTMX partials may be insufficient;
  revisit then.