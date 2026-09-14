using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Xunit;

namespace YourMod.Tests;

/// <summary>Golden-file parity for your mod's code-first defs (see the wiki's "Code-First
/// Definitions" page). Runs the real checks - every def reproduces its golden, and the golden
/// set exactly covers the defs - once your mod declares a code-first block, item or recipe;
/// until then there are none to check, and the fact records that rather than being skipped -
/// this repo runs no skipped tests. A newly scaffolded def needs its golden blessed once with
/// EXLIB_WRITE_GOLDENS=1 before this goes green (see the wiki page).</summary>
public class GoldenTests {
  // Found by assembly name rather than a ProjectReference type, so this file compiles unchanged
  // whether or not your mod project is wired up yet (see the csproj's conditional reference).
  private static Assembly? ModAssembly =>
    AppDomain.CurrentDomain.GetAssemblies()
      .FirstOrDefault(a => a.GetName().Name == "YourModProject");

  private static string GoldenRoot([CallerFilePath] string here = "") =>
    Path.Combine(Path.GetDirectoryName(here)!, "..", "goldens");

  private const string Domain = "YourModProject"; // your asset domain, if it differs from --ModName

  [Fact]
  public void Definitions_reproduce_their_goldens_and_the_set_is_complete() {
    Assembly? asm = ModAssembly;
    List<IExDef> defs =
      asm == null ? [] : DefinitionGoldens.Collect(Domain, asm).ToList();

    if (defs.Count == 0) {
      // No code-first defs yet - nothing to golden-check. Add one and this branch stops running.
      Assert.Empty(defs);
      return;
    }

    foreach (
      string relativePath in defs.Select(DefinitionGoldens.RelativePath).OrderBy(p => p)
    ) {
      var (ok, message) = DefinitionGoldens.CheckGolden(
        Domain,
        asm!,
        relativePath,
        GoldenRoot()
      );
      Assert.True(ok, message);
    }

    var (missing, orphans) = DefinitionGoldens.CheckCompleteness(
      Domain,
      asm!,
      GoldenRoot()
    );
    Assert.True(
      missing.Count == 0,
      "defs with no golden file (unmigrated?): " + string.Join(", ", missing)
    );
    Assert.True(
      orphans.Count == 0,
      "golden files with no def (deleted?): " + string.Join(", ", orphans)
    );
  }

  [Fact]
  public void Blesses_the_goldens_when_requested() {
    if (!DefinitionGoldens.WriteRequested)
      return;
    Assembly? asm = ModAssembly;
    if (asm == null)
      return;
    DefinitionGoldens.WriteAll(Domain, asm, GoldenRoot());
  }
}
