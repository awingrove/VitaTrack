using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VitaTrack.ArchitectureTests;

/// <summary>
/// The debt register's file shape: rule 6 of the set enforced by
/// <see cref="TechnicalDebtRegisterTests"/>, split into its own class because it reads
/// the file as raw text rather than as a parsed node, and because the split keeps both
/// types clear of the 300-line trigger.
/// </summary>
/// <remarks>
/// Lives here, not in <c>TechnicalDebtRegisterTests</c>, for the same reason the rule does:
/// the other five validate a parsed <c>YamlMappingNode</c> and this validates the file's
/// bytes. Folding them together would conflate a schema violation with a formatting one,
/// and adding this one inline pushed the class to 309 lines — over the split trigger, so
/// the guardrail that guards the guardrail tests caught it.
/// </remarks>
[TestClass]
public class DebtRegisterShapeTests
{
    // A key opening a folded block scalar: four spaces of key indent, then `key: >-`.
    private static readonly Regex FoldedKeyPattern = new(@"^ {4}(?<key>\w+): >-\s*$", RegexOptions.Compiled);

    /// <summary>
    /// Rule 6 — every folded scalar (<c>&gt;-</c>) in the register has its content lines at
    /// exactly six spaces: keys sit at four, so six is the only correct content indent.
    /// </summary>
    /// <remarks>
    /// This checks the <em>shape</em> of the file, not its meaning. It catches a malformed
    /// register — a hand-edited scalar whose content drifted off the six-space grid — and it
    /// runs independently of the parser so a bad indent is reported as an indent problem
    /// rather than surfacing as an opaque YamlDotNet message from another test.
    ///
    /// It does **not** catch a silently truncated field, and this remark exists to stop a
    /// later reader assuming it does. That failure was measured, not assumed: a 5-space
    /// continuation is a <c>ParserError</c> and a 4-space cut is a <c>ScannerError</c>, so an
    /// indent error is already loud without this rule. And a *dropped* continuation line —
    /// the genuinely silent case — leaves valid YAML with a shorter field, with no structural
    /// signal at all. Two content thresholds were measured and both fail: a flat 40-character
    /// per-field floor false-positives on 7 legitimate fields (the shortest is
    /// <c>TD-009.where</c> at 24), and a 200-character per-entry volume floor catches zero
    /// real truncations, because all 23 entries absorb a one-line drop. Tightening the volume
    /// floor to 217 would catch it on the tightest entry but leaves 44 characters of margin,
    /// so the first legitimately terse entry breaks the build.
    ///
    /// So truncation is undetectable by a threshold that does not also fail on healthy data.
    /// The honest mitigations are review at the point of edit — the register's header comment
    /// documents the six-space grid — and not pretending otherwise. It is a separate
    /// [TestMethod] because it reads the file as text rather than as a parsed node.
    /// </remarks>
    [TestMethod]
    public void Folded_Scalars_Indent_Their_Content_At_Six_Spaces()
    {
        var repoRoot = RepoLocator.Root();
        var path = Path.Combine(repoRoot, "docs", "factory", "technical-debt.yaml");
        var lines = File.ReadAllLines(path);

        var errors = new List<string>();
        var foldedKeys = 0;
        string? pendingKey = null;
        var pendingKeyLine = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var keyMatch = FoldedKeyPattern.Match(line);
            if (keyMatch.Success)
            {
                pendingKey = keyMatch.Groups["key"].Value;
                pendingKeyLine = i + 1;
                foldedKeys++;
                continue;
            }

            if (pendingKey is null || line.Length == 0) continue;

            var indent = line.Length - line.TrimStart().Length;
            if (indent <= 4)
            {
                pendingKey = null;
                continue;
            }

            if (indent != 6)
                errors.Add($"{Path.GetFileName(path)}:{i + 1}: folded scalar '{pendingKey}' "
                    + $"(declared at line {pendingKeyLine}) has content at {indent} spaces, expected 6.");
        }

        // Completeness, not just non-emptiness. FoldedKeyPattern pins four spaces of key
        // indent, so a scalar declared at a top-level or nested indent would fall outside
        // the pattern and stop being inspected — while `foldedKeys > 0` still passed on the
        // 115 it did match. Counting the `>-` markers in the file catches that: the counts
        // must agree, so a new indent level is a red build rather than a silent blind spot.
        // Comment lines are skipped: the register's own header explains the `>-` convention,
        // so a naive text count would be permanently off by one.
        var declaredScalars = lines.Count(l =>
            !l.TrimStart().StartsWith('#') && l.Contains(">-", StringComparison.Ordinal));

        // Completeness is asserted first and separately. A nested scalar trips the indent
        // check too, and `Assert.AreEqual` short-circuits — so with indent first, the
        // completeness failure would never be reported and the "am I inspecting
        // everything?" question would stay unanswerable from the output.
        Assert.AreEqual(declaredScalars, foldedKeys,
            $"{path} declares {declaredScalars} folded scalar(s) but the rule matched {foldedKeys}. "
            + "FoldedKeyPattern expects a key at four spaces, so a scalar at another indent is not "
            + "being checked. Widen the pattern, or state why the extra declaration is not a scalar.");
        Assert.IsTrue(foldedKeys > 0,
            $"no folded scalar found in {path} — the rule is not inspecting anything, so it would pass vacuously.");
        Assert.AreEqual(0, errors.Count,
            "technical-debt register indentation failures:\n  - " + string.Join("\n  - ", errors));
    }
}
