using ExpandedLib.Registries;
using ExpandedLib.Testing;

namespace ExpandedLib.Tests;

/// <summary>Registers this test assembly under the mod id <see cref="Id"/> through
/// <see cref="EntityRegistry.RegisterAll"/>, as a mod's <c>Start</c> registers its own, so
/// <c>Class&lt;T&gt;()</c>, <c>Behavior&lt;T&gt;()</c> and <see cref="EntityRegistry.KeyFor"/>
/// key its types under <see cref="Id"/>.</summary>
/// <remarks>The assembly declares no <c>[assembly: ExDomain]</c>; until a class registers it, a key
/// for one of its types takes the caller's domain and warns. Registering also discovers the
/// assembly's definitions and contributors into <c>ExDefinitions</c>.</remarks>
internal static class TestModDomain {
  internal const string Id = "test";

  internal static void Register() {
    var world = new TestWorld();
    world.Mods.Add(Id, "1.0.0");
    EntityRegistry.RegisterAll(
      world.Api,
      world.Mods.GetMod(Id)!,
      typeof(TestModDomain).Assembly
    );
  }
}
