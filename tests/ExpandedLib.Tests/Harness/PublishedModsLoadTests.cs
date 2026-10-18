#if GAME_GE_1_22
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The shipped ppex and smex assemblies, built before the current exlib, compiled against this exlib's
/// public surface.
/// </summary>
public class PublishedModsLoadTests
{
  private static string? ReleaseFolder()
  {
    for (
      var dir = new DirectoryInfo(AppContext.BaseDirectory);
      dir != null;
      dir = dir.Parent
    )
    {
      string candidate = Path.Combine(
        dir.FullName,
        "exmods-legacy",
        "dist",
        "Releases",
        "1.22.0"
      );
      if (Directory.Exists(candidate))
        return candidate;
    }
    return null;
  }

  private static Assembly Extract(string zip, string dll, string into)
  {
    using var archive = ZipFile.OpenRead(zip);
    archive
      .Entries.Single(e => e.Name == dll)
      .ExtractToFile(Path.Combine(into, dll));
    return Assembly.LoadFrom(Path.Combine(into, dll));
  }

  private static int Compile(Assembly assembly)
  {
    int compiled = 0;
    foreach (Type type in assembly.GetTypes())
    {
      if (type.ContainsGenericParameters)
        continue;
      foreach (
        MethodBase method in type.GetMethods(
            BindingFlags.Instance
              | BindingFlags.Static
              | BindingFlags.Public
              | BindingFlags.NonPublic
              | BindingFlags.DeclaredOnly
          )
          .Cast<MethodBase>()
          .Concat(
            type.GetConstructors(
              BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly
            )
          )
      )
      {
        if (method.IsAbstract || method.ContainsGenericParameters)
          continue;
        RuntimeHelpers.PrepareMethod(method.MethodHandle);
        compiled++;
      }
    }
    return compiled;
  }

  // Fails when a public member the published ppex or smex calls is renamed or removed in exlib: compiling
  // the assembly's methods throws a missing-member error. A no-op when the legacy line's release zips are
  // not beside the checkout.
  [Fact]
  public void Published_ppex_and_smex_compile_against_this_exlib()
  {
    string? releases = ReleaseFolder();
    if (releases == null)
      return;
    string into = Directory.CreateTempSubdirectory("published-mods").FullName;
    try
    {
      string ppex = Directory.GetFiles(releases, "ppex_*.zip").Single();
      string smex = Directory.GetFiles(releases, "smex_*.zip").Single();

      int ppexMethods = Compile(
        Extract(ppex, "PipesAndPowerExpanded.dll", into)
      );
      int smexMethods = Compile(Extract(smex, "SteelmakingExpanded.dll", into));

      Assert.True(ppexMethods > 100, $"ppex compiled {ppexMethods} methods");
      Assert.True(smexMethods > 100, $"smex compiled {smexMethods} methods");
    }
    finally
    {
      Directory.Delete(into, recursive: true);
    }
  }
}
#endif
