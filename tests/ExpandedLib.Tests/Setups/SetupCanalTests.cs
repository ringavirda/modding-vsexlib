#if GAME_GE_1_22
using System;
using System.IO;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>What the <c>advance</c> step and an <c>interact</c>'s <c>offHand</c> do to a molten cell
/// (setups.md, What the harness runs).</summary>
[Collection("Setups")]
public class SetupCanalTests
{
  private static readonly string FixturePath = Path.Combine(
    RepoPaths.Root,
    "tests",
    "ExpandedLib.Tests",
    "Setups",
    "Fixtures",
    "SetupCanal"
  );

  private static SetupScene Build(string steps) =>
    SetupScene.Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupcanal\"], \"steps\": "
          + steps
          + ", \"seconds\": 10, \"record\": {}}",
        "t.json"
      ),
      [FixturePath],
      null,
      Register
    );

  private static void Register(TestWorld w)
  {
    w.RegisterClass("SetupHoldCanalBlock", typeof(SetupHoldCanalBlock))
      .RegisterClass("SetupHoldCanal", typeof(SetupHoldCanal))
      .RegisterBlockEntityFactory("SetupHoldCanal", () => new SetupHoldCanal());
    w.RegisterItem("game:ingot-iron", 1500f);
    w.RegisterItem("game:metalbit-iron");
  }

  private static string Refused(string steps) =>
    Assert.Throws<InvalidOperationException>(() => Build(steps)).Message;

  private const string Canal =
    "{\"place\": {\"id\": \"canal\", \"code\": \"setupcanal:canal\", \"at\": [0, 0, 0]}}";
  private const string Plug =
    "{\"place\": {\"id\": \"plug\", \"code\": \"setupcanal:plug\", \"at\": [0, 0, 0]}}";
  private const string Chisel = "{\"stack\": \"setupcanal:chisel\"}";
  private const string Hammer = "{\"stack\": \"setupcanal:hammer\"}";

  private static SetupHoldCanal CanalAt(SetupScene scene, string id) =>
    Assert.IsType<SetupHoldCanal>(
      scene.World.GetBlockEntity(scene.PositionOf(id))
    );

  // Fails when advance leaves the calendar where it was, so a canal cell keeps its pour temperature.
  [Fact]
  public void Advance_MovesCalendarAndCoolsCanal()
  {
    double before;
    using (SetupScene still = Build("[" + Canal + "]"))
      before = still.World.Calendar.TotalHours;
    using SetupScene scene = Build(
      "[" + Canal + ", {\"advance\": {\"hours\": 0.5}}]"
    );
    SetupHoldCanal canal = CanalAt(scene, "canal");
    float poured = canal.Cell.CellTemperature;

    canal.Cell.UpdateThermal(scene.World.World);

    Assert.Equal(1600f, poured);
    Assert.Equal(0.5, scene.World.Calendar.TotalHours - before, 3);
    Assert.True(
      canal.Cell.CellTemperature < poured - 10f,
      $"{canal.Cell.CellTemperature}"
    );
  }

  // Fails when advance moves the calendar in one jump instead of ticking the block entities between,
  // so a 1000 ms listener runs other than once per game hour across 2.5 h (three chunks).
  [Fact]
  public void Advance_TicksTheBlockEntityListenerOncePerChunk()
  {
    using SetupScene scene = Build(
      "[" + Canal + ", {\"advance\": {\"hours\": 2.5}}]"
    );

    Assert.Equal(3, CanalAt(scene, "canal").Ticks);
  }

  // Fails when advance takes a negative span, which would run the calendar back.
  [Fact]
  public void Advance_of_negative_hours_fails_the_step()
  {
    string message = Refused("[{\"advance\": {\"hours\": -1}}]");

    Assert.Contains("steps[0]", message);
    Assert.Contains("hours -1 is negative", message);
  }

  // Fails when the off-hand slot is not set, so the chisel finds no hammer and the plug stays.
  [Fact]
  public void InteractWith_OffHandChiselClearsPlug()
  {
    using SetupScene scene = Build(
      "["
        + Plug
        + ", {\"interact\": {\"at\": {\"of\": \"plug\"}, \"with\": "
        + Chisel
        + ", \"offHand\": "
        + Hammer
        + ", \"returns\": true}}]"
    );

    SetupHoldCanal plug = CanalAt(scene, "plug");
    Assert.Equal(0, plug.Cell.CellAmount);
  }

  // Fails when the off-hand stack is left in the slot after the step, so the next click still holds it.
  [Fact]
  public void An_interact_empties_the_off_hand_after_the_click()
  {
    using SetupScene scene = Build(
      "["
        + Plug
        + ", {\"interact\": {\"at\": {\"of\": \"plug\"}, \"with\": "
        + Chisel
        + ", \"offHand\": "
        + Hammer
        + "}}]"
    );

    Assert.Null(scene.Player.OffHandSlot.Itemstack);
  }
}
#endif
