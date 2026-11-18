using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ExpandedLib.Assembling;
using ExpandedLib.Testing;
using Newtonsoft.Json;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Xunit;
using Scene = ExpandedLib.Tests.HandAssemblyFixTests.Scene;

namespace ExpandedLib.Tests;

/// <summary>Fixes and take-offs driven from the client's hold through the packet bytes to the server, and where the bolts a joint spent end up.</summary>
[Collection(LangCollection.Name)]
public class HandAssemblyPacketPathTests {
  private const double HoldSeconds = 10;

  private static readonly BlockPos Cell = new(64, 16, 64, 0);
  private static readonly BlockPos Ground = new(64, 15, 64, 0);
  private static readonly BlockPos North = Cell.AddCopy(BlockFacing.NORTH);
  private static readonly BlockPos FarNorth = North.AddCopy(BlockFacing.NORTH);

  private static readonly Dictionary<string, string> EnglishEntries =
    JsonConvert.DeserializeObject<Dictionary<string, string>>(
      File.ReadAllText(
        Path.Combine(RepoPaths.Assets("exlib"), "lang", "en.json")
      )
    )!;

  private static string English(string key, object[] args) =>
    string.Format(EnglishEntries[key.Replace("exlib:", "")], args);

  private sealed class Rig {
    private Rig(Scene scene) => Scene = scene;

    public Scene Scene { get; }

    public static Rig Standing(int bolts) {
      var scene = Scene.Standing(["ns"]);
      scene.Bolts(bolts);
      return new Rig(scene);
    }

    public int Pieces =>
      Scene.Player.Hotbar.Sum(slot =>
        slot.Itemstack is { Block: { } block } stack
        && block.Code.Path.StartsWith("pipe-")
          ? stack.StackSize
          : 0
      );

    public int Spawned(Item item) =>
      Scene
        .World.Spawns.Where(s => s.Stack.Item == item)
        .Sum(s => s.Stack.StackSize);

    public void Fix(BlockPos aimed, BlockFacing face, EnumGameMode mode) {
      Scene.Player.GameMode = mode;
      Scene.Player.Hold(new ItemStack(Scene.Part("ns")));
      Scene.Player.Sneaking = true;
      var driver = new HandAssemblyControlTests.Driver(
        Scene,
        Scene.Select(aimed, face)
      ).Start();
      driver.Hold(HoldSeconds, sneak: true);

      AssemblyFix sent = Assert.IsType<AssemblyFix>(
        Assert.Single(driver.Channel.Sent)
      );
      AssemblyFix arrived = SerializerUtil.Deserialize<AssemblyFix>(
        SerializerUtil.Serialize(sent)
      );
      Assert.Null(
        HandAssemblyServer.Fix(Scene.World.World, Scene.Player.Player, arrived)
      );
    }

    /// <summary>The HUD line the control showed on the aim of the last take-off.</summary>
    public string? Line { get; private set; }

    public AssemblyResult? TakeOff(BlockPos pos, EnumGameMode mode) {
      Scene.Player.GameMode = mode;
      Scene.Player.Hold(new ItemStack(Scene.Wrench));
      Scene.Player.Sneaking = true;
      var driver = new HandAssemblyControlTests.Driver(
        Scene,
        Scene.Select(pos, BlockFacing.UP)
      ).Start();
      Line = driver.Control.TakeOffLine(
        Scene.World.World,
        Scene.Player.Player,
        English
      );
      driver.Hold(HoldSeconds, sneak: true);

      AssemblyTakeOff sent = Assert.IsType<AssemblyTakeOff>(
        Assert.Single(driver.Channel.Sent)
      );
      AssemblyTakeOff arrived = SerializerUtil.Deserialize<AssemblyTakeOff>(
        SerializerUtil.Serialize(sent)
      );
      Assert.Null(
        HandAssemblyServer.TakeOff(
          Scene.World.World,
          Scene.Player.Player,
          arrived,
          out AssemblyResult? done
        )
      );
      return done;
    }

    public void FitCap(BlockPos pos, BlockFacing face, EnumGameMode mode) {
      ExpectUnregisteredNetwork();
      Scene.Player.GameMode = mode;
      Scene.Player.Hold(new ItemStack(Scene.CapItem, 2));
      Assert.Null(
        HandAssemblyServer.FitCap(
          Scene.World.World,
          Scene.Player.Player,
          new AssemblyCap {
            X = pos.X,
            Y = pos.Y,
            Z = pos.Z,
            Face = face.Index,
            Slot = Scene.Player.ActiveNumber,
            CapCode = "test:cap-cast",
          }
        )
      );
    }

    public void TakeCapOff(BlockPos pos, BlockFacing face) {
      ExpectUnregisteredNetwork();
      Scene.Player.Hold(new ItemStack(Scene.Wrench));
      Scene.Player.Sneaking = false;
      Assert.Null(
        HandAssemblyServer.Cap(
          Scene.World.World,
          Scene.Player.Player,
          pos,
          new Vec3d(
            0.5 + 0.5 * face.Normali.X,
            0.5 + 0.5 * face.Normali.Y,
            0.5 + 0.5 * face.Normali.Z
          )
        )
      );
    }

    private void ExpectUnregisteredNetwork() =>
      Scene.World.Log.Expect(EnumLogType.Error, "declares network type");

    public List<string> Lines() =>
      [
        .. Scene
          .World.Log.Entries.Where(e =>
            e.Type == EnumLogType.Debug && e.Message.Contains("handassembly")
          )
          .Select(e => e.Message),
      ];

    public Rig Pair(EnumGameMode mode = EnumGameMode.Survival) {
      Fix(Ground, BlockFacing.UP, mode);
      Fix(Cell, BlockFacing.NORTH, mode);
      return this;
    }
  }

  // Fails when a survival take-off of the later piece through the packet does not give its joint's bolts back.
  [Fact]
  public void SurvivalFixThenTakeOff_LaterPieceBoltsLand() {
    Rig rig = Rig.Standing(10).Pair();
    Assert.Equal(8, rig.Scene.Carried);

    rig.TakeOff(North, EnumGameMode.Survival);

    Assert.Equal(10, rig.Scene.Carried);
    Assert.Equal(1, rig.Pieces);
    Assert.Equal(0, rig.Scene.World.GetBlock(North).Id);
  }

  // Fails when a survival take-off of the earlier piece does not give back the bolts the later piece holds toward it.
  [Fact]
  public void SurvivalFixThenTakeOff_EarlierPieceBoltsLand() {
    Rig rig = Rig.Standing(10).Pair();

    rig.TakeOff(Cell, EnumGameMode.Survival);

    Assert.Equal(10, rig.Scene.Carried);
    Assert.Equal(1, rig.Pieces);
    Assert.Null(rig.Scene.RecordAt(North).FastenerAt(BlockFacing.SOUTH));
  }

  // Fails when a stack the full pack cannot hold is not spawned at the player's feet.
  [Fact]
  public void SurvivalFixThenTakeOff_FullPackDropsAtFeet() {
    Rig rig = Rig.Standing(2).Pair();
    Assert.Equal(0, rig.Scene.Carried);
    for (int slot = 1; slot < 10; slot++)
      rig.Scene.Player.Hotbar[slot].Itemstack = new ItemStack(rig.Scene.Hammer);

    rig.TakeOff(North, EnumGameMode.Survival);

    Assert.Equal(0, rig.Scene.Carried);
    Assert.Equal(2, rig.Spawned(rig.Scene.Bolt));
    Assert.Single(
      rig.Scene.World.Spawns,
      s => s.Stack.Block?.Code.Path.StartsWith("pipe-") == true
    );
  }

  // Fails when a creative take-off of the earlier piece clears the later piece's record, so ClearRecordsToward runs under it.
  [Fact]
  public void CreativeTakeOffOfEarlier_LaterKeepsItsRecord() {
    Rig rig = Rig.Standing(10).Pair();

    rig.TakeOff(Cell, EnumGameMode.Creative);

    Assert.Equal(8, rig.Scene.Carried);
    Assert.NotNull(rig.Scene.RecordAt(North).FastenerAt(BlockFacing.SOUTH));
    Assert.Equal(
      2,
      rig.Scene.RecordAt(North).FastenerAt(BlockFacing.SOUTH)!.StackSize
    );
    rig.TakeOff(North, EnumGameMode.Survival);
    Assert.Equal(10, rig.Scene.Carried);
    Assert.Equal(1, rig.Pieces);
  }

  // Fails when a creative take-off of the later piece does not move its own record onto the earlier piece.
  [Fact]
  public void CreativeTakeOffOfLater_EarlierGetsTheRecord() {
    Rig rig = Rig.Standing(10).Pair();

    rig.TakeOff(North, EnumGameMode.Creative);

    Assert.Equal(8, rig.Scene.Carried);
    Assert.NotNull(rig.Scene.RecordAt(Cell).FastenerAt(BlockFacing.NORTH));
    Assert.Equal(
      2,
      rig.Scene.RecordAt(Cell).FastenerAt(BlockFacing.NORTH)!.StackSize
    );
    rig.TakeOff(Cell, EnumGameMode.Survival);
    Assert.Equal(10, rig.Scene.Carried);
    Assert.Equal(1, rig.Pieces);
  }

  // Fails when Place does not mark a creative fix's joint free, so the record is a plain absence the take-off cannot tell from a lost one.
  [Fact]
  public void CreativeFix_RecordsOwedNothing() {
    Rig rig = Rig.Standing(10).Pair(EnumGameMode.Creative);
    Assert.Equal(10, rig.Scene.Carried);
    Assert.True(rig.Scene.RecordAt(North).FreeAt(BlockFacing.SOUTH));
    Assert.True(rig.Scene.RecordAt(North).HasFree);
    rig.Scene.World.Reload(North);
    Assert.True(rig.Scene.RecordAt(North).FreeAt(BlockFacing.SOUTH));

    rig.TakeOff(North, EnumGameMode.Survival);

    Assert.Equal(10, rig.Scene.Carried);
    Assert.Equal(1, rig.Pieces);
  }

  // Fails when a survival joint is marked free too, or the free joint of a creative piece is counted as owed.
  [Fact]
  public void MixedJoints_ReturnOnlyTheSpent() {
    Rig rig = Rig.Standing(10).Pair();
    rig.Fix(North, BlockFacing.NORTH, EnumGameMode.Creative);
    Assert.Equal(8, rig.Scene.Carried);
    Assert.False(rig.Scene.RecordAt(North).FreeAt(BlockFacing.SOUTH));
    Assert.True(rig.Scene.RecordAt(FarNorth).FreeAt(BlockFacing.SOUTH));

    (var stacks, bool anyFree) = HandAssemblyServer.TakeOffReturns(
      rig.Scene.World.World.BlockAccessor,
      North
    );
    Assert.Equal(2, stacks.Sum(s => s.StackSize));
    Assert.True(anyFree);

    rig.TakeOff(North, EnumGameMode.Survival);

    Assert.Equal(10, rig.Scene.Carried);
    Assert.Equal(1, rig.Pieces);
  }

  // Fails when a fix against a face that holds a kept record overwrites that record, so a take-off returns fewer than were spent.
  [Fact]
  public void KeptRecordThenRefix_ReturnsBoth() {
    Rig rig = Rig.Standing(10).Pair();
    rig.TakeOff(Cell, EnumGameMode.Creative);

    rig.Fix(Ground, BlockFacing.UP, EnumGameMode.Survival);

    Assert.Equal(6, rig.Scene.Carried);
    Assert.Equal(
      2,
      rig.Scene.RecordAt(North).FastenerAt(BlockFacing.SOUTH)!.StackSize
    );
    Assert.Equal(
      2,
      rig.Scene.RecordAt(Cell).FastenerAt(BlockFacing.NORTH)!.StackSize
    );
    rig.TakeOff(Cell, EnumGameMode.Survival);
    Assert.Equal(10, rig.Scene.Carried);
    Assert.Equal(1, rig.Pieces);
  }

  // Fails when TakeOffLine words a case with another key, or the control shows no line on a wrench aim at a fixed piece (the client then hides it).
  [Fact]
  public void TakeOffHud_Lines() {
    IReadOnlyList<(string, int)> back = [("Bolt", 2), ("Cap", 1)];

    Assert.Equal(
      "Take off Pipe and 2 Bolt, 1 Cap",
      HandAssemblyHud.TakeOffLine(English, "Pipe", back, false, false)
    );
    Assert.Equal(
      "Take off Pipe, no fasteners: fixed in creative",
      HandAssemblyHud.TakeOffLine(English, "Pipe", [], true, false)
    );
    Assert.Equal(
      "Take off Pipe",
      HandAssemblyHud.TakeOffLine(English, "Pipe", [], false, false)
    );
    Assert.Equal(
      "Take off Pipe: nothing comes back in creative",
      HandAssemblyHud.TakeOffLine(English, "Pipe", back, true, true)
    );

    Rig rig = Rig.Standing(10).Pair();
    rig.TakeOff(North, EnumGameMode.Survival);
    Assert.NotNull(rig.Line);
    Assert.StartsWith("Take off ", rig.Line);
    Assert.Contains(" and 2 ", rig.Line);
  }

  // Fails when the HUD counts a piece's own records only, so it says less than a take-off gives for the earlier piece or the mixed joints.
  [Theory]
  [InlineData("later")]
  [InlineData("earlier")]
  [InlineData("mixed")]
  public void TakeOffHud_CountMatchesWhatTakeOffGives(string which) {
    Rig rig = Rig.Standing(10).Pair();
    if (which == "mixed")
      rig.Fix(North, BlockFacing.NORTH, EnumGameMode.Creative);
    BlockPos target = which == "earlier" ? Cell : North;
    int before = rig.Scene.Carried;

    rig.TakeOff(target, EnumGameMode.Survival);

    int gained = rig.Scene.Carried - before;
    Assert.Equal(2, gained);
    Assert.NotNull(rig.Line);
    Assert.Equal(
      gained,
      Regex.Matches(rig.Line!, @"\b\d+\b").Sum(m => int.Parse(m.Value))
    );
  }

  // Fails when the server sends no answer after a take-off, words it bare in survival, or its count differs from what reached the pack and the feet.
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void AfterTakeOff_AnswerNamesWhatCameBack(bool fullPack) {
    using Translating _ = new("uk");
    Rig rig = Rig.Standing(fullPack ? 2 : 10).Pair();
    rig.Scene.Player.ServerPlayer!.LanguageCode.Returns("uk");
    if (fullPack)
      for (int slot = 1; slot < 10; slot++)
        rig.Scene.Player.Hotbar[slot].Itemstack = new ItemStack(
          rig.Scene.Hammer
        );
    int before = rig.Scene.Carried;

    AssemblyResult? done = rig.TakeOff(North, EnumGameMode.Survival);

    Assert.NotNull(done);
    Assert.True(done!.Done);
    Assert.Equal("tookoff", done.Code);
    Assert.Equal(2, done.Args.Length);
    Match count = Regex.Match(
      (string)done.Args[1],
      @"handassembly-hud-stack\((\d+),"
    );
    Assert.True(count.Success);
    Assert.Equal(
      rig.Scene.Carried - before + rig.Spawned(rig.Scene.Bolt),
      int.Parse(count.Groups[1].Value)
    );
    Assert.Equal(2, int.Parse(count.Groups[1].Value));

    rig.Fix(Cell, BlockFacing.NORTH, EnumGameMode.Creative);
    AssemblyResult? bare = rig.TakeOff(North, EnumGameMode.Creative);
    Assert.Equal("tookoff-bare", bare!.Code);
    Assert.True(bare.Done);
    Assert.Single(bare.Args);
  }

  // Fails when a break loses a joint's bolts twice (spawns the record's spent bolts), or the neighbour's record toward the broken piece stays.
  [Fact]
  public void Break_LosesEachBrokenJointsBoltsOnce() {
    Rig rig = Rig.Standing(20).Pair();
    rig.Fix(North, BlockFacing.NORTH, EnumGameMode.Survival);
    rig.FitCap(FarNorth, BlockFacing.NORTH, EnumGameMode.Survival);
    Assert.Equal(14, rig.Scene.Carried);
    Assert.NotNull(rig.Scene.RecordAt(FarNorth).FastenerAt(BlockFacing.SOUTH));
    Assert.NotNull(rig.Scene.RecordAt(FarNorth).CapAt(BlockFacing.NORTH));
    Assert.NotNull(
      rig.Scene.RecordAt(FarNorth).CapFastenersAt(BlockFacing.NORTH)
    );

    rig.Scene.Player.GameMode = EnumGameMode.Survival;
    rig.Scene.World.Accessor.BreakBlock(North, rig.Scene.Player.Player);

    Assert.Equal(0, rig.Spawned(rig.Scene.Bolt));
    Assert.DoesNotContain(
      rig.Scene.World.Drops,
      drop => drop.Item == rig.Scene.Bolt
    );
    Assert.Equal(14, rig.Scene.Carried);
    BEBehaviorAssembled far = rig.Scene.RecordAt(FarNorth);
    Assert.Null(far.FastenerAt(BlockFacing.SOUTH));
    Assert.NotNull(far.CapAt(BlockFacing.NORTH));
    Assert.NotNull(far.CapFastenersAt(BlockFacing.NORTH));
    Assert.True(rig.Scene.RecordAt(Cell).Fixed);
  }

  // Fails when the event's line is missing (a dropped take-off or cap fit line), doubled, or lacks player, mode, piece, cell, joints or fasteners.
  [Theory]
  [InlineData("fix")]
  [InlineData("takeoff")]
  [InlineData("capfit")]
  [InlineData("captakeoff")]
  [InlineData("break")]
  [InlineData("refusal")]
  public void DebugLine_PerEvent(string kind) {
    Rig rig = Rig.Standing(10).Pair();
    int before = rig.Lines().Count;
    string player = rig.Scene.Player.Player.PlayerName;
    string piece = rig.Scene.World.GetBlock(North).Code.ToString();
    string expected;
    switch (kind) {
      case "fix":
        rig.Fix(North, BlockFacing.NORTH, EnumGameMode.Survival);
        piece = rig.Scene.World.GetBlock(FarNorth).Code.ToString();
        expected =
          $"[exlib] handassembly fix player={player} mode=survival piece={piece} cell=64,16,62 joints=south fasteners=-2 test:bolt";
        break;
      case "takeoff":
        rig.TakeOff(North, EnumGameMode.Survival);
        expected =
          $"[exlib] handassembly takeoff player={player} mode=survival piece={piece} cell=64,16,63 joints=south fasteners=+2 test:bolt";
        break;
      case "capfit":
        rig.FitCap(North, BlockFacing.NORTH, EnumGameMode.Survival);
        expected =
          $"[exlib] handassembly capfit player={player} mode=survival piece={piece} cell=64,16,63 joints=north fasteners=-2 test:bolt";
        break;
      case "captakeoff":
        rig.FitCap(North, BlockFacing.NORTH, EnumGameMode.Survival);
        before = rig.Lines().Count;
        rig.TakeCapOff(North, BlockFacing.NORTH);
        expected =
          $"[exlib] handassembly captakeoff player={player} mode=survival piece={piece} cell=64,16,63 joints=north fasteners=+1 test:cap-cast, +2 test:bolt";
        break;
      case "break":
        rig.Scene.Player.GameMode = EnumGameMode.Survival;
        rig.Scene.World.Accessor.BreakBlock(North, rig.Scene.Player.Player);
        expected =
          $"[exlib] handassembly break player={player} mode=survival piece={piece} cell=64,16,63 joints=south fasteners=-2 test:bolt";
        break;
      default:
        rig.Scene.Player.GameMode = EnumGameMode.Survival;
        rig.Scene.Player.Hold(new ItemStack(rig.Scene.Wrench));
        rig.Scene.Player.Sneaking = true;
        Assert.NotNull(
          HandAssemblyServer.TakeOff(
            rig.Scene.World.World,
            rig.Scene.Player.Player,
            new AssemblyTakeOff { X = 64, Y = 15, Z = 64 }
          )
        );
        piece = rig.Scene.World.GetBlock(Ground).Code.ToString();
        expected =
          $"[exlib] handassembly refusal player={player} mode=survival piece={piece} cell=64,15,64 joints=- fasteners=- code={HandAssemblyServer.NotFixed}";
        break;
    }

    Assert.Equal([expected], rig.Lines().Skip(before));
  }

  private sealed class Translating : IDisposable {
    private readonly string _locale;

    public Translating(string locale) {
      _locale = locale;
      TestLang.Init();
      var svc = Substitute.For<ITranslationService>();
      svc.LanguageCode.Returns(locale);
      svc.Get(Arg.Any<string>(), Arg.Any<object[]>())
        .Returns(ci =>
          ci.Arg<string>() + "(" + string.Join(",", ci.Arg<object[]>()) + ")"
        );
      svc.HasTranslation(Arg.Any<string>(), Arg.Any<bool>()).Returns(true);
      svc.HasTranslation(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
        .Returns(true);
      Lang.AvailableLanguages[locale] = svc;
    }

    public void Dispose() => Lang.AvailableLanguages.Remove(_locale);
  }
}
