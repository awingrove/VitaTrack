using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VitaTrack.ArchitectureTests;

/// <summary>
/// Every absolute view path named in the Web layer resolves to a real Razor file, and
/// resolves to a file some slice actually claims.
/// <para>
/// This exists because of a defect that four unit tests and the e2e suite were all green
/// through: <c>ConnectionBadgeViewComponent</c> asserted its result's <em>shape</em> while
/// the page it renders on returned 500, because the view had landed in the
/// convention's <c>Views/Shared/Components/{Name}/Default.cshtml</c> — the allowlisted
/// directory, which is also exactly where the ownership check cannot see it. A component
/// whose tests all pass while the component is broken on the page is a test-shape problem
/// and a manifest problem at once; this is the cheap net for the first, and the
/// <c>Views/Shared</c> exclusion below is the net for the second.
/// </para>
/// <para>
/// Only <c>~/…</c> paths are in scope. A bare <c>View("Review")</c> is resolved by
/// convention from the controller's own folder, which is a different question; a path
/// spelled out in full is the one a rename can silently break, and it is the one this
/// codebase uses precisely to take a view out of convention's hands.
/// </para>
/// </summary>
[TestClass]
public class ViewPathTests
{
    private static readonly Regex AbsoluteViewPath = new(
        "\"~/(?<path>[^\"]+\\.cshtml)\"", RegexOptions.Compiled);

    /// <summary>Where a view component's convention puts its partial when the path is not
    /// named, and where a slice's markup stops being claimed: <c>shards.yaml</c>
    /// allowlists <c>Views/Shared/**</c>, so a view parked there satisfies every other
    /// check while belonging to nobody. Compared against the <em>web-root-relative</em>
    /// path the regex captures (<c>Views/Shared/…</c>), not the repo-relative one — a
    /// prefix that disagrees with the captured form never matches, and this check would
    /// pass on exactly the relocation it exists to catch.</summary>
    private const string UnclaimedViewRoot = "Views/Shared/";

    [TestMethod]
    public void AbsoluteViewPaths_ResolveToAClaimedRazorFile()
    {
        var webRoot = Path.Combine(RepoLocator.Root(), "VitaTrack.Web");
        var problems = new List<string>();

        foreach (var (declaredIn, viewPath) in DeclaredViewPaths(webRoot))
        {
            var resolved = Path.Combine(webRoot, viewPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(resolved))
                problems.Add($"{declaredIn}: view '{viewPath}' does not exist — the page renders this at runtime and 500s");
            else if (viewPath.StartsWith(UnclaimedViewRoot, StringComparison.Ordinal))
                problems.Add($"{declaredIn}: view '{viewPath}' is under Views/Shared, which is allowlisted rather than claimed; name a path a slice owns");
        }

        Assert.AreEqual(0, problems.Count,
            "absolute Razor view paths must resolve to a file a slice claims:\n  - " + string.Join("\n  - ", problems));
    }

    private static IEnumerable<(string DeclaredIn, string ViewPath)> DeclaredViewPaths(string webRoot)
    {
        foreach (var file in Directory.EnumerateFiles(webRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(segment => segment is "bin" or "obj"))
                continue;

            var relative = Path.GetRelativePath(RepoLocator.Root(), file).Replace('\\', '/');
            foreach (Match match in AbsoluteViewPath.Matches(File.ReadAllText(file)))
                yield return (relative, match.Groups["path"].Value);
        }
    }
}
