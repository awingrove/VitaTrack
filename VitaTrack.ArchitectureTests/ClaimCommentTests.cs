using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VitaTrack.ArchitectureTests;

/// <summary>
/// Enforces the claim vocabulary (AGENTS.md, Testing Philosophy): a comment in
/// the test project that claims what a test does must carry `CHECKS:` or `PINS:`
/// and be backed by an `Assert.` inside its enclosing method. This closes the
/// DL-006 class mechanically where it can — a claim with no assertion behind it —
/// by reading test source as data, the layer-2 precedent of
/// <see cref="StoryMapConsistencyTests"/>; no test here runs another test.
///
/// Scope, stated so the silence is visible: C# test sources only (Playwright
/// specs carry the vocabulary but stay review-enforced), and enclosing-method
/// only — a claim written inside a helper method cannot see the calling test's
/// assertions and must use `DOCUMENTS:` instead. A claim whose enclosing method
/// cannot be resolved is an error, never a skip: skipping is vacuity.
/// </summary>
[TestClass]
public class ClaimCommentTests
{
    private static readonly Regex ClaimKeyword = new(
        @"\b(CHECKS|PINS):", RegexOptions.Compiled);

    /// <summary>
    /// An indented, non-attribute declaration line carrying access/stack modifiers
    /// followed by a type, a name, and an open paren. Deliberately excludes field
    /// initializers (<c>= new(</c> never follows type-then-name) and attribute
    /// lines (<c>[InlineData(...)]</c>); a constructor line is not matched, which
    /// only widens a region — never narrows one past its claims.
    /// </summary>
    private static readonly Regex MethodSignatureLine = new(
        @"^\s+(?!\[)((public|private|protected|internal|static|async|unsafe|override|virtual|sealed|partial|new)\s+)+[\w\.\[\]<>\?,]+\s+\w+\s*\(",
        RegexOptions.Compiled);

    [TestMethod]
    public void Every_Claim_Comment_In_Test_Sources_Is_Backed_By_An_Assert_In_Its_Enclosing_Method()
    {
        var repoRoot = RepoLocator.Root();
        var scanRoot = Path.Combine(repoRoot, "VitaTrack.Tests");
        Assert.IsTrue(Directory.Exists(scanRoot), $"non-vacuity: scan root missing: {scanRoot}");

        var files = EnumerateTestSources(scanRoot).ToList();
        Assert.IsTrue(files.Count > 0,
            $"non-vacuity: zero .cs files scanned under {scanRoot} — the rule selects nothing.");

        var errors = new List<string>();
        var claimsFound = 0;

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            var signatureIndexes = SignatureIndexes(lines);
            var relativePath = Path.GetRelativePath(repoRoot, file);

            for (var i = 0; i < lines.Length; i++)
            {
                if (!ClaimKeyword.IsMatch(lines[i]))
                    continue;
                claimsFound++;

                var regionStart = LastSignatureAtOrBefore(signatureIndexes, i);
                if (regionStart < 0)
                {
                    errors.Add($"claim at {relativePath}:{i + 1} is outside a method — use DOCUMENTS:");
                    continue;
                }

                var regionEnd = NextSignatureAfter(signatureIndexes, i);
                if (!RegionHasAssert(lines, regionStart, regionEnd))
                    errors.Add($"claim at {relativePath}:{i + 1} has no Assert. in its enclosing method");
            }
        }

        Assert.IsTrue(claimsFound >= 1,
            "non-vacuity: no CHECKS:/PINS: claims found in VitaTrack.Tests — the rule selects nothing. " +
            "Convert a guarantee-comment to the vocabulary (AGENTS.md, Testing Philosophy).");
        Assert.AreEqual(0, errors.Count,
            "claim-vocabulary failures:\n  - " + string.Join("\n  - ", errors));
    }

    /// <summary>
    /// Test sources only: <c>bin</c>/<c>obj</c> hold generated .cs that no claim
    /// ever lives in. The exclusion mirrors every filesystem-scoped guard here.
    /// </summary>
    private static IEnumerable<string> EnumerateTestSources(string scanRoot)
        => Directory.EnumerateFiles(scanRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !HasBuildArtifactSegment(f))
            .OrderBy(f => f, StringComparer.Ordinal);

    private static bool HasBuildArtifactSegment(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(s => s is "bin" or "obj");
    }

    private static List<int> SignatureIndexes(string[] lines)
    {
        var indexes = new List<int>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (MethodSignatureLine.IsMatch(lines[i]))
                indexes.Add(i);
        }
        return indexes;
    }

    private static int LastSignatureAtOrBefore(List<int> signatureIndexes, int lineIndex)
    {
        var before = signatureIndexes.Where(s => s < lineIndex).ToList();
        return before.Count == 0 ? -1 : before[^1];
    }

    private static int NextSignatureAfter(List<int> signatureIndexes, int lineIndex)
    {
        var after = signatureIndexes.Where(s => s > lineIndex).ToList();
        return after.Count == 0 ? int.MaxValue : after[0];
    }

    private static bool RegionHasAssert(string[] lines, int regionStart, int regionEnd)
    {
        var end = Math.Min(regionEnd, lines.Length);
        for (var i = regionStart; i < end; i++)
        {
            if (lines[i].Contains("Assert.", StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
