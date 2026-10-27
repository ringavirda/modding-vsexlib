#if GAME_GE_1_22
using System.IO;
using ExpandedLib.Industry.Metals;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>A setup's load fills <see cref="MetalRegistry"/> from the listed mods' <c>config/metals</c> as a running server does (setups.md, Loading).</summary>
[Collection("Setups")]
public class SetupMetalLoadTests
{
  // Fails when the setup load leaves the registry unfilled: the metal then resolves to the game's ingot by convention.
  [Fact]
  public void A_setup_load_registers_the_listed_mods_metals()
  {
    using SetupScene scene = SetupScene.Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupmetal\"], \"steps\": [], \"seconds\": 10, \"record\": {}}",
        "t.json"
      ),
      [
        Path.Combine(
          RepoPaths.Root,
          "tests",
          "ExpandedLib.Tests",
          "Setups",
          "Fixtures",
          "SetupMetal"
        ),
      ],
      null,
      _ => { }
    );

    Assert.Equal(
      "setupmetal:ingot-fixturealloy",
      MetalRegistry.MoltenItemOf("fixturealloy").ToString()
    );
  }
}
#endif
