using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VitaTrack.ArchitectureTests;

/// <summary>
/// The one place the architecture tests learn where the repository root is.
/// Every filesystem-scoped guardrail needs it (shards.yaml, storymap.yaml, the
/// source tree); this walk existed as seven private copies, each carrying its own
/// `dir!` null-forgiving to silence a null the walk had already ruled out. One
/// helper, asserted where it fails instead of suppressed at the return.
/// </summary>
internal static class RepoLocator
{
    /// <summary>
    /// Walks up from the test assembly's directory to the directory holding
    /// <c>VitaTrack.sln</c>. Asserts rather than returning null, so callers get a
    /// non-nullable root and no caller needs a redundant guard.
    /// </summary>
    internal static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VitaTrack.sln")))
            dir = dir.Parent;

        Assert.IsNotNull(dir, "Could not locate repo root (VitaTrack.sln not found).");
        return dir.FullName;
    }
}
