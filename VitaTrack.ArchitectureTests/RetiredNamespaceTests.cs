using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VitaTrack.ArchitectureTests;

/// <summary>
/// TD-015. The flat <c>VitaTrack.Core/Services</c> directory and the
/// <c>VitaTrack.Core.Services</c> namespace were retired on purpose. They were
/// deleted, then named as a place code lives in four live docs; TD-007 was
/// closed for exactly that class and the same text came back. A doc that points
/// at them sends the next reader to reinvent a layout that was removed, and it
/// makes "docs must not lie" unreviewable because review already passed on the
/// same words once.
///
/// This is a total rule: every scanned file must be free of the names except
/// for the spans listed below, each anchored to the one sentence that earns it.
/// Two of those spans are the AGENTS.md prohibitions that keep the namespace
/// from coming back - flagging them would invert the entry they serve.
/// </summary>
[TestClass]
public class RetiredNamespaceTests
{
    private static readonly string[] RetiredNames =
    {
        "VitaTrack.Core.Services",
        "VitaTrack.Core/Services",
    };

    private static readonly string[] ScannedExtensions =
    {
        ".cs", ".cshtml", ".md", ".yaml", ".yml", ".json", ".js", ".sh",
    };

    /// <summary>
    /// Build output, test-run artifacts and agent-tool scratch, by directory name at
    /// any depth. The artifact names mirror <c>ShardOwnershipTests.ExcludeDirs</c>;
    /// the dotted ones are the same class of untracked scratch as
    /// <c>.superpowers</c>. The generated dashboard needs no entry: <c>.html</c> is
    /// not a scanned extension, so <c>docs/factory/dashboard.html</c> is out of scope
    /// by the extension filter rather than by a special case.
    /// </summary>
    private static readonly HashSet<string> NotScannedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".claude", ".git", ".opencode", ".playwright", ".superpowers",
        "bin", "node_modules", "obj", "playwright-report", "test-results", "TestResults",
    };

    /// <summary>Dated records of decisions taken. Historical, and immutable by policy.</summary>
    private static readonly string[] NotScannedPathPrefixes =
    {
        "docs/plans/",
        "docs/superpowers/",
    };

    /// <summary>
    /// Files the scan never reads. An entry matches either the repo-relative path or
    /// the bare file name, so a file's move between directories does not silently
    /// re-arm the scan against it.
    /// </summary>
    private static readonly (string Match, string Why)[] NotScannedFiles =
    {
        ("docs/factory/technical-debt.yaml", "the register quotes the retired name in TD-006, TD-015 and TD-019"),
        (nameof(RetiredNamespaceTests) + ".cs", "a grep test has to contain the literal it greps for"),
    };

    /// <summary>
    /// Live files allowed to name the retired namespace, each pinned to one sentence.
    /// A mention anywhere else in the same file is still a violation, so an entry
    /// cannot become a whole-file amnesty.
    /// </summary>
    private static readonly (string Path, string Fragment)[] PermittedMentions =
    {
        ("AGENTS.md", "namespace and directory are **retired**"),
        ("VitaTrack.Core/AGENTS.md", "the flat `VitaTrack.Core/Services` namespace are retired"),
        ("VitaTrack.Core/AGENTS.md", "There is no `VitaTrack.Core.Services` (retired)"),
        ("docs/factory/shard-metrics.yaml", "ServicesLayeringTests still filters namespace"),
    };

    /// <summary>
    /// Hard ceiling on the allow-list, so it cannot absorb future mentions. It is
    /// four today: three retirement prohibitions and one ledger narrative. A fifth
    /// is not a fifth harmless citation, it is a fifth place that has to be read, so
    /// the ceiling fails the build and forces that read.
    /// </summary>
    private const int MaxPermittedMentions = 4;

    /// <summary>
    /// Floor on the number of files the scan reads, so an over-broad exclusion
    /// cannot turn this into a rule that passes by seeing nothing. 288 files are
    /// scanned today — 286 tracked, the two-file delta being gitignored local files
    /// (`package-lock.json`, `appsettings.Production.json`), so the count moves with
    /// untracked local files exactly as `ConfigBindingAbsentTests` records for its own
    /// floor. 200 therefore permits 88 files of churn; below that, the scan is
    /// broken rather than the repository shrinking, and this says so.
    /// </summary>
    private const int MinScannedFiles = 200;

    [TestMethod]
    public void No_Live_File_References_The_Retired_Core_Services_Namespace()
    {
        var violations = new List<string>();
        var scanned = ScannableFiles();

        foreach (var (file, relative) in scanned)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!NamesARetiredNamespace(lines[i])) continue;
                if (IsPermitted(relative, lines[i])) continue;
                violations.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
            }
        }

        Assert.IsTrue(
            scanned.Count >= MinScannedFiles,
            $"The scan read {scanned.Count} file(s), below the floor of {MinScannedFiles}. A guardrail that "
            + "stops reading files stops guarding; check the exclusion lists before assuming the repo is clean.");

        Assert.IsTrue(
            PermittedMentions.Length <= MaxPermittedMentions,
            $"The allow-list has {PermittedMentions.Length} entries, over the ceiling of {MaxPermittedMentions}. "
            + "Each addition is a live file that names the retired namespace, and needs a human to read it.");

        Assert.AreEqual(
            0,
            violations.Count,
            "Live files naming the retired VitaTrack.Core.Services namespace. The directory and namespace were "
            + "retired on purpose; name where the code lives now, or name the namespace only in a sentence that "
            + "says it is retired.\n  " + string.Join("\n  ", violations));
    }

    private static List<(string File, string Relative)> ScannableFiles()
    {
        var repoRoot = RepoLocator.Root();
        var files = new List<(string, string)>();

        foreach (var file in Directory.EnumerateFiles(repoRoot, "*", SearchOption.AllDirectories))
        {
            if (!ScannedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;

            var relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            if (IsNotScanned(relative)) continue;

            files.Add((file, relative));
        }

        return files;
    }

    private static bool IsNotScanned(string relative)
    {
        var segments = relative.Split('/');
        if (segments.Any(segment => NotScannedDirectoryNames.Contains(segment))) return true;
        if (NotScannedPathPrefixes.Any(prefix => relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            return true;

        var fileName = segments[^1];
        return NotScannedFiles.Any(entry =>
            relative.Equals(entry.Match, StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(entry.Match, StringComparison.OrdinalIgnoreCase));
    }

    private static bool NamesARetiredNamespace(string line) =>
        RetiredNames.Any(name => line.Contains(name, StringComparison.Ordinal));

    private static bool IsPermitted(string relative, string line) =>
        PermittedMentions.Any(entry =>
            relative.Equals(entry.Path, StringComparison.OrdinalIgnoreCase)
            && line.Contains(entry.Fragment, StringComparison.Ordinal));
}
