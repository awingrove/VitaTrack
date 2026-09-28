// A real OpenAI-compatible service, in a real listening socket.
//
// The app is not modified in any way to make these tests possible: it reads its
// connection from the database, dials the base URL a user typed into the Settings
// form, and parses whatever comes back. What stands in here is the *other* side of
// that conversation — a dependency the app does not own. Statuses are real statuses,
// the JSON is the shape the providers actually use, and the request that arrives is
// recorded whole so a test can assert on the credential and the settings that
// travelled with it.
//
// What it deliberately does NOT do is answer 200 to everything. An unknown path is a
// 404, which is what makes a probe for the wrong path fail rather than quietly
// verify, and it is why the "verified" assertions in the spec mean something: a stub
// that says yes to anything would make them pass while proving nothing.
//
// Ephemeral and per-test, not a second `webServer` entry: `fullyParallel` shares a
// fixed port across every worker, and recorded requests would leak between parallel
// tests. Port 0 lets the OS pick, so two workers never collide.

const http = require('http');

const MODELS_PATH = '/v1/models';
const COMPLETIONS_PATH = '/v1/chat/completions';

const DEFAULT_MODELS = ['stub-model-a', 'stub-model-b'];

// What the app's own parser expects back (SupplementLabelParser.ParseNutrients): a
// `nutrients` array of objects with those three names, and an optional
// `swapSuggestion`. Two named, unmistakable nutrients, so an assertion on the editor
// cannot be satisfied by a row that came from somewhere else.
const DEFAULT_COMPLETION = JSON.stringify({
  nutrients: [
    { genericName: 'Stub Vitamin C', specificForm: 'Ascorbic Acid', dosage: '80mg' },
    { genericName: 'Stub Zinc', specificForm: 'Zinc Picolinate', dosage: '15mg' },
  ],
  swapSuggestion: null,
});

/**
 * Starts the stub on an ephemeral loopback port.
 *
 * @param {{ models?: string[] }} [options] `models: []` serves a 200 carrying an empty
 *   catalog — the negative case, where the status is a success and the answer is still
 *   not a model list, so the app has to treat the connection as unverified.
 * @returns {Promise<{ baseUrl: string, close: () => Promise<void>, requests: object[] }>}
 */
async function startLlmStub(options = {}) {
  const models = options.models ?? DEFAULT_MODELS;
  const requests = [];

  const server = http.createServer((req, res) => {
    const chunks = [];
    req.on('data', chunk => chunks.push(chunk));
    req.on('end', () => {
      const raw = Buffer.concat(chunks).toString('utf8');
      const path = req.url.split('?')[0];

      // Recorded before answering, and whole: a test that asserts on the credential or
      // the model needs the request as it arrived, not as a summary of it.
      requests.push({
        method: req.method,
        path,
        headers: { ...req.headers },
        body: raw,
      });

      if (req.method === 'GET' && path === MODELS_PATH) {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({
          object: 'list',
          data: models.map(id => ({ id, object: 'model', owned_by: 'stub' })),
        }));
        return;
      }

      if (req.method === 'POST' && path === COMPLETIONS_PATH) {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({
          object: 'chat.completion',
          choices: [{
            index: 0,
            message: { role: 'assistant', content: DEFAULT_COMPLETION },
            finish_reason: 'stop',
          }],
        }));
        return;
      }

      // Anything else. The 404 is load-bearing in both directions: a probe for the
      // wrong path fails instead of verifying, and a completion posted to the wrong
      // place fails instead of returning nutrients.
      res.writeHead(404, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ error: { message: 'no such path', type: 'not_found' } }));
    });
  });

  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', resolve);
  });

  return {
    baseUrl: `http://127.0.0.1:${server.address().port}`,
    // Sockets first, then the listener. The app's pooled HttpClient holds keep-alive
    // connections open for its lifetime, so closing the listener alone would wait for
    // them and hang the worker rather than releasing the port.
    close: async () => {
      server.closeAllConnections();
      await new Promise(resolve => server.close(resolve));
    },
    requests,
  };
}

/**
 * A loopback port with nothing listening on it — the "connection refused" case, which
 * no HTTP server can be asked to produce. Racy by nature (the port is free again the
 * moment this returns), which is why a test using it asserts on what the app *rendered*
 * rather than on what was sent: nothing was sent, because there was nowhere to send it.
 */
async function reserveClosedPort() {
  const server = http.createServer();
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const { port } = server.address();
  await new Promise(resolve => server.close(resolve));
  return port;
}

module.exports = { startLlmStub, reserveClosedPort, MODELS_PATH, COMPLETIONS_PATH };
