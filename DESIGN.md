---
version: alpha
name: VitaTrack
description: Design tokens and UI rules for VitaTrack, a family supplement tracker built on ASP.NET MVC, Bootstrap 5, and HTMX. Tokens mirror Bootstrap 5.3 defaults where possible so Razor views can stay stock Bootstrap underneath.

colors:
  primary: "#0d6efd"
  on-primary: "#ffffff"
  secondary: "#6c757d"
  on-secondary: "#ffffff"
  success: "#198754"
  on-success: "#ffffff"
  danger: "#dc3545"
  on-danger: "#ffffff"
  warning: "#ffc107"
  on-warning: "#000000"
  info: "#0dcaf0"
  on-info: "#000000"
  surface: "#ffffff"
  background: "#f8f9fa"
  navbar-background: "#212529"
  navbar-text: "#ffffff"
  text: "#212529"
  text-muted: "#6c757d"

typography:
  body:
    fontFamily: "system-ui, -apple-system, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif"
    fontSize: 1rem
    fontWeight: 400
    lineHeight: 1.5
  heading-page:
    fontFamily: "{body.fontFamily}"
    fontSize: 1.75rem
    fontWeight: 500
    lineHeight: 1.2
  heading-section:
    fontFamily: "{body.fontFamily}"
    fontSize: 1.25rem
    fontWeight: 500
    lineHeight: 1.2
  small:
    fontFamily: "{body.fontFamily}"
    fontSize: 0.875rem
    fontWeight: 400
    lineHeight: 1.5

rounded:
  sm: 0.25rem
  md: 0.375rem
  lg: 0.5rem
  pill: 50rem

spacing:
  3xs: 0.25rem
  2xs: 0.5rem
  xs: 0.75rem
  sm: 1rem
  md: 1.5rem
  lg: 3rem

components:
  navbar:
    backgroundColor: "{colors.navbar-background}"
    textColor: "{colors.navbar-text}"
  alert-warning:
    backgroundColor: "{colors.warning}"
    textColor: "{colors.on-warning}"
  form-text:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text-muted}"
    typography: "{typography.small}"
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.on-primary}"
    rounded: "{rounded.sm}"
    padding: 6px 12px
  button-primary-hover:
    backgroundColor: "#0b5ed7"
    textColor: "{colors.on-primary}"
  button-create:
    backgroundColor: "{colors.success}"
    textColor: "{colors.on-success}"
    rounded: "{rounded.sm}"
    padding: 6px 12px
  button-destructive:
    backgroundColor: "{colors.danger}"
    textColor: "{colors.on-danger}"
    rounded: "{rounded.sm}"
    padding: 6px 12px
  button-secondary-action:
    backgroundColor: "transparent"
    textColor: "{colors.primary}"
    rounded: "{rounded.sm}"
    padding: 6px 12px
  button-row-action:
    backgroundColor: "transparent"
    textColor: "{colors.info}"
    rounded: "{rounded.sm}"
    padding: 4px 8px
  input:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
    rounded: "{rounded.sm}"
    height: 38px
  table-header:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
  card:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
    rounded: "{rounded.md}"
    padding: 16px
---

# VitaTrack Design System

## Overview

VitaTrack is a pragmatic family supplement tracker: data-dense CRUD tables,
forms, HTMX-driven partial updates, and two report pages. The UI should feel
calm, functional, and boring in the best sense — no decoration that does not
aid scanning or task completion.

The implementation stack is **Bootstrap 5.3 via CDN + HTMX + vanilla JS**.
This document does not replace Bootstrap; it selects from it. When building or
editing UI:

- Use stock Bootstrap classes that match these tokens. Never invent custom CSS
  unless no Bootstrap utility/component achieves the result.
- Prefer Bootstrap utilities (`mt-3`, `d-flex`, `gap-2`) over inline `style`
  attributes.
- All interactivity follows the repo's CSP rules: no inline scripts, no inline
  event handlers; JS lives under `wwwroot/js`.

## Colors

Semantic roles map 1:1 onto Bootstrap theme colors. Use the Bootstrap class,
not raw hex:

| Role | Token | Bootstrap usage |
|---|---|---|
| Primary action | `{colors.primary}` | `btn-primary`, `btn-outline-primary` |
| Create / positive | `{colors.success}` | `btn-success` |
| Destructive / delete | `{colors.danger}` | `btn-danger`, `text-danger` |
| Informational link-action | `{colors.info}` | `btn-outline-info` |
| Neutral / cancel | `{colors.secondary}` | `btn-outline-secondary` |
| Page chrome | `{colors.navbar-background}` | `navbar-dark bg-dark` |

Rules:

- Exactly one primary action per view region. "Add New X" is always
  `btn btn-success`.
- Row-level actions inside tables are always small outline buttons:
  `btn btn-sm btn-outline-info` (navigate) or `btn btn-sm btn-primary` (edit).
- Delete is always `btn-sm btn-danger` inside its own POST form with a
  confirmation hook (`data-confirm-message`), never an `<a>`.
- Never introduce new hues. If a state seems to need one, reuse `warning` for
  caution and `danger` for errors.
- Muted explanatory text uses `text-muted`; do not lower opacity instead.

## Typography

Bootstrap's reboot defaults carry the system font stack. Headings use the
default Bootstrap scale — page title is `<h2>` (matching existing views),
section headings within a page are `<h3>`–`<h5>` or `.h4`-style utilities.

Rules:

- One `<h2>` per page, matching `ViewData["Title"]`.
- Table headers: sentence case, never ALL CAPS, no letter-spacing tricks.
- Numbers in tables (costs, counts) stay right-aligned when sortable columns
  benefit from it; currency is rendered as `£F2` server-side, not formatted in JS.
- Empty states say what is missing and how to fix it ("No supplements yet —
  Add New Supplement"), using `text-muted`.

## Layout

Content sits in `main.container` beneath a fixed dark navbar (defined once in
`_Layout.cshtml`). Pages compose vertically: title → toolbar → content.

- Vertical rhythm between major blocks: `mb-3`/`mb-4` (`{spacing.xs}`–`{spacing.md}`).
- **Form fields and action rows are wrapped in `<div class="mb-3">`.** The
  Bootstrap 4 `form-group` class has no effect in Bootstrap 5 — never use it;
  without `mb-3` blocks collapse together (e.g. buttons touching the last
  input).
- Toolbars ("Add", "Import", "Delete Selected") are `<p>` or flex rows directly
  under the title, buttons separated by default spacing or `gap-2 d-flex`.
- Tables are plain `table` (optionally `table-striped` for long reports);
  never `table-dark`. Sortable tables carry `data-sortable` attributes.
- Forms use Bootstrap grid or `row g-3` + `col-md-*`; labels above inputs,
  help text via `form-text text-muted`.
- Modals follow `_ImportModal.cshtml`: standard Bootstrap modal markup, opened
  by `data-bs-toggle`, never by inline JS.

## Shapes

Radii come straight from Bootstrap defaults; do not override. Pills
(`badge rounded-pill`) only for status chips such as nutrient counts or flags.
Cards, modals, and inputs keep their default component radii.

## Components

### Buttons

| Intent | Class |
|---|---|
| Create entity | `btn btn-success` |
| Edit entity | `btn btn-sm btn-primary` |
| Navigate to child list (e.g., Nutrients) | `btn btn-sm btn-outline-info` |
| Delete | `btn btn-sm btn-danger` |
| Secondary dialog opener (Import CSV) | `btn btn-outline-primary` |
| Bulk destructive | `btn btn-danger` |
| Cancel / dismiss | `btn btn-outline-secondary` |

HTMX forms must disable their submit button while a request is in flight and
re-enable on response (external JS only). Show progress with
`spinner-border spinner-border-sm` injected into the button, never a
full-screen overlay.

### Tables

Standard pattern per `Views/Supplement/Index.cshtml`: checkbox column for bulk
select (`select-all` + `row-checkbox` wired to a shared delete form), sortable
headers via `data-sort-key`, actions column last. Row checkboxes belong to the
bulk-delete form via the `form` attribute.

### Forms

- Inputs: `form-control` / `form-select`, validation feedback via
  `is-invalid` + validation partials (`_ValidationErrors.cshtml`), never alert
  boxes for field errors.
- Server-side validation is authoritative; `required` attributes are managed
  by external JS where conditional.
- Anti-forgery token in every POST form.

### Feedback

- Success after redirect: TempData-driven `alert alert-success alert-dismissible`.
- Errors that block the whole operation: `alert alert-danger`.
- Field errors: `text-danger` / `is-invalid` only.
- HTMX OOB swaps may append `alert` fragments for immediate feedback.

## Do's and Don'ts

**Do**

- Reuse an existing view's markup as the reference implementation before
  inventing structure — Supplement Index is canonical for tables; PrescribedDose
  Create is canonical for forms.
- Reach every new GET page from nav or an in-app button in the same change.
- Keep partials self-contained: own script tag (`<script src="/js/...">`) if
  they need JS after swap.
- Use anonymous objects through `ViewData`, never ValueTuples.

**Don't**

- Don't add custom CSS files, CSS frameworks, icon fonts, or component
  libraries alongside Bootstrap.
- Don't use inline styles, inline scripts, or `onclick` handlers (CSP blocks
  them in non-Dev environments).
- Don't hardcode ids in links or tests; resolve rows via DOM lookups.
- Don't restyle Bootstrap components with ad-hoc utility soup when one
  component class exists (`card`, `alert`, `badge`).
- Don't add color without meaning — every color signals an intent listed above.
