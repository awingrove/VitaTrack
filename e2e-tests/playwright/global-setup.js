// global-setup.js
// Web server runs in-memory SQLite (Data Source=:memory:, kept alive for the
// process lifetime by ServiceCollectionExtensions.AddInfra). No file cleanup
// needed — the DB is destroyed when the dotnet process exits. This hook stays
// as a no-op placeholder so playwright.config.js `globalSetup` has something
// to point at; cheap to re-add file cleanup if a future env switches back to
// file-backed SQLite.
module.exports = async () => {
    console.log('In-memory SQLite — no file cleanup needed');
};