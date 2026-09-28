using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VitaTrack.ArchitectureTests;

/// <summary>
/// The database is the only source of connection state. Configuration binding is
/// gone, and this is the guard that stops it returning quietly: a
/// <c>VitaTrackOptions</c> type, a <c>Configure&lt;VitaTrackOptions&gt;</c> call, a
/// <c>VitaTrack</c> config block or a <c>VitaTrack__ApiKey</c> export can each come
/// back in a way that compiles and passes review — the type is a plain class, the
/// config block is inert JSON, the env var is read by a shell script — and each one
/// is a second place the credential lives, which is the thing this slice removed.
/// <para>
/// The scan covers the three source roots, and every exclusion is a decision rather
/// than an inheritance. <c>docs/plans/</c> and <c>docs/superpowers/</c> are dated
/// records of the decision itself and quote the old names on purpose — 37 of the 38
/// hits a repo-root scan reports live there. The root <c>AGENTS.md</c> is the
/// exclusion that is temporary: it names <c>VitaTrackOptions</c> at line 58, prose
/// saying the binding is gone and a spelling a reintroducer would type, so naming the
/// repo root in <see cref="ScanRoots"/> today would make this guard red over a
/// documentation fix another task owns. The root's other files — <c>README.md</c>,
/// <c>run.sh</c> — carry no token and would pass, and a repo-root scan names the file
/// and line of each, which is the list to work through before that root is added.
/// <para>
/// Five of the six tokens — every one except <c>VitaTrackOptions</c> itself, which the
/// line above accounts for — and the JSON section key now occur nowhere outside that
/// excluded documentation. The one that was, and where it was, is the part worth keeping.
/// <c>VitaTrack__ApiKey</c> appeared at <c>AGENTS.md</c>:98 and twice more, on the skip
/// predicate of the real-provider e2e spec: one dead operand naming a variable the app
/// stopped reading, and one line of CI prose telling a reader to add a repo secret under
/// that same dead name. All three are gone, and both places now name the variables the
/// spec actually reads. The count falling is the least interesting fact about it — every
/// one of those hits was a sentence showing the next author how to bring the binding
/// back.
/// </para>
/// <para>
/// One token here is a JSON section key rather than one of the colon-form config
/// paths, because the colon form is invisible to this scan: <c>appsettings.json</c>
/// holds <c>"VitaTrack": { "ApiKey": ... }</c>, which no amount of grepping for
/// <c>VitaTrack:ApiKey</c> will find. Without that token the block's removal would be
/// an unverified step in the diff, and a future block would be free to reappear.
/// It is therefore matched only in <c>.json</c> files, where it means the section
/// key, and never in code — where the same quotes are just a string. The trailing
/// colon is load-bearing, not decoration: unanchored, the token also matches
/// <c>package-lock.json</c>'s <c>"name": "VitaTrack"</c> value, which is this
/// project's own name and has nothing to do with a connection.
/// </para>
/// </summary>
[TestClass]
public class ConfigBindingAbsentTests
{
    /// <summary>The roots whose source cannot carry connection configuration.</summary>
    private static readonly string[] ScanRoots =
    {
        "VitaTrack.Core", "VitaTrack.Web", "VitaTrack.Tests",
    };

    /// <summary>
    /// Every way this configuration named itself, kept as a list rather than one
    /// pattern because they are separate spellings and a reintroducer picks one of
    /// them. <c>VitaTrackOptions</c> subsumes <c>IOptions&lt;VitaTrackOptions&gt;</c>,
    /// so the first entry is redundant by design rather than by accident.
    /// <para>
    /// Four of these six had <em>no</em> pre-deletion location inside the three scan
    /// roots: the binding named the section once and read the leaves through
    /// <c>VitaTrackOptions</c>, so <c>VitaTrack:ApiKey</c> and its siblings never
    /// appeared as text in code, an <c>appsettings</c> reader or a doc line here. They
    /// are kept because <c>IConfiguration["VitaTrack:ApiKey"]</c> is exactly what a
    /// reintroducer writes, and a guard has to be able to go red on a spelling it has
    /// never seen. Dropping one because nothing in the repository says it today would
    /// leave the spelling most likely to be typed unguarded.
    /// </para>
    /// </summary>
    private static readonly string[] ForbiddenTokens =
    {
        "IOptions<VitaTrackOptions>",
        "VitaTrackOptions",
        "VitaTrack:ApiKey",
        "VitaTrack:BaseUrl",
        "VitaTrack:Model",
        "VitaTrack__ApiKey",
    };

    /// <summary>The <c>"VitaTrack":</c> section key, which is what an
    /// <c>appsettings</c> file actually contains. Checked in <c>.json</c> only, and
    /// anchored on the colon so a <c>"VitaTrack"</c> appearing as a value — this
    /// repository's own name in <c>package-lock.json</c> — is not a violation.</summary>
    private const string JsonSectionToken = "\"VitaTrack\":";

    private static readonly string[] ScannedExtensions =
    {
        ".cs", ".cshtml", ".md", ".yaml", ".yml", ".json", ".js", ".sh",
    };

    /// <summary>Build output, test-run artifacts and agent-tool scratch, by directory
    /// name at any depth. Mirrors <c>RetiredNamespaceTests.NotScannedDirectoryNames</c>.</summary>
    private static readonly HashSet<string> NotScannedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".claude", ".git", ".opencode", ".playwright", ".superpowers",
        "bin", "node_modules", "obj", "playwright-report", "test-results", "TestResults",
    };

    /// <summary>Files the scan never reads.</summary>
    private static readonly string[] NotScannedFiles =
    {
        // A grep test has to contain the literal it greps for; excluding it by name
        // means a rename cannot quietly leave the scan arming itself against itself.
        nameof(ConfigBindingAbsentTests) + ".cs",
    };

    /// <summary>
    /// Floor on the number of files the scan reads, so an over-broad exclusion cannot
    /// turn this into a rule that passes by seeing nothing. Roughly 209 files are
    /// scanned today (the count moves with untracked local files), so 150 permits
    /// ordinary churn; below that, the scan is broken rather than the repository
    /// shrinking, and this says so.
    /// </summary>
    private const int MinScannedFiles = 150;

    [TestMethod]
    public void No_Source_File_Binds_The_Deleted_VitaTrack_Configuration()
    {
        var violations = new List<string>();
        var scanned = ScannableFiles();

        foreach (var (file, relative) in scanned)
        {
            var isJson = Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase);
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var token = MatchedToken(lines[i], isJson);
                if (token is not null)
                {
                    violations.Add($"{relative}:{i + 1}: '{token}' — {lines[i].Trim()}");
                }
            }
        }

        Assert.IsTrue(
            scanned.Count >= MinScannedFiles,
            $"The scan read {scanned.Count} file(s), below the floor of {MinScannedFiles}. A guardrail that "
            + "stops reading files stops guarding; check the exclusion lists before assuming the code is clean.");

        Assert.AreEqual(
            0,
            violations.Count,
            "Source still binding the deleted connection configuration. The database is the only source of "
            + "connection state: a type, a Configure<> call, a config block or an env var here is a second "
            + "home for the credential, and each of the four compiles and passes review on its own.\n  "
            + string.Join("\n  ", violations));
    }

    /// <summary>The first forbidden token on the line, or <c>null</c> if it carries none.</summary>
    private static string? MatchedToken(string line, bool isJson)
    {
        foreach (var token in ForbiddenTokens)
        {
            if (line.Contains(token, StringComparison.Ordinal)) return token;
        }

        if (isJson && line.Contains(JsonSectionToken, StringComparison.Ordinal)) return JsonSectionToken;

        return null;
    }

    private static List<(string File, string Relative)> ScannableFiles()
    {
        var repoRoot = RepoLocator.Root();
        var files = new List<(string, string)>();

        foreach (var root in ScanRoots)
        {
            var rootPath = Path.Combine(repoRoot, root);
            if (!Directory.Exists(rootPath))
            {
                Assert.Fail($"Scan root '{root}' does not exist; the guard is reading nothing and is not guarding.");
            }

            foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
            {
                if (!ScannedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;

                var relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                if (IsNotScanned(relative)) continue;

                files.Add((file, relative));
            }
        }

        return files;
    }

    private static bool IsNotScanned(string relative)
    {
        var segments = relative.Split('/');
        if (segments.Any(segment => NotScannedDirectoryNames.Contains(segment))) return true;

        var fileName = segments[^1];
        return NotScannedFiles.Any(name => fileName.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
