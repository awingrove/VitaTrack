# agent-identity — GitHub identity for agent sessions

Runbook for TD-010: setup, verification, rotation, and the per-new-repo
checklist for the machine GitHub identity every OpenCode session authenticates
as. GitHub-side steps (account, PAT, rulesets) are executed by the human in a
browser — the agent must never perform them with human credentials.

## What this is

Agent sessions authenticate to GitHub as the machine account
(`awingrove-opencode`), the human's keyring token is unreachable from agent
shells, and enforcement — the human approves every merge, the agent never
deletes or force-pushes — is server-side (GitHub rulesets), not convention.
The plugin at `.opencode/plugins/agent-identity.js` is OpenCode V2 shape
(v2.0.21: default export with `ctx.shell.hook('create.before')`); it injects
`GH_TOKEN` plus the machine `GIT_AUTHOR_*`/`GIT_COMMITTER_*` identity into
every shell a session runs, and fails closed to the sentinel
`__AGENT_TOKEN_FILE_MISSING_see_docs/factory/agent-identity.md` when the token
file is missing or unreadable. Spec:
`docs/superpowers/specs/2026-10-02-agent-github-identity-design.md`.

## One-time setup

Human-only, in the browser, once per machine (steps a–e) and once per repo
(step f). In order:

- **(a) Machine account** — create a second GitHub personal account (handle
  `awingrove-opencode`), enable 2FA, register a unique email (`+alias` on the
  human's address).
- **(b) Repo access** — invite the machine account as **collaborator (write)**
  on this repo; accept the invite from the machine account's email.
- **(c) Fine-grained PAT**, issued by the machine account:
  - Repositories: **all repositories the account can access** — a future repo
    then needs only the collaborator invite, no new token.
  - Permissions: **Contents read/write, Pull requests read/write, Workflows
    read/write** (Workflows is the fine-grained equivalent of the classic
    `workflow` scope; without it future `ci.yml` edits fail), **Metadata
    read**. No admin, no `delete_repo`.
  - Expiration: 1 year (max practical); rotation is below.
- **(d) Token file** — paste the PAT into `~/.config/opencode/gh-agent-token`
  (outside any repo, so it can never be committed), then `chmod 600` it.
- **(e) Identity constants** — edit `AGENT_GIT_NAME` / `AGENT_GIT_EMAIL` in
  `.opencode/plugins/agent-identity.js` to the machine account handle/email
  (shipped as `awingrove-opencode` /
  `awingrove-opencode@users.noreply.github.com`).
- **(f) Rulesets** — repo Settings → Rules → Rulesets, **bypass actors = the
  human account (`awingrove`) only**, enforcement **Active**, two rulesets:
  - targeting `main`: require pull request, require ≥1 approving review,
    dismiss stale approvals, require approval from someone other than the last
    pusher, block force pushes, block deletions.
  - targeting `*` (covers `main`, `feature/**`, and the repo's other branch
    prefixes): block deletions, block force pushes.

## Verify

```bash
GH_TOKEN="$(cat ~/.config/opencode/gh-agent-token)" gh api user   # → awingrove-opencode
```

- With the PAT, attempt approve (on the machine account's own PR), merge to
  `main` without an independent approval, and branch delete — every attempt
  must be rejected with a 4xx.
- In a **new** OpenCode session, `echo $GH_TOKEN` prints the token. Plugins in
  `.opencode/plugins/` auto-load at startup: a running session does not see a
  newly added plugin, only new sessions do.

## Failure signature

`gh` or `git push` failing 401 "Bad credentials" after a token-file problem
means the fail-closed sentinel fired — `echo $GH_TOKEN` prints
`__AGENT_TOKEN_FILE_MISSING_see_docs/factory/agent-identity.md` instead of a
token, and the plugin logged `agent-identity: cannot read token file ...` to
stderr at session start. The fix is restoring
`~/.config/opencode/gh-agent-token`, not issuing new credentials.

## Rotation

1. Issue a replacement PAT (same permissions as One-time setup (c)) and
   replace the contents of `~/.config/opencode/gh-agent-token` (keep mode
   `600`). The plugin reads the file per shell — the next shell picks it up,
   no OpenCode restart.
2. Rotate the CI secret `AGENT_GH_TOKEN` in the same pass, or the acceptance
   spec silently stops enforcing.

## Per-new-repo checklist

1. Copy `.opencode/plugins/agent-identity.js` to
   `~/.config/opencode/plugins/` (global coverage) or into the new repo's
   `.opencode/plugins/` (versioned with the repo).
2. Invite the machine account as collaborator (write); accept from its email.
3. Apply both rulesets from One-time setup (f), bypass = human account only.
4. Verify: `GH_TOKEN="$(cat ~/.config/opencode/gh-agent-token)" gh api user`
   resolves the machine account and repo access works — the PAT was issued for
   all repositories the account can access, so no re-issue is needed.

## Probe cleanup

Each acceptance run appends one commit to `feature/td010-probe` and opens one
PR (closed, not merged); the branch is deliberately persistent. The debris
accumulates — the human deletes the branch and closes stragglers when it
annoys. The agent never can: blocked deletions with human-only bypass is the
enforcement the spec proves.
