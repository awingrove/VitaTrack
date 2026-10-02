# Design: Agent GitHub Identity (TD-010)

Date: 2026-10-02
Status: awaiting spec review
Closes: TD-010 (`docs/factory/technical-debt.yaml`), on acceptance-spec green

## Intent

Agent sessions must hold their own GitHub identity and must never be able to act as
the human (`awingrove`) through `git` or `gh`. Human approval of `main` becomes
machine-enforced server-side rather than a convention. The DL-003 failure (agent
deleted a feature branch mid-CI, auto-closing PR #19) must become impossible, and
replaying it with agent credentials must be *rejected by GitHub*, not deferred.

Success criteria:

1. Every OpenCode session authenticates to GitHub as the machine account — for both
   `gh` calls and `git push` over HTTPS — with no path to the human keyring token.
2. A missing/rotated token fails **closed**: agent operations error loudly instead of
   silently falling back to human credentials.
3. Server-side rulesets reject, with agent credentials: approving the agent's own PR,
   merging `main` without an independent approval, and deleting or force-pushing any
   protected branch.
4. The above is pinned by a self-skipping API spec that exercises real GitHub.
5. Commit authorship in the audit trail distinguishes agent commits from human ones.

## Decisions (settled during brainstorming)

- **Identity: second personal account ("machine account") + fine-grained PAT.**
  Chosen over a GitHub App: single-dev pragmatism, and GitHub's built-in
  author-cannot-approve semantics need a real user account to work on. GitHub App
  remains the org-scale option; not needed here.
- **One machine account serves all future repos** (user has more repos landing soon).
- **Bitwarden Secrets Manager: out of scope** (deferred by the user; revisit as an
  optional storage/rotation layer later, not part of TD-010).
- **All OpenCode sessions are agent sessions.** The human acts through a plain
  terminal (keyring, untouched) and the GitHub web UI. No per-session discipline:
  injection happens centrally in OpenCode config.
- **Agent never deletes branches** — not its own, not anyone's. Human keeps delete
  rights via ruleset bypass. Merged-branch cleanup is a human action.
- **Acceptance test: self-skipping API spec** reading a repo secret
  (`AGENT_GH_TOKEN`), same pattern as the `LLM_API_KEY` real-provider specs.
- **GitHub-side setup is executed by the human** from a runbook; the agent must not
  perform account, PAT, or ruleset changes with human credentials.

## Assumptions

- The human's own keyring token (`repo`, `workflow`, `gist`, `read:org`) stays as-is.
- The human and agent may share the same working copy; isolation is by environment,
  not by directory.
- Existing branch prefixes: `main`, `feature/**`, plus whatever else the runbook
  enumerates at ruleset creation time; the runbook's per-repo checklist keeps the
  list current.

## Out of scope

- Bitwarden Secrets Manager, GitHub App migration, organization transfer, commit
  signing requirements, narrowing the human's own token scopes.

---

## §1 — GitHub-side runbook (human executes, browser)

One-time steps, then a short per-new-repo checklist.

1. **Machine account**: second GitHub personal account (suggested handle
   `awingrove-bot`), 2FA enabled, unique email (catch-all or `+alias`).
2. **Repo access**: invite the machine account as **collaborator (write)** on
   VitaTrack and on each future repo; the invite is accepted from the machine
   account's email. Per-new-repo.
3. **Fine-grained PAT**, issued by the machine account:
   - Resource owner: the machine account; repositories: **all repositories the
     account can access** (editable later — a future repo then needs only the
     collaborator invite, no new token).
   - Permissions: **Contents read/write, Pull requests read/write, Workflows
     read/write** (Workflows is the fine-grained equivalent of the classic `workflow`
     scope; without it future `ci.yml` edits fail), Metadata read.
   - No administration, no `delete_repo`. Expiration: max practical (1 year);
     rotation is a runbook step.
4. **Rulesets** on each repo — both with **bypass actors = the human account only**:
   - `main`: require pull request, require ≥1 approving review, dismiss stale
     reviews, **require approval from someone other than the last pusher**, block
     force pushes, block deletions.
   - All branches: block deletions and force pushes (enumerate the repo's actual
     branch prefixes as patterns).
5. **Verify**: `GH_TOKEN=<pat> gh api user` returns the machine account; then attempt
   approve, merge, and branch-delete with the PAT and confirm each is rejected.

## §2 — Repo-side credential isolation (implemented in this change)

- **Token file**: `~/.config/opencode/gh-agent-token`, mode 600, outside any repo so
  it can never be committed. The human pastes the PAT once; the runbook documents
  rotation (replace file contents; no code change).
- **Injection: project-level OpenCode plugin** at
  `.opencode/plugins/agent-identity.js` in each repo. Files in that directory are
  auto-loaded at startup (no config listing needed), so the plugin is version
  controlled, reviewable, and testable; the runbook tells future repos to copy it
  into `~/.config/opencode/plugins/` for global coverage. *(Amended after
  implementation: the plugin is OpenCode V2 shape — a default export whose
  `ctx.shell.hook('create.before')` injects per shell creation, the earliest
  per-shell seam — with the V1 `shell.env` named export kept in lockstep; the
  spec tests drive both entrypoints at the same injection body.)* Each injection
  sets:
  - `GH_TOKEN` — overrides the keyring entirely for `gh`, and flows through
    `credential.helper = gh auth git-credential` in `~/.gitconfig`, so `git push`
    over HTTPS resolves to the same machine token. Keyring never reached.
  - `GIT_AUTHOR_NAME` / `GIT_AUTHOR_EMAIL` / `GIT_COMMITTER_NAME` /
    `GIT_COMMITTER_EMAIL` — machine account identity, so commit authorship
    distinguishes agent from human in the audit trail (and makes `human_interventions`
    attributable).
  OpenCode's config schema has no session-level `environment` key, but the plugin's
  per-shell hook is the documented seam for injecting env into all shell execution
  (AI tools and integrated terminals); spawned shells inherit it. Chosen over a
  shell wrapper function because it covers every launch path (CLI, TUI, desktop) with
  no user discipline.
- **Fail-closed**: token file missing or unreadable → plugin sets `GH_TOKEN` to a
  sentinel invalid value and logs a loud warning. Every `gh`/`git push` then fails
  401 with an obvious cause instead of silently reverting to human credentials.
  `GH_TOKEN` is never left unset once the plugin loads.
- Human terminal: no environment changes outside OpenCode; keyring auth untouched.

## §3 — Acceptance spec (TD-010 paydown step 4)

- Playwright **API-only** spec (no browser) under `e2e-tests/playwright/`, reading
  `AGENT_GH_TOKEN` from `process.env`; absent → test **skips with a printed reason**
  (exact pattern of the `LLM_API_KEY` real-provider spec). CI gains an optional
  repository secret `AGENT_GH_TOKEN`, added as one line in the e2e step's existing
  `env:` block beside the `LLM_*` secrets — no pipeline restructuring. The target
  repo defaults to `awingrove/VitaTrack`, overridable via `AGENT_GH_REPO`; the
  optional `AGENT_GH_LOGIN` tightens the control test from "not the human" to the
  exact machine account.
- Flow, every step authenticated with the machine token:
  1. Control: `gh api user` (or REST `/user`) resolves to the machine account —
     proves the token is not the human's before anything is asserted.
  2. Append one commit to fixed probe branch `feature/td010-probe` (create if
     absent; never force-push — force pushes are themselves blocked). One small
     commit per run; the branch is deliberately persistent. Probe commits carry
     `[skip ci]` so spec runs do not spawn workflow runs (minutes + noise).
  3. Open a PR from the probe branch.
  4. Assert server-side rejections: submit an approving review on own PR → 4xx;
     attempt merge of `main`-bound PR without independent approval → 4xx;
     attempt `DELETE` on the probe branch → 4xx.
  5. Close the PR (closing stays permitted; the branch remains — its persistence is
     part of what is pinned). The close runs in an `afterAll` so it happens whether or
     not the rejection tests passed. Runbook notes occasional human cleanup of the probe
     branch's accumulated commits. *(Amended after implementation: when the probe branch
     is absent it is bootstrapped from `main`'s HEAD via `POST /git/refs` first — the
     contents API cannot create a ref — and each run opens a fresh PR, since a stale
     open PR would reject the next run's `POST /pulls`.)
- Assertions are on GitHub's HTTP answers to real credentials — a replay of DL-003
  that GitHub itself rejects, re-run after any auth or ruleset change.
- Spec must state in a comment that it pins GitHub-side enforcement and therefore
  self-skips without the secret (claim vocabulary: `PINS:` per AGENTS.md).

## §4 — Docs and registers

- `AGENTS.md`: new "Agent GitHub identity" section — env contract, fail-closed
  behavior, what agent credentials may and may not do, pointer to the runbook.
- `docs/factory/agent-identity.md` (runbook): one-time setup steps, PAT rotation,
  **per-new-repo checklist** (collaborator invite → rulesets → verify), probe-branch
  cleanup note.
- `FACTORY.md` step 7: amend wording from soft convention to cite the mechanical
  enforcement (rulesets + separate identity) and the runbook.
- `docs/factory/technical-debt.yaml`: TD-010 moves to `closed` with `closed_in`
  naming the accepting commit — only after the spec is green locally and in CI.
- `storymap.yaml`: **must** carry an `e2e: <new-spec-stem>::<title fragment>` ref —
  `StoryMapConsistencyTests` fails the build on any unreferenced `*.spec.js`. This
  is non-UI work with no in-app entry point, so the story needs a precedent-correct
  `entry_point` (research existing maintenance/infra stories first); if none
  exists, a maintenance story with an honest non-app entry point is the fallback,
  flagged to the human rather than faked. Landed as activity `Repository
  Governance`, task `SHELL-4` with `entry_point: n/a (...)` — a `GOV-1` id would
  demand a brand-new `GOV` shard (`ShardOwnershipTests` cross-checks every
  story-map prefix against `shards.yaml`), and a new slice was ruled out for this
  work — and the spec is claimed
  in `shards.yaml` under the SHELL shard's `e2e_specs` — the same precedent as
  `supplement-llm-integration.spec.js` under LLM.

## Testing

- **Verify-first spikes, before any code lands** (ordering is a plan concern but the
  risks are spec-level): (a) a plugin setting `process.env` is actually inherited by
  `bash` tool shells — if not, §2's mechanism changes to the wrapper-function
  fallback and the spec must be amended; (b) `git push` resolves through
  `gh auth git-credential` to `GH_TOKEN`, proving the keyring is unreachable.
- Unit/plugin: a small test or scripted check that the plugin sets the four git
  identity vars and `GH_TOKEN`, and fails closed on an absent token file (fixture
  path, not the real file).
- Acceptance: §3 spec — green locally with the token file exported, self-skipped in
  CI until `AGENT_GH_TOKEN` is set, then enforced on every e2e run.
- Docs: existing `ClaimCommentTests` / `TechnicalDebtRegisterTests` /
  `StoryMapConsistencyTests` gates must stay green; TD-010's `where` paths update to
  resolve (register test fails on stale paths).

## Risks / open questions

- Probe branch accumulation: one commit per spec run; acceptable volume, cleanup is
  a human runbook step.
- If GitHub ever changes last-pusher semantics, the acceptance spec fails first —
  that is the intended tripwire.
- Future repos must remember the per-repo checklist; the runbook is the mitigation,
  TD-010's closure does not depend on it.
