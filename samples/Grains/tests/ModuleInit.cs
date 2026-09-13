using System.Runtime.CompilerServices;
using ExpandedLib.Testing;

namespace Grains.Tests;

/// <summary>
/// Registers the Vintage Story assembly resolver before any test type (which references the game
/// assemblies plus exlib/grains) is touched by the runner's reflection-based discovery, and
/// touches <see cref="GrainsModule"/> so its <c>[assembly: ExModule]</c> is loaded before
/// <c>ExModules</c> discovery runs.
/// </summary>
internal static class ModuleInit {
  [ModuleInitializer]
  internal static void Init() {
    VsAssemblyResolver.Register();
    TestLang.Init();
    _ = typeof(global::Grains.GrainsModule);
  }
}
