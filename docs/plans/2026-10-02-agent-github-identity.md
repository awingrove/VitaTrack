# Agent GitHub Identity (TD-010) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Agent sessions hold their own GitHub identity (machine account) and can never act as the human through `git` or `gh`; human approval of `main` becomes machine-enforced; the DL-003 replay is pinned by a self-skipping acceptance spec.

**Architecture:** An OpenCode `shell.env` plugin injects `GH_TOKEN` + git identity into every shell an agent session runs, failing closed with a sentinel when the token file is missing. Server-side GitHub rulesets (human executes the runbook) reject approve/merge/delete attempts made with agent credentials. A Playwright API spec exercises real GitHub and asserts those rejections.

**Tech Stack:** OpenCode plugin (ESM JavaScript), Playwright (API-only tests, Node `fetch`), GitHub REST API, GitHub rulesets, Markdown/YAML docs.

**Spec:** `docs/superpowers/specs/2026-10-02-agent-github-identity-design.md`

## Global Constraints

- Branch: `feature/td010-agent-identity` (all commits ride this branch; remote rejects direct pushes to `main`).
- Token file: `~/.config/opencode/gh-agent-token`, mode 600, raw token text, trimmed on read. Test-only override: `AGENT_GH_TOKEN_FILE`.
- Sentinel (fail-closed value): `__AGENT_TOKEN_FILE_MISSING_see_docs/factory/agent-identity.md`.
- Env contract set by the plugin on every shell: `GH_TOKEN`, `GIT_AUTHOR_NAME`, `GIT_AUTHOR_EMAIL`, `GIT_COMMITTER_NAME`, `GIT_COMMITTER_EMAIL`. `GH_TOKEN` is **never** left unset once the hook runs.
- Machine git identity: two constants at the top of the plugin (`AGENT_GIT_NAME`, `AGENT_GIT_EMAIL`) — set to the machine account's handle and email at install time (runbook step).
- Probe branch: `feature/td010-probe`; every probe commit message contains `[skip ci]`.
- Acceptance spec env: `AGENT_GH_TOKEN` (required for the GitHub half), optional `AGENT_GH_LOGIN` (tightens the control assertion).
- Claim vocabulary for behavior-claiming comments: `CHECKS:` / `PINS:` / `DOCUMENTS:` (AGENTS.md testing rules).
- No CI-derivable counts in any doc written here.
- Self-skip pattern copied from `e2e-tests/playwright/tests/supplement-llm-integration.spec.js`: skip is decided from the environment before any network call, with a printed reason naming the missing variable.

## Review Focus

- Token file missing at shell start: the shell must see the sentinel `GH_TOKEN`, so `gh`/`git push` fail 401 — never an unset `GH_TOKEN` that falls through to the human keyring. → plugin unit test "fails closed".
- Token rotation: replacing the token file must be picked up by the next shell exec without an OpenCode restart. → plugin unit test "picks up a rotated token file".
- `AGENT_GH_TOKEN_FILE` pointing at a nonexistent path must fail closed exactly like a missing default path. → covered in "fails closed" (fixture path is itself the nonexistent path).
- Acceptance spec without `AGENT_GH_TOKEN`: GitHub half skips with a printed reason; plugin half still runs and passes. → spec structure step asserts both describes exist and only the second skips.
- Probe commit without `[skip ci]` spawns workflow runs on every spec run. → assert the created commit's message contains `[skip ci]` as part of the probe test.

---

### Task 1: Agent identity plugin + unit tests

**Files:**
- Create: `.opencode/plugins/agent-identity.js`
- Create: `e2e-tests/playwright/tests/agent-identity.spec.js` (plugin half only in this task)

**Interfaces:**
- Consumes: nothing.
- Produces: named export `AgentIdentityPlugin: () => Promise<{ "shell.env": (input: {cwd: string}, output: {env: Record<string,string>}) => Promise<void> }>`; module constants `AGENT_GIT_NAME`, `AGENT_GIT_EMAIL`, `SENTINEL_TOKEN`; env contract from Global Constraints. Tasks 3–4 import `AgentIdentityPlugin` and `SENTINEL_TOKEN` from this file.

- [x] **Step 1: Write the failing tests** in `e2e-tests/playwright/tests/agent-identity.spec.js`

Three tests under `test.describe('agent identity plugin', ...)`; each writes a fixture token file under `os.tmpdir()`, sets `process.env.AGENT_GH_TOKEN_FILE` to it (restored in `afterEach`), calls `const hooks = await AgentIdentityPlugin()`, then `await hooks["shell.env"]({ cwd: process.cwd() }, output)` with `const output = { env: {} }`:

```js
test('DOCUMENTS: sets GH_TOKEN and the machine git identity from the token file')
  // fixture file contains "ghp_fixture_token\n"
  // expect(output.env.GH_TOKEN).toBe('ghp_fixture_token')
  // expect(output.env.GIT_AUTHOR_NAME).toBe(AGENT_GIT_NAME)  (same for COMMITTER_NAME)
  // expect(output.env.GIT_AUTHOR_EMAIL).toBe(AGENT_GIT_EMAIL) (same for COMMITTER_EMAIL)

test('PINS: fails closed with a sentinel GH_TOKEN when the token file is missing')
  // AGENT_GH_TOKEN_FILE -> path that does not exist
  // expect(output.env.GH_TOKEN).toBe(SENTINEL_TOKEN)   — and expect it to be defined, never ''

test('PINS: picks up a rotated token file without a restart')
  // write fixture with 'token_one', run hook; rewrite with 'token_two', run hook again
  // expect(second run output.env.GH_TOKEN).toBe('token_two')
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `cd e2e-tests/playwright && npx playwright test agent-identity -g "agent identity plugin"`
Expected: FAIL — module `.opencode/plugins/agent-identity.js` not found.

- [x] **Step 3: Implement the plugin** in `.opencode/plugins/agent-identity.js`

ESM module, auto-loaded from this directory at OpenCode startup. Shape: `export const AgentIdentityPlugin = async () => ({ "shell.env": async (input, output) => { ... } })`. Behavior in the hook body: resolve token path from `process.env.AGENT_GH_TOKEN_FILE` or `path.join(os.homedir(), ".config/opencode/gh-agent-token")`; `readFileSync` + `.trim()`; on any read error set `output.env.GH_TOKEN = SENTINEL_TOKEN` and `console.error` one line naming the missing path and the runbook; otherwise set `output.env.GH_TOKEN` to the trimmed token. Always set the four `GIT_*` vars to the `AGENT_GIT_NAME` / `AGENT_GIT_EMAIL` constants. Export `SENTINEL_TOKEN` and the two identity constants (exported so tests and Task 4 compare against them). Read the token file **per hook invocation** (that is the rotation pickup; the file is tiny).

- [x] **Step 4: Run the tests to verify they pass**

Run: `cd e2e-tests/playwright && npx playwright test agent-identity -g "agent identity plugin"`
Expected: PASS, three tests green.

- [x] **Step 5: Verify the suite still runs this file with the rest**

Run: `./test-e2e.sh` (accept `AGENT_GH_TOKEN` empty / blanks at prompts)
Expected: all specs pass or self-skip; `agent-identity.spec.js` contributes three green plugin tests.

- [x] **Step 6: Commit**

```bash
git add .opencode/plugins/agent-identity.js e2e-tests/playwright/tests/agent-identity.spec.js
git commit -m "feat: agent identity plugin with fail-closed GH_TOKEN injection"
```

---

### Task 2: Runbook `docs/factory/agent-identity.md`

**Files:**
- Create: `docs/factory/agent-identity.md`

**Interfaces:**
- Consumes: plugin contract from Task 1 (for the install/verify sections).
- Produces: the document AGENTS.md and FACTORY.md will point at (Task 4), and the GitHub-side procedure the human executes before Task 5.

- [x] **Step 1: Write the runbook** with exactly these sections:

1. **What this is** — one paragraph: agent sessions authenticate as the machine account; the human's keyring is unreachable from agent shells; enforcement is server-side (rulesets), not convention. Link the spec.
2. **One-time setup** — (a) create machine GitHub account (2FA, `+alias` email); (b) invite as collaborator (write) on this repo; (c) fine-grained PAT issued by the machine account **(landed as classic PAT, see runbook — fine-grained cannot write collaborator repos)**: all repositories the account can access, permissions **Contents RW, Pull requests RW, Workflows RW**, Metadata read, no admin, 1-year expiry; (d) paste token into `~/.config/opencode/gh-agent-token` (`chmod 600`); (e) edit `AGENT_GIT_NAME`/`AGENT_GIT_EMAIL` constants in `.opencode/plugins/agent-identity.js` to the machine account handle/email; (f) rulesets on the repo, **bypass actors = human account only**: `main` — require PR, ≥1 approving review, dismiss stale reviews, require approval from someone other than the last pusher, block force pushes, block deletions; all branches (`main`, `feature/**`, and the repo's other prefixes) — block deletions and force pushes.
3. **Verify** — `gh api user` with the token returns the machine account; approve/merge/delete attempts with the token are rejected (4xx); `echo $GH_TOKEN` in a **new** OpenCode session prints the token (plugins load at startup — a running session does not see a new plugin).
4. **Failure signature** — `gh`/`git push` 401 "Bad credentials" after a token-file problem means fail-closed fired; the fix is the token file, not new credentials.
5. **Rotation** — replace file contents; next shell picks it up (no restart). Rotate the CI secret `AGENT_GH_TOKEN` in the same pass or the acceptance spec silently stops enforcing.
6. **Per-new-repo checklist** — copy `.opencode/plugins/agent-identity.js` to `~/.config/opencode/plugins/` (or into the new repo's `.opencode/plugins/`), invite collaborator, apply both rulesets, verify (PAT reaches the new repo without re-issue).
7. **Probe cleanup** — `feature/td010-probe` accumulates one commit and one closed PR per acceptance run; human deletes it when it annoys (agent never can).

- [x] **Step 2: Commit**

```bash
git add docs/factory/agent-identity.md
git commit -m "docs: agent identity runbook (TD-010)"
```

---

### Task 3: Acceptance spec (GitHub enforcement half) + CI plumbing

**Files:**
- Modify: `e2e-tests/playwright/tests/agent-identity.spec.js` (add second describe)
- Modify: `test-e2e.sh`
- Modify: `.github/workflows/ci.yml` (e2e step `env:` block)

**Interfaces:**
- Consumes: `AGENT_GIT_NAME`/`SENTINEL_TOKEN` exports from Task 1's plugin module (for the plugin-plumbing assertion), probe branch name from Global Constraints.
- Produces: `e2e: agent-identity::<title fragments>` for the story map (Task 4) — exact test titles: `the agent authenticates as the machine account, not the human`, `the agent cannot approve its own PR`, `the agent cannot merge to main without independent approval`, `the agent cannot delete a feature branch`, `every probe commit carries [skip ci]`.

- [x] **Step 1: Write the failing tests** — `test.describe('agent GitHub enforcement (real GitHub)', ...)`, guarded **before any network call** by `test.skip(!process.env.AGENT_GH_TOKEN, 'Skipping — AGENT_GH_TOKEN not set ...')` naming that this pins GitHub-side enforcement (comment claims use `PINS:`). All requests via Node `fetch` against `https://api.github.com` with `Authorization: Bearer <AGENT_GH_TOKEN>`; repo constant `awingrove/VitaTrack` with an `AGENT_GH_REPO` override. Five tests, titles exactly as listed in Interfaces:

1. control: `GET /user` → `login` is truthy, `!== 'awingrove'`, and `=== process.env.AGENT_GH_LOGIN` when that is set.
2. probe: `PUT /repos/{repo}/contents/agent-probe/<timestamp>.txt` on branch `feature/td010-probe` (create-if-absent semantics: 404 on ref GET is fine, contents PUT with `branch` creates it), message `[skip ci] agent identity probe <ISO timestamp>`, body a short marker string; assert response `ok`, and assert the returned commit `message` contains `[skip ci]`. Then `POST /repos/{repo}/pulls` (head `feature/td010-probe`, base `main`, unique title with timestamp) → keep `number` and the returned `user.login` (the machine account).
3. self-approve: `POST /repos/{repo}/pulls/{number}/reviews` with `{"event":"APPROVE"}` → expect `!res.ok` (403/422), record `res.status()`.
4. merge: `PUT /repos/{repo}/pulls/{number}/merge` → expect `!res.ok`.
5. delete: `DELETE /repos/{repo}/git/refs/heads/feature/td010-probe` → expect `!res.ok` (ruleset `deletion` rule).

PR lifecycle: tests 2–5 are one serial describe (`describe.configure({ mode: 'serial' })` like the LLM spec, since they share the PR number); the PR is closed (`PATCH /repos/{repo}/pulls/{number}` `state:closed`) in an `afterAll`, and the probe branch is deliberately left in place (its persistence is what is pinned).

- [x] **Step 2: Run to verify they skip (not fail) without the secret**

Run: `cd e2e-tests/playwright && npx playwright test agent-identity`
Expected: plugin tests PASS; GitHub tests SKIP with the printed reason; exit 0.

- [x] **Step 3: Wire `AGENT_GH_TOKEN` through `test-e2e.sh`** — in the `if [[ -n "${CI:-}" ]]` branch add `export AGENT_GH_TOKEN="${AGENT_GH_TOKEN:-}"`; in the interactive path, if `AGENT_GH_TOKEN` is unset and `~/.config/opencode/gh-agent-token` exists and is readable, export its trimmed contents; otherwise export empty. **No prompt** — a PAT is not prompted for; the spec self-skips. One comment line saying the file is the same one the plugin reads (runbook pointer).

- [x] **Step 4: Wire the CI secret** — add `AGENT_GH_TOKEN: ${{ secrets.AGENT_GH_TOKEN }}` to the e2e step's `env:` block in `.github/workflows/ci.yml`, with one comment line: unset secret → empty → spec self-skips, same as the `LLM_*` trio.

- [x] **Step 5: Run the full suite**

Run: `./test-e2e.sh`
Expected: exit 0, GitHub half skipping without the secret.

- [x] **Step 6: Commit**

```bash
git add e2e-tests/playwright/tests/agent-identity.spec.js test-e2e.sh .github/workflows/ci.yml
git commit -m "test: acceptance spec pinning GitHub-side agent identity enforcement (TD-010)"
```

---

### Task 4: Story map, AGENTS.md, FACTORY.md, spec amendment

**Files:**
- Modify: `storymap.yaml`
- Modify: `AGENTS.md`
- Modify: `FACTORY.md` (step 7)
- Modify: `docs/superpowers/specs/2026-10-02-agent-github-identity-design.md` (§4 story-map bullet is already amended; only fix anything Task 1–3 resolved differently)

**Interfaces:**
- Consumes: exact test titles produced by Task 3 (fragments must literally appear in the spec file — `StoryMapConsistencyTests` checks that).
- Produces: green `StoryMapConsistencyTests`; docs that name the runbook.

- [x] **Step 1: Add the story-map entry** — new activity `Repository Governance`, first task:

```yaml
  - name: Repository Governance
    tasks:
      - id: GOV-1
        name: Agent GitHub identity
        entry_point: n/a (agent credential identity, not a UI location)
        stories:
          - title: Agent sessions act as the machine account and are rejected at approve, merge, and delete
            status: done
            priority: high
            tests:
              - e2e: agent-identity::the agent authenticates as the machine account, not the human
              - e2e: agent-identity::the agent cannot approve its own PR
              - e2e: agent-identity::the agent cannot merge to main without independent approval
              - e2e: agent-identity::the agent cannot delete a feature branch
              - e2e: agent-identity::every probe commit carries [skip ci]
```

(Use `n/a` entry-point prefix exactly — `StoryMapConsistencyTests` accepts `entry_point` starting with `n/a`, precedent `SHELL-2`.)

> **Landed differently:** the task id is `SHELL-4`, not `GOV-1`. `ShardOwnershipTests.ValidateStoryMapIds` requires every story-map id prefix to match a shard in `shards.yaml`, and the controller ruling forbade inventing a `GOV` slice — so the task rides the existing `SHELL` shard prefix and the spec is claimed in SHELL's `e2e_specs` (the `supplement-llm-integration.spec.js` precedent).

- [x] **Step 2: Run the story-map gate**

Run: `dotnet test VitaTrack.sln --filter StoryMapConsistencyTests`
Expected: PASS. If the fragment match fails, fix the story ref to match the spec title exactly — never the reverse.

- [x] **Step 3: AGENTS.md — new "Agent GitHub identity" section** under the CLI & Git Workflow area: sessions authenticate as the machine account via `.opencode/plugins/agent-identity.js` (token file `~/.config/opencode/gh-agent-token`); fail-closed sentinel when the file is missing; agents may push feature branches and open PRs, never approve, merge, close, or delete branches — those are rejected server-side, so routing around a rejection is pointless; runbook `docs/factory/agent-identity.md` holds setup, rotation, and the per-new-repo checklist. Keep it to ~10 lines; no CI-derivable counts.

- [x] **Step 4: FACTORY.md step 7** — replace the soft-rule wording ("The human approves and merges. An agent never approves...") so it cites the mechanical enforcement: the human approves and merges because GitHub **rejects** agent attempts at approve/merge/delete (rulesets + separate identity — see `docs/factory/agent-identity.md`), and the gate-rejection rule (one rejection → fix named cause; second → stop and report) is now backed by identity, not just convention.

- [x] **Step 5: Spec amendment** — if any detail landed differently than the spec's §2/§3 wording (e.g. `AGENT_GH_REPO` override, `afterAll` PR close), update the spec in the same commit. Spec must stay true to what shipped.

- [x] **Step 6: Full gates**

Run: `./format-check.sh && dotnet test`
Expected: all green.

- [x] **Step 7: Commit**

```bash
git add storymap.yaml AGENTS.md FACTORY.md docs/superpowers/specs/
git commit -m "docs: wire agent identity into story map, AGENTS.md, FACTORY.md (TD-010)"
```

---

### Task 5: Live verification checkpoint (human + agent, before TD-010 closes)

**Files:**
- Create/Modify: none (verification only)

**Interfaces:**
- Consumes: everything from Tasks 1–4 plus the human's GitHub-side setup (Task 2 runbook, section 2).
- Produces: the evidence TD-010's closure cites.

- [ ] **Step 1 (human): execute the runbook one-time setup** (account, PAT, collaborator, rulesets, token file, plugin constants) — runbook section 2.

- [ ] **Step 2 (agent): live plugin wiring smoke** — start a **new** OpenCode session, run `echo $GH_TOKEN` and `git config user.name` equivalents via the shell tool:
Expected: `GH_TOKEN` prints the machine token (not empty, not sentinel); a sentinel print means the token file path/permissions are wrong — runbook section 4.

- [ ] **Step 3 (agent): prove the keyring is unreachable** — in the same session:
(a) `gh api user` → machine login;
(b) `printf 'protocol=https\nhost=github.com\n' | gh auth git-credential get` → the
machine token, not the human's `gho_` token (this is the exact path `git push` uses
through `~/.gitconfig`'s `credential.helper = gh auth git-credential`).
Expected: machine account and machine token in both — `awingrove` or a `gho_` token here means the plugin is not reaching shell env (runbook section 4).

- [ ] **Step 4: run the acceptance spec for real** — `export AGENT_GH_TOKEN=<machine token> AGENT_GH_LOGIN=<machine login>` (or rely on `test-e2e.sh` reading the token file), run `./test-e2e.sh`.
Expected: all five GitHub tests PASS (rejections observed), plugin tests PASS.

- [ ] **Step 5: replay DL-003 by hand once** — with the machine token, attempt branch delete and self-merge via `gh api` exactly as in the incident; confirm 4xx and record the statuses in the PR description.
Expected: rejected at every step.

---

### Task 6: Close TD-010

**Files:**
- Modify: `docs/factory/technical-debt.yaml`

**Interfaces:**
- Consumes: Task 5 evidence.
- Produces: register closed-entry shape (`id`, `title`, `closed`, `note`) per AGENTS.md rule 6.

- [ ] **Step 1: move TD-010 from `open` to `closed`** — `closed: <date>`, `note: >-` citing: separate machine identity + `shell.env` plugin (fail-closed), rulesets with human-only bypass, acceptance spec `agent-identity.spec.js` pinning the DL-003 rejections, runbook `docs/factory/agent-identity.md`. Keep the old `what`/`interest` context in the note where it still earns its place; remove the `where`/`paydown` fields (closed shape).

- [ ] **Step 2: register gate**

Run: `dotnet test VitaTrack.sln --filter TechnicalDebtRegisterTests`
Expected: PASS — unique ids, no `open`/`closed` overlap, closed shape complete.

- [ ] **Step 3: Commit**

```bash
git add docs/factory/technical-debt.yaml
git commit -m "docs: close TD-010 (agent GitHub identity)"
```

---

## Sequencing note

Tasks 1–4 are self-contained and land green on the branch immediately (GitHub half self-skips). Task 5 needs the human's browser work; Task 6 is deliberately last — TD-010 stays `open` until the acceptance spec has passed against real GitHub once. The PR carrying Tasks 1–4 can be reviewed and merged before Task 5 completes; Task 6 rides a follow-up commit/PR if so.
