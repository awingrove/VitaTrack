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

// Both plugin shapes funnel into injectAgentIdentity; tests drive it directly and
// drive the V2 default export's setup(ctx) with a stub ctx that hands back the
// registered hook, so what is exercised is the same path OpenCode v2 invokes.
async function loadInjection() {
  const mod = await import(PLUGIN_PATH);
  return mod;
}

// Under Playwright's CJS context the ESM named exports surface one level down
// (the module namespace is wrapped); OpenCode itself loads the file natively and
// sees the plain shape, so the test reaches through the wrapper to the same code.
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
  /**
   * Guards the last reference before binding — `mod.default` visibility differs
   * between Node-native ESM (plain shape) and Playwright's CJS interop (one
   * level deeper); demangle() resolves both to the same module object.
   */
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
    const { AGENT_GIT_NAME, AGENT_GIT_EMAIL, injectAgentIdentity } = await loadInjection();
    const tokenFile = writeFixtureTokenFile('ghp_fixture_token\n');

    const direct = { env: {} };
    process.env.AGENT_GH_TOKEN_FILE = tokenFile;
    injectAgentIdentity(direct.env);
    const viaHook = await runViaV1Hook(tokenFile);

    expectIdentityEnv(viaHook.env, 'ghp_fixture_token', AGENT_GIT_NAME, AGENT_GIT_EMAIL);
    expect(viaHook.env).toEqual(direct.env);
  });
});
