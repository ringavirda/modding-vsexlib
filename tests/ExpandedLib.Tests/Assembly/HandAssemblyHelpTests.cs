using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Assembling;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The interaction help of hand assembly: the held part's fixing line shows the tool, a fixed piece lists its take-off and a capped end its cap take-off once with the wrench in hand but no rotate, and the HUD's lines carry no gesture words.</summary>
public class HandAssemblyHelpTests {
  private static readonly BlockPos Cell = new(64, 16, 64, 0);
  private static readonly string[] Axis = ["ns", "we", "ud"];

  private const string Spec =
    """{"handAssembly":{"tool":"test:wrench-*","fastener":"test:bolt","perJoint":4,"cap":"test:cap-cast"}}""";

  private static readonly string[] GestureWords =
  [
    "hold",
    "sneak",
    "click",
    "shift",
    "button",
  ];

  #region Held help

  // Fails when the fixing line's Itemstacks are left null instead of the spec's tool.
  [Fact]
  public void HeldHelp_FixLineShowsTheTool() {
    var scene = Scene.Standing(fixedPiece: false);
    var behavior = new BlockBehaviorHandAssembly(scene.Block);
    behavior.OnLoaded(scene.World.Api);
    var handling = EnumHandling.PassThrough;

    WorldInteraction[] help = behavior.GetHeldInteractionHelp(
      scene.Player.ActiveSlot,
      ref handling
    );

    WorldInteraction fix = Assert.Single(
      help,
      w => w.ActionLangCode == ExlibLang.BlockhelpHandassemblyFix
    );
    Assert.Equal(scene.Wrench, Assert.Single(fix.Itemstacks).Collectible);
    Assert.Null(
      Assert
        .Single(
          help,
          w => w.ActionLangCode == ExlibLang.BlockhelpHandassemblyStep
        )
        .Itemstacks
    );
  }

  #endregion

  #region Placed help

  // Fails when the take-off line is listed without the Fixed check.
  [Fact]
  public void PlacedHelp_TakeOffOnlyOnAFixedPiece() {
    var fixedScene = Scene.Standing(fixedPiece: true);
    WorldInteraction takeOff = Assert.Single(
      fixedScene.Help(BlockFacing.UP),
      w => w.ActionLangCode == ExlibLang.BlockhelpHandassemblyTakeoff
    );
    Assert.Equal(EnumMouseButton.Right, takeOff.MouseButton);
    Assert.Equal("shift", takeOff.HotKeyCode);
    Assert.Equal(
      fixedScene.Wrench,
      Assert.Single(takeOff.Itemstacks).Collectible
    );

    Assert.DoesNotContain(
      Scene.Standing(fixedPiece: false).Help(BlockFacing.UP),
      w => w.ActionLangCode == ExlibLang.BlockhelpHandassemblyTakeoff
    );
    Assert.DoesNotContain(
      Scene.Standing(fixedPiece: true, spec: null).Help(BlockFacing.UP),
      w => w.ActionLangCode == ExlibLang.BlockhelpHandassemblyTakeoff
    );
  }

  // Fails when the cap take-off is listed whenever the piece has any cap, whatever end is aimed.
  [Fact]
  public void PlacedHelp_CapOffOnlyAtACappedEnd() {
    var scene = Scene.Standing(fixedPiece: true);
    scene.Record.SetCap(BlockFacing.NORTH, new ItemStack(scene.Cap), null);
    scene.Player.Hold(new ItemStack(scene.Wrench));

    WorldInteraction[] north = scene
      .Help(BlockFacing.NORTH)
      .Where(w => w.ActionLangCode == ExlibLang.BlockhelpHandassemblyCapoff)
      .ToArray();

    WorldInteraction capOff = Assert.Single(north);
    Assert.Equal(EnumMouseButton.Right, capOff.MouseButton);
    Assert.Equal(scene.Wrench, Assert.Single(capOff.Itemstacks).Collectible);
    Assert.DoesNotContain(
      scene.Help(BlockFacing.SOUTH),
      w => w.ActionLangCode == ExlibLang.BlockhelpHandassemblyCapoff
    );
  }

  // Fails when the cap take-off is listed once per mouse button, so twice for a piece with two capped ends and the wrench in hand.
  [Fact]
  public void PlacedHelp_NoLineIsListedTwice() {
    var scene = Scene.Standing(fixedPiece: true);
    scene.Record.SetCap(BlockFacing.NORTH, new ItemStack(scene.Cap), null);
    scene.Record.SetCap(BlockFacing.SOUTH, new ItemStack(scene.Cap), null);
    scene.Player.Hold(new ItemStack(scene.Wrench));

    foreach (BlockFacing aim in new[] { BlockFacing.NORTH, BlockFacing.SOUTH })
      Assert.Equal(
        1,
        scene
          .Help(aim)
          .Count(w => w.ActionLangCode == ExlibLang.BlockhelpHandassemblyCapoff)
      );
    WorldInteraction[] help = scene.Help(BlockFacing.NORTH);
    Assert.Equal(
      help.Length,
      help.Select(w => (w.ActionLangCode, w.MouseButton)).Distinct().Count()
    );
  }

  // Fails when the wrench-held check is removed, so a pipe in the main hand still lists the cap take-off.
  [Fact]
  public void PlacedHelp_NoCapOffWithoutTheWrenchInHand() {
    var scene = Scene.Standing(fixedPiece: true);
    scene.Record.SetCap(BlockFacing.NORTH, new ItemStack(scene.Cap), null);
    scene.Player.Hold(new ItemStack(scene.Cap));

    Assert.DoesNotContain(
      scene.Help(BlockFacing.NORTH),
      w => w.ActionLangCode == ExlibLang.BlockhelpHandassemblyCapoff
    );
  }

  // Fails when the rotate line is kept on a fixed piece.
  [Fact]
  public void PlacedHelp_NoRotateOnAFixedPiece() {
    Assert.Contains(
      Scene.Standing(fixedPiece: false).Help(BlockFacing.UP),
      w => w.ActionLangCode == "exlib:blockhelp-rotate"
    );
    Assert.DoesNotContain(
      Scene.Standing(fixedPiece: true).Help(BlockFacing.UP),
      w => w.ActionLangCode == "exlib:blockhelp-rotate"
    );
  }

  #endregion

  #region HUD

  // Fails when a handassembly-hud value of en.json names a gesture or a button.
  [Fact]
  public void HudLines_NameNoGesture() {
    var entries = JsonConvert.DeserializeObject<Dictionary<string, string>>(
      File.ReadAllText(
        Path.Combine(RepoPaths.Assets("exlib"), "lang", "en.json")
      )
    )!;

    string[] named =
    [
      .. entries
        .Where(e => e.Key.StartsWith("handassembly-hud-"))
        .Where(e =>
          GestureWords.Any(w =>
            e.Value.Contains(w, StringComparison.OrdinalIgnoreCase)
          )
        )
        .Select(e => $"{e.Key}: {e.Value}"),
    ];
    Premise.NotEmpty(
      entries.Keys.Where(k => k.StartsWith("handassembly-hud-")),
      "handassembly-hud lang keys"
    );
    Assert.True(named.Length == 0, string.Join("\n", named));
  }

  #endregion

  private sealed class Scene {
    private Scene(TestWorld world, BlockNetworkNode block) {
      World = world;
      Block = block;
      Player = world.Player();
      Player.StandAt(Cell, BlockFacing.SOUTH);
      Wrench = world.RegisterItem("test:wrench-iron");
      world.RegisterItem("test:bolt");
      Cap = world.RegisterItem("test:cap-cast");
    }

    public TestWorld World { get; }
    public TestPlayer Player { get; }
    public BlockNetworkNode Block { get; }
    public Item Wrench { get; }
    public Item Cap { get; }
    public BEBehaviorAssembled Record { get; private set; } = null!;

    public static Scene Standing(bool fixedPiece, string? spec = Spec) {
      var world = new TestWorld();
      world.RegisterNetwork("test", sys => new StubNetwork(sys));
      TestNetworkBlock[] family = TestNetworkBlock.Family(
        "test",
        "test:node",
        "Axis",
        Axis
      );
      foreach (TestNetworkBlock block in family) {
        if (spec != null)
          block.Attributes = new JsonObject(JToken.Parse(spec));
        world.Register(block);
      }
      TestNetworkBlock ns = family.First(b => b.Orientation == "ns");
      var scene = new Scene(world, ns);
      var entity = new OrientableNode {
        Orientation = "ns",
        PossibleOrientations = Axis,
      };
      scene.Record = new BEBehaviorAssembled(entity) { Fixed = fixedPiece };
      entity.Behaviors.Add(scene.Record);
      world.Place(Cell, ns, entity);
      world.Initialize(entity);
      return scene;
    }

    public WorldInteraction[] Help(BlockFacing aim) =>
      Block.GetPlacedBlockInteractionHelp(
        World.World,
        new BlockSelection {
          Position = Cell,
          Face = BlockFacing.UP,
          HitPosition = new Vec3d(
            0.5 + 0.5 * aim.Normali.X,
            0.5 + 0.5 * aim.Normali.Y,
            0.5 + 0.5 * aim.Normali.Z
          ),
        },
        Player.Player
      );
  }
}
