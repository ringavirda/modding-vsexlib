#if GAME_GE_1_22
using System.IO;
using ExpandedLib.Catalogues;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>Which stations tick while a craft step holds a sequence step at one of them.</summary>
[Collection("Setups")]
public class SetupCraftTickTests
{
  private static readonly string FixturePath = Path.Combine(
    RepoPaths.Root,
    "tests",
    "ExpandedLib.Tests",
    "Setups",
    "Fixtures",
    "SetupCraft"
  );

  private const string Hammered =
    "{\"machine\": \"bench\", \"sequences\": [{\"recipe\": \"setupcraft:frame\", \"steps\": ["
    + "{\"tool\": \"setupcraft:mallet\", \"seconds\": 3}, "
    + "{\"tool\": \"setupcraft:mallet\", \"offhand\": \"setupcraft:stud\", \"seconds\": 3}]}]}";

  private static (int Crafted, int Other, int World) TicksAfterCraft(
    string stations
  )
  {
    Scenarios.CraftFixtureChest.Ticks = 0;
    Scenarios.CraftFixtureBench.Ticks = 0;
    int world = 0;
    CraftSequenceRegistry.Shared.Clear();
    using SetupScene scene = SetupScene.Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupcraft\"], \"steps\": ["
          + stations
          + ", {\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \"setupcraft:frame\"}}], "
          + "\"seconds\": 10, \"record\": {}}",
        "t.json"
      ),
      [FixturePath],
      w =>
      {
        CraftSequenceLoader.Load([
          ("setupcraft:config/craftsequences/bench.json", Hammered),
        ]);
        w.World.RegisterGameTickListener(_ => world++, 1000);
      },
      Scenarios.CraftStepTests.Register
    );
    CraftSequenceRegistry.Shared.Clear();
    return (
      Scenarios.CraftFixtureBench.Ticks,
      Scenarios.CraftFixtureChest.Ticks,
      world
    );
  }

  private const string Bench =
    "{\"place\": {\"id\": \"bench\", \"code\": \"setupcraft:seqbench\", \"at\": [0, 0, 0]}}";
  private const string Ticker =
    "{\"place\": {\"id\": \"other\", \"code\": \"setupcraft:chest\", \"at\": [3, 0, 0]}}";

  // Fails when a craft hold ticks every placed station instead of only the crafted one, or
  // ticks nothing (the crafted station's or the world's listener stays silent).
  [Fact]
  public void A_craft_hold_ticks_the_crafted_station_and_the_world_but_not_another_station()
  {
    var (crafted, other, world) = TicksAfterCraft(Bench + ", " + Ticker);

    Assert.Equal(0, other);
    Assert.Equal(4, crafted);
    Assert.Equal(4, world);
  }
}
#endif
