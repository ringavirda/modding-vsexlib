using System.Runtime.CompilerServices;
using ExpandedLib.Testing;

namespace BurdenMaker.Tests;

/// <summary>
/// Registers the Vintage Story assembly resolver before any test type (which references the game
/// assemblies plus exlib/burdenmaker) is touched by the runner's reflection-based discovery.
/// </summary>
internal static class ModuleInit {
  [ModuleInitializer]
  internal static void Init() {
    VsAssemblyResolver.Register();
    TestLang.Init();
    // The two hoppers read the shared material-role registry; seed the sample's roles (the headless
    // stand-in for materialroles.json) so those predicates resolve.
    MaterialRoleSeeds.SeedDefaults();
  }
}
