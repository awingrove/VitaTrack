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
- **This document is part of every UI change.** If a change introduces or alters
  a pattern, interaction, or component that isn't covered here, the author must
  raise it with the user and — on agreement — amend this document in the same
  change. A shipped UI pattern that this document doesn't describe is a defect.

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
- **Page-bottom whitespace comes from the layout, not the page.** `main.container`
  in `_Layout.cshtml` carries `pb-5`; views must never rely on the last block's
  margin to keep content off the viewport edge (a trailing `mt-3` div provides
  zero space below it).
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
| Bulk compare (Compare Selected) | `btn btn-outline-primary` |
| Cancel / dismiss | `btn btn-outline-secondary` |
| Save/confirm a setting that persists (Settings page) | `btn btn-success` |
| Save a subordinate choice (model, reasoning effort) | `btn btn-sm btn-primary` |
| Disconnect a service | `btn btn-sm btn-danger`, in its own POST form with `data-confirm-message` |

**Settings is a nav destination, not a sub-page of a supplement.** `Settings`
(`ServiceConnectionController`) sits in the navbar beside the other top-level
destinations, because a connection is app-wide state rather than anything owned
by one supplement — reaching it from a supplement row would say otherwise.

**Three intents share a class value with an entity intent, and the region picks
the row.** `btn btn-success` is "create entity" *and* "save a setting";
`btn btn-sm btn-primary` is "edit entity" *and* "save a subordinate choice";
`btn btn-sm btn-danger` is "delete" *and* "disconnect a service". A Settings
button is never a supplement, family member or nutrient, so on that page the
setting rows apply and the entity rows do not. The class is the same either way
— Bootstrap 5.3 defaults only, and no second class was invented to tell them
apart.

HTMX forms must disable their submit button while a request is in flight and
re-enable on response (external JS only). Show progress with
`spinner-border spinner-border-sm` injected into the button, never a
full-screen overlay.

### Tables

Standard pattern per `Views/Supplement/Index.cshtml`: checkbox column for bulk
select (`select-all` + `row-checkbox` wired to a shared delete form), sortable
headers via `data-sort-key`, actions column last. Row checkboxes belong to the
bulk-delete form via the `form` attribute. The same selection also feeds
**Compare Selected** (`#compare-selected-btn`): the anchor is disabled until at
least 2 rows are checked, its `href` is rebuilt by
`wwwroot/js/compare-selected.js` from the checked boxes in DOM order, and a
selection is capped at 5 — the 6th checked box is unchecked and `#compare-hint`
("Compare up to 5 supplements.") becomes visible at the cap. The cap is
client-side only; the server accepts any ≥ 2 ids.

Expandable detail rows use Bootstrap collapse on `<tr>` targets: a
`btn btn-link p-0` trigger with `data-bs-toggle="collapse"` /
`data-bs-target="#row-id"`, and the hidden row marked
`<tr class="collapse" id="row-id">` with a full-width `colspan` cell
(reference: `Views/Reporting/NutrientReport.cshtml`). `.collapse:not(.show)`
hides via `display: none`, so the `<tr>` reverts to its natural `table-row`
display when shown — never JS show/hide.

Expand/collapse affordance: an inline SVG chevron rides in the trigger
(right-facing when collapsed, down-facing when expanded), wrapped in a
`<span data-chevron="collapsed|expanded">` and flipped by
`wwwroot/js/report-toggle.js` listening to `show`/`hide.bs.collapse`. Wrap
chevrons in HTML `<span>`s, never toggle `hidden` on `<svg>` directly (the
`hidden` attribute's UA `display:none` rule does not apply to SVG elements).
No icon fonts, no custom CSS.

### Comparison grid

`Views/Supplement/Compare.cshtml` renders one plain `table` (never
`data-sortable`) from the `ComparisonGrid` model. Each column header is three
lines: supplement name, brand and serving (`DailyDose` free text, shown as-is),
with the latter two as `text-muted small`. The first cell of each body row is
the nutrient label; blend children are indented beneath their parent via
Bootstrap spacing (`ps-4` on the label cell), and an em dash (`—`) marks "not
in this supplement". Cells show the specific form with the normalized dosage
after it in `text-muted` — stock Bootstrap only, no custom CSS.

### Forms

- Inputs: `form-control` / `form-select`, validation feedback via
  `is-invalid` + validation partials (`_ValidationErrors.cshtml`), never alert
  boxes for field errors.
- Server-side validation is authoritative; `required` attributes are managed
  by external JS where conditional.
- Anti-forgery token in every POST form.

### Connection state

The Settings page (`ServiceConnectionController.Index`, nav item **Settings**) owns
the AI service connection. Its dynamic region is one element — `#connection-state`
in `Views/ServiceConnection/_ConnectionState.cshtml` — that the `Connect` and
`SelectModel` POSTs swap with `hx-swap="outerHTML"` (`Delete` is the controller's
third POST and swaps nothing: it is a full-page form that redirects, so a reload
cannot resubmit it). The id lives in the partial, not in `Index.cshtml`, because
an outerHTML swap replaces the target element: a wrapper the page owned would be
gone after the first swap and the second would have no target. Every outcome of
either swapping POST (saved, rejected, unverified) must be visible inside that
region.

Three states, one component. `ConnectionBadgeViewComponent` +
`_ConnectionBadge.cshtml` is the single renderer, so the Settings page and the
enrichment pages cannot disagree about what the state looks like:

| State | Badge | Class | Notes |
|---|---|---|---|
| No connection | *nothing rendered* | — | Enrichment refuses in words and names Settings; a badge would be decoration with no state to describe. |
| Connected, verified | `verified` | `badge rounded-pill bg-success` | The last `GET {BaseUrl}/v1/models` answered with this key. |
| Connected, unverified | `unverified` | `badge rounded-pill bg-warning text-dark` | A failed probe still saves the connection. Three causes produce this state — no model list, a base URL the service does not serve, a rejected key — and the app cannot tell them apart, so neither this badge nor `_Unverified.cshtml` may promise anything about whether enrichment works: one of the three is a connection that enriches fine. Never red: unconfirmed is not broken, and the connection was saved. |

Both badges are always visible — the pill is the state, not a hover reveal — and
each carries a `title` whose explanation is hover-only: no icon, no colour beyond
the token pair. `_Unverified.cshtml` is **not** that explanation. It renders above
the picker on the one response that answers a probe which did not verify, and on
no other: a plain page load must not spend a network round trip, so it carries no
catalog and no note, and repeating a warning about a probe the user never asked
for would be noise. The badge is the lasting truth; the note says why the field
underneath it is free text right now.

### Model and reasoning-effort choice

The model field is a **dropdown when the probe this response answers listed
models, and free text when it did not** — one signal (`Models.Count`) decides
which, so the two branches cannot disagree about when a picker is offered.
The signal is **per response, not per connection**: it describes the catalog
this response is the answer to, and the picker's own save answers with no
catalog, so clicking **Save model** turns a dropdown into a free-text input
carrying the model just saved. That is three individually-correct decisions
interacting (a save is not a probe; a reload does not re-probe; `Models` is not
stored), and it is why the field can never be a dropdown the user is looking
at twice. The value is stored, prefilled and used either way — the demotion is
a rendering change, not a lost choice. Whether that is the right interaction is
a design question, not a bug report; the sentence here records what the app
does so the next reader is not surprised, and changing it would be a
`DESIGN.md` amendment of this paragraph rather than a quiet fix.
"Models" is deliberately *not* a stored fact: a reload does not re-probe, so the
same saved connection renders free text after a load and a dropdown right after
the connect that discovered its models. Reasoning effort is always a
`form-select` of the registry's fixed six values, with help text saying not every
service uses one.

Both forms carry `asp-action`/`asp-controller`/`method` *as well as* `hx-post`.
htmx is the enhancement, not the mechanism: a form carrying only `hx-post`
degrades to a GET of the current URL, which discards the user's choice with no
error at all. With the tag helpers, a browser without htmx posts for real and the
controller redirects to a page that says what happened.

Ids on these forms are prefixed (`picker-model`, `picker-variant`) while the
`name` attributes are not. The connect form's own model field is `asp-for="Model"`,
which renders `id="Model"` — and two elements sharing one id break the
`<label for>` association for both.

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
