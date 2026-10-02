const { test, expect } = require('@playwright/test');
const fs = require('fs');
const os = require('os');
const path = require('path');

// The plugin under test lives outside this suite's tree: repo-root .opencode/plugins/.
const PLUGIN_PATH = path.resolve(__dirname, '../../../.opencode/plugins/agent-identity.js');

const originalTokenFileEnv = process.env.AGENT_GH_TOKEN_FILE;
const fixtureDirs = [];

function writeFixtureTokenFile(contents) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'agent-identity-'));
  fixtureDirs.push(dir);
  const file = path.join(dir, 'gh-agent-token');
  fs.writeFileSync(file, contents);
  return file;
}

// DOCUMENTS: both plugin shapes funnel into injectAgentIdentity; tests drive it
// directly and drive the V2 default export's setup(ctx) with a stub ctx that
// hands back the registered hook, so what is exercised is the same path
// OpenCode v2 invokes.
async function loadInjection() {
  const mod = await import(PLUGIN_PATH);
  return mod;
}

// DOCUMENTS: under Playwright's CJS context the ESM named exports surface one
// level down (the module namespace is wrapped); OpenCode itself loads the file
// natively and sees the plain shape, so the test reaches through the wrapper to
// the same code.
function demangle(mod) {
  return mod.default && mod.default.injectAgentIdentity ? mod.default : mod;
}

async function runViaSetup(tokenFilePath) {
  process.env.AGENT_GH_TOKEN_FILE = tokenFilePath;
  const mod = demangle(await loadInjection());
  let captured;
  const stubCtx = {
    shell: {
      hook: (name, fn) => {
        if (name === 'create.before') captured = fn;
      },
    },
  };
  // Guards the last reference before binding — `mod.default` visibility differs
  // between Node-native ESM (plain shape) and Playwright's CJS interop (one
  // level deeper); demangle() resolves both to the same module object.
  mod.default.setup(stubCtx);
  const event = { env: {} };
  await captured(event);
  return event;
}

async function runViaV1Hook(tokenFilePath) {
  process.env.AGENT_GH_TOKEN_FILE = tokenFilePath;
  const mod = await loadInjection();
  const hooks = await mod.AgentIdentityPlugin();
  const output = { env: {} };
  await hooks['shell.env']({ cwd: process.cwd() }, output);
  return output;
}

const expectIdentityEnv = (env, token, name, email) => {
  expect(env.GH_TOKEN).toBe(token);
  expect(env.GIT_AUTHOR_NAME).toBe(name);
  expect(env.GIT_COMMITTER_NAME).toBe(name);
  expect(env.GIT_AUTHOR_EMAIL).toBe(email);
  expect(env.GIT_COMMITTER_EMAIL).toBe(email);
};

test.afterEach(() => {
  if (originalTokenFileEnv === undefined) delete process.env.AGENT_GH_TOKEN_FILE;
  else process.env.AGENT_GH_TOKEN_FILE = originalTokenFileEnv;
  while (fixtureDirs.length > 0) fs.rmSync(fixtureDirs.pop(), { recursive: true, force: true });
});

test.describe('agent identity plugin (V2 shell.hook entrypoint)', () => {
  // DOCUMENTS: sets GH_TOKEN and the machine git identity from the token file
  test('DOCUMENTS: sets GH_TOKEN and the machine git identity from the token file', async () => {
    const { AGENT_GIT_NAME, AGENT_GIT_EMAIL } = await loadInjection();
    const tokenFile = writeFixtureTokenFile('ghp_fixture_token\n');

    const event = await runViaSetup(tokenFile);

    expectIdentityEnv(event.env, 'ghp_fixture_token', AGENT_GIT_NAME, AGENT_GIT_EMAIL);
  });

  // PINS: fails closed with a sentinel GH_TOKEN when the token file is missing
  test('PINS: fails closed with a sentinel GH_TOKEN when the token file is missing', async () => {
    const { SENTINEL_TOKEN } = await loadInjection();
    const missingPath = path.join(os.tmpdir(), `agent-identity-missing-${Date.now()}.token`);

    const event = await runViaSetup(missingPath);

    expect(event.env.GH_TOKEN).toBeDefined();
    expect(event.env.GH_TOKEN).not.toBe('');
    expect(event.env.GH_TOKEN).toBe(SENTINEL_TOKEN);
  });

  // PINS: fails closed with a sentinel GH_TOKEN when the token file is readable
  // but empty (or whitespace-only) — gh treats GH_TOKEN='' as unset, which would
  // fall through to the human keyring, so an empty file must not pass a blank
  // credential through.
  test('PINS: fails closed with a sentinel GH_TOKEN when the token file is empty', async () => {
    const { SENTINEL_TOKEN } = await loadInjection();
    const tokenFile = writeFixtureTokenFile(' \n');

    const event = await runViaSetup(tokenFile);

    expect(event.env.GH_TOKEN).toBe(SENTINEL_TOKEN);
  });

  // PINS: picks up a rotated token file without a restart
  test('PINS: picks up a rotated token file without a restart', async () => {
    const mod = await loadInjection();
    const tokenFile = writeFixtureTokenFile('token_one');

    const first = await runViaSetup(tokenFile);
    expect(first.env.GH_TOKEN).toBe('token_one');

    fs.writeFileSync(tokenFile, 'token_two');

    const second = await runViaSetup(tokenFile);
    expect(second.env.GH_TOKEN).toBe('token_two');
  });
});

test.describe('agent identity plugin (V1 named export stays in lockstep)', () => {
  // PINS: the V1 shape delegates to the same injection body, so the two
  // entrypoints cannot drift apart — the first env contract test runs both.
  test('PINS: V1 shell.env hook injects the identical env contract', async () => {
    const {
      AGENT_GIT_NAME,
      AGENT_GIT_EMAIL,
      AGENT_GH_CONFIG_DIR,
      injectAgentIdentity,
    } = await loadInjection();
    const tokenFile = writeFixtureTokenFile('ghp_fixture_token\n');

    const direct = { env: {} };
    process.env.AGENT_GH_TOKEN_FILE = tokenFile;
    injectAgentIdentity(direct.env);
    const viaHook = await runViaV1Hook(tokenFile);

    expectIdentityEnv(viaHook.env, 'ghp_fixture_token', AGENT_GIT_NAME, AGENT_GIT_EMAIL);
    expect(viaHook.env.GH_CONFIG_DIR).toBe(AGENT_GH_CONFIG_DIR);
    expect(viaHook.env).toEqual(direct.env);
  });
});

// ── GitHub enforcement half ─────────────────────────────────────────────────────
//
// DOCUMENTS: the plugin describes above prove what the plugin *injects*; the
// GitHub-enforcement tests below prove what GitHub itself *answers* when that
// identity is used. API-only (Node fetch against
// api.github.com — no browser, no app under test) because the enforcement under test
// lives server-side in GitHub's rulesets, not in this codebase. Nothing here is a
// double: real endpoint, real credentials, real answers (per AGENTS.md's no-mock
// rule this is the external-dependency shape — a dependency this app does not own).
//
// DOCUMENTS: the skip is decided from the environment alone, before any network call
// — the same shape as supplement-llm-integration.spec.js deciding its skip before the
// first page.goto. AGENT_GH_TOKEN absent (CI with no secret, local shell with no
// export) means there is no credential to present, so the half self-skips rather than
// failing for a reason nobody can act on; the reason below names the missing variable
// and says what this half pins. The plugin describes in this file still run either way.
const GH_REPO = process.env.AGENT_GH_REPO || 'awingrove/VitaTrack';
const GH_PROBE_BRANCH = 'feature/td010-probe';
const ghToken = process.env.AGENT_GH_TOKEN;
const ghSkipReason =
  'Skipping — AGENT_GH_TOKEN not set in the environment. This half pins GitHub-side enforcement '
  + 'of the agent identity (control login, [skip ci] probe commit, self-approve/merge/branch-delete/force-push '
  + 'rejections) against real api.github.com with the machine token, so without the token there is '
  + 'nothing to ask GitHub. Export AGENT_GH_TOKEN (CI: repo secret, same mechanism as LLM_API_KEY) '
  + 'to enforce it; the plugin tests in this file run regardless.';

async function githubRequest(pathname, init = {}) {
  return fetch(`https://api.github.com${pathname}`, {
    ...init,
    headers: {
      Authorization: `Bearer ${ghToken}`,
      Accept: 'application/vnd.github+json',
      'X-GitHub-Api-Version': '2022-11-28',
      // GitHub's API requires a User-Agent; Node's fetch does not send one on its own.
      'User-Agent': 'VitaTrack-agent-identity-spec',
      ...(init.body ? { 'Content-Type': 'application/json' } : {}),
      ...(init.headers || {}),
    },
  });
}

// DOCUMENTS: the probe branch's real arrival path. The contents API does NOT create a
// missing ref — a PUT with a branch param against a nonexistent branch answers 422 — so
// the branch is bootstrapped here first: absent → created from main's current HEAD via
// POST /git/refs (Contents RW permits ref creation on a feature branch; the runbook's
// rulesets block deletion and force-push, not creation); present → nothing to do.
// Called from test 2, so it lives in the skipped-by-default half: no ref is ever
// touched while AGENT_GH_TOKEN is absent.
async function bootstrapProbeBranch() {
  const refRes = await githubRequest(`/repos/${GH_REPO}/git/ref/heads/${GH_PROBE_BRANCH}`);
  if (refRes.status === 404) {
    const mainRes = await githubRequest(`/repos/${GH_REPO}/git/ref/heads/main`);
    expect(mainRes.ok, `GET main ref answered ${mainRes.status}`).toBe(true);
    const mainSha = (await mainRes.json()).object.sha;
    const createRes = await githubRequest(`/repos/${GH_REPO}/git/refs`, {
      method: 'POST',
      body: JSON.stringify({ ref: `refs/heads/${GH_PROBE_BRANCH}`, sha: mainSha }),
    });
    expect(createRes.ok, `POST git/refs (bootstrap) answered ${createRes.status}`).toBe(true);
    return;
  }
  expect(refRes.ok, `GET probe ref answered ${refRes.status}`).toBe(true);
}

// Shared across the serial group: what test 1 learned about the token's identity and
// what test 2 learned about the PR it opened, so later tests address the PR test 2
// actually created rather than a literal.
let agentLogin = null;
let probePullNumber = null;

test.describe('agent GitHub enforcement (real GitHub)', () => {
  // Serial, not a style choice: tests 2–6 share the PR number — test 2 creates the PR
  // and the last three attack it. Under fullyParallel they could start before the PR
  // exists and fail for a reason that has nothing to do with enforcement.
  test.describe.configure({ mode: 'serial' });

  test.skip(!ghToken, ghSkipReason);

  // PINS: the token resolves to the machine account and never to the human — this is
  // the control every later assertion stands on, so it runs before anything else and
  // proves the credential under test is not the human's. AGENT_GH_LOGIN tightens it
  // from "some account that is not the human" to the exact machine account when set.
  test('the agent authenticates as the machine account, not the human', async () => {
    const res = await githubRequest('/user');
    expect(res.ok, `GET /user answered ${res.status}`).toBe(true);
    const user = await res.json();

    expect(user.login).toBeTruthy();
    expect(user.login).not.toBe('awingrove');
    if (process.env.AGENT_GH_LOGIN) expect(user.login).toBe(process.env.AGENT_GH_LOGIN);
    agentLogin = user.login;
  });

  // PINS: one small commit per run to the persistent probe branch, and every probe
  // commit carries [skip ci] so spec runs do not spawn workflow runs (minutes + noise).
  // The branch's real arrival path: bootstrapped from main's HEAD when absent
  // (bootstrapProbeBranch — the contents API cannot create a ref itself), then aimed
  // at by this contents PUT through the branch param — no local git, never force-pushed
  // (the runbook's rulesets block force-push), never deleted (test 5 pins the
  // rejection). The PR opened here is the one tests 3–6 attack: its number is kept for
  // them, and its author is checked against the identity the control test verified.
  test('every probe commit carries [skip ci]', async () => {
    await bootstrapProbeBranch();

    const stamp = new Date().toISOString();
    const putRes = await githubRequest(
      `/repos/${GH_REPO}/contents/agent-probe/${Date.now()}.txt`,
      {
        method: 'PUT',
        body: JSON.stringify({
          message: `[skip ci] agent identity probe ${stamp}`,
          content: Buffer.from(`agent identity probe ${stamp}\n`).toString('base64'),
          branch: GH_PROBE_BRANCH,
        }),
      },
    );
    expect(putRes.ok, `contents PUT answered ${putRes.status}`).toBe(true);
    const created = await putRes.json();
    expect(created.commit.message).toContain('[skip ci]');

    const prRes = await githubRequest(`/repos/${GH_REPO}/pulls`, {
      method: 'POST',
      body: JSON.stringify({
        title: `Agent identity probe ${stamp}`,
        head: GH_PROBE_BRANCH,
        base: 'main',
        body: 'Acceptance-spec probe PR (TD-010). Closed by the spec after the rejection tests.',
      }),
    });
    expect(prRes.ok, `POST pulls answered ${prRes.status}`).toBe(true);
    const pull = await prRes.json();
    expect(pull.user.login).toBeTruthy();
    // Guarded so this test still runs solo (-g on its title); in the normal serial path
    // the control ran first and the PR author must be the very identity it verified.
    if (agentLogin) expect(pull.user.login, 'probe PR author').toBe(agentLogin);
    probePullNumber = pull.number;
  });

  // PINS: GitHub rejects the agent approving its own PR server-side — the DL-003-class
  // guarantee that approval must come from an identity other than the author. Status
  // varies with how the rejection is reached (403 ruleset/permission, 422 own-PR rule,
  // 405 verb handling), so the pin is on the rejection, not on one code: NOT ok, with
  // the observed status recorded in the message.
  test('the agent cannot approve its own PR', async () => {
    expect(probePullNumber, 'probe PR missing — test 2 must run first (serial group)').not.toBeNull();

    const res = await githubRequest(`/repos/${GH_REPO}/pulls/${probePullNumber}/reviews`, {
      method: 'POST',
      body: JSON.stringify({ event: 'APPROVE' }),
    });
    expect(
      res.ok,
      `APPROVE review answered ${res.status} (a rejection — 403/422/405 — is the pinned outcome)`,
    ).toBe(false);
  });

  // PINS: merging the agent's own PR into main without an independent approval is
  // rejected server-side. If this ever returns ok the merge already happened — the
  // runbook's "require approval from someone other than the last pusher" rule is the
  // thing that moved, and a red spec here is the tripwire by design.
  test('the agent cannot merge to main without independent approval', async () => {
    expect(probePullNumber, 'probe PR missing — test 2 must run first (serial group)').not.toBeNull();

    const res = await githubRequest(`/repos/${GH_REPO}/pulls/${probePullNumber}/merge`, {
      method: 'PUT',
      body: JSON.stringify({}),
    });
    expect(
      res.ok,
      `PUT merge answered ${res.status} (a rejection — 403/422/405 — is the pinned outcome)`,
    ).toBe(false);
  });

  // PINS: the replay of DL-003 — deleting a feature branch with agent credentials — is
  // rejected by GitHub itself (ruleset deletion rule), not merely discouraged. The
  // probe branch is deliberately left standing after the run; that persistence is what
  // this rejection exists to guarantee, and the runbook owns occasional cleanup of its
  // accumulated commits.
  // DOCUMENTS: in the serial run, branch existence is guaranteed by the bootstrap in
  // test 2 — a 404 here can no longer mean "the branch was never there", so a not-ok
  // answer is GitHub refusing the delete, not a nonexistent-ref pass-through. A solo
  // -g run skips the bootstrap, so this states a precondition rather than asserting.
  test('the agent cannot delete a feature branch', async () => {
    const res = await githubRequest(`/repos/${GH_REPO}/git/refs/heads/${GH_PROBE_BRANCH}`, {
      method: 'DELETE',
    });
    expect(
      res.ok,
      `DELETE ref answered ${res.status} (a rejection — 403/422/405 — is the pinned outcome)`,
    ).toBe(false);
  });

  // PINS: force-updating a protected branch with agent credentials is rejected
  // server-side by the ruleset's block-force-pushes rule (human-only bypass) —
  // the history-rewrite counterpart to the deletion rejection above. The attempt
  // must be a genuine rewind: the ref is read, its tip commit's PARENT sha is
  // fetched, and the PATCH targets that — a same-sha PATCH with force:true is a
  // 200 no-op update and pins nothing (the live run proved exactly that). The
  // pin is on the rejection, with the observed status recorded in the message
  // (status varies by how the ruleset answers).
  test('the agent cannot force-push a protected branch', async () => {
    const refRes = await githubRequest(`/repos/${GH_REPO}/git/refs/heads/${GH_PROBE_BRANCH}`);
    expect(refRes.ok, `GET probe ref answered ${refRes.status} (force-push precondition)`).toBe(
      true,
    );
    const tipSha = (await refRes.json()).object.sha;

    const commitRes = await githubRequest(`/repos/${GH_REPO}/commits/${tipSha}`);
    expect(
      commitRes.ok,
      `GET probe tip commit answered ${commitRes.status} (force-push precondition)`,
    ).toBe(true);
    const parents = (await commitRes.json()).parents;
    const parentSha = parents[0] && parents[0].sha;
    expect(parentSha, 'probe tip commit has no parent — nothing to rewind to').toBeTruthy();

    const res = await githubRequest(`/repos/${GH_REPO}/git/refs/heads/${GH_PROBE_BRANCH}`, {
      method: 'PATCH',
      body: JSON.stringify({ force: true, sha: parentSha }),
    });
    expect(
      res.ok,
      `PATCH force rewind answered ${res.status} (a rejection — 403/422 — is the pinned outcome)`,
    ).toBe(false);
  });

  // DOCUMENTS: closing the probe PR is permitted (closing stays a human-and-agent
  // right; only approve/merge/delete/force-push are pinned as rejections), and the PR is closed
  // here regardless of which of tests 3–6 failed so the next run's test 2 does not
  // trip over a stale open PR. The probe branch itself is left in place on purpose —
  // test 5's rejection is the thing that guarantees it stays.
  test.afterAll(async () => {
    if (probePullNumber == null) return;
    const res = await githubRequest(`/repos/${GH_REPO}/pulls/${probePullNumber}`, {
      method: 'PATCH',
      body: JSON.stringify({ state: 'closed' }),
    });
    if (!res.ok) console.log(`afterAll: closing probe PR #${probePullNumber} answered ${res.status}`);
  });
});
