using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VitaTrack.ArchitectureTests;

/// <summary>
/// A plan that ships with unticked steps is stale session context (AGENTS.md
/// directive 5): the plan file is where a cold reader resumes the run, so an
/// unchecked box left behind by a completed run breaks the file's only job.
/// Scopes to plans changed on this branch (<c>origin/main...HEAD</c>) —
/// historical plans keep their state untouched, because a freeze whose first
/// act is retroactive compliance is not a freeze (new-shard.md). An empty diff
/// is correct semantics (main diffs empty by construction), not a rule that
/// selects nothing. Layer-2 precedent of <see cref="ClaimCommentTests"/>: reads
/// files as data, runs no other test. A failing git invocation is an error and
/// fails the build — never a skip, because skipping is vacuity.
/// </summary>
[TestClass]
public class PlanTickTests
{
    private const string UntickedMarker = "- [ ]";

    [TestMethod]
    public void Plans_Changed_On_This_Branch_Ship_With_No_Unticked_Steps()
    {
        var repoRoot = RepoLocator.Root();
        var changed = ChangedPlanFiles(repoRoot);

        var errors = new List<string>();
        foreach (var relativePath in changed)
        {
            var absolute = Path.Combine(repoRoot, relativePath);
            if (!File.Exists(absolute))
                continue; // deleted or renamed away on this branch — nothing ships

            var lines = File.ReadAllLines(absolute);
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith(UntickedMarker, StringComparison.Ordinal))
                    errors.Add($"{relativePath}:{i + 1}");
            }
        }

        Assert.AreEqual(0, errors.Count,
            "plan-tick failures — tick completed steps before the branch ships (AGENTS.md " +
            "directive 5); delete or annotate deliberately deferred steps:\n  - " +
            string.Join("\n  - ", errors));
    }

    /// <summary>
    /// <c>--diff-filter=d</c> drops deletions; a removed plan ships nothing to
    /// tick. Pathspecs restrict the diff to the two plan directories.
    /// </summary>
    private static IReadOnlyList<string> ChangedPlanFiles(string repoRoot)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("diff");
        startInfo.ArgumentList.Add("--name-only");
        startInfo.ArgumentList.Add("--diff-filter=d");
        startInfo.ArgumentList.Add("origin/main...HEAD");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("docs/plans");
        startInfo.ArgumentList.Add("docs/superpowers/plans");

        using var process = Process.Start(startInfo);
        Assert.IsNotNull(process, "Process.Start returned null for git diff");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode,
            $"git diff failed (exit {process.ExitCode}): {stderr.Trim()} — cannot evaluate " +
            "the plan-tick rule; an unevaluable check is an error, never a skip.");

        return stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
