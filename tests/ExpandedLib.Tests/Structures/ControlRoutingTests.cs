#if GAME_GE_1_22
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A click on a footprint cell reaches the principal with the box id it hit, and the router sends it to
/// the control's handler by held item, to the body for a body hit, and refuses what no handler takes.</summary>
public class ControlRoutingTests
{
  private static readonly BlockPos PrincipalPos = new(10, 5, 10);
  private static readonly BlockPos CellPos = new(11, 5, 10);

  private const string Controls = """
    [
      { "id": "anvil", "kind": "work", "alsoIds": ["botdie"] },
      { "id": "botdie", "kind": "tooling" }
    ]
    """;

  #region Forwarding

  // Fails when Repoint sets SelectionBoxId to null.
  [Fact]
  public void The_id_reaches_the_principal_through_a_filler()
  {
    using var scene = new Scene();

    bool handled = scene.Start("anvil");

    Assert.True(handled);
    Assert.Equal("anvil", scene.Principal.StartSelection?.SelectionBoxId);
    Assert.Equal(PrincipalPos, scene.Principal.StartSelection?.Position);
    Assert.Equal(CellPos, scene.Principal.StartCell);
  }

  // Fails when step forwarding builds a fresh selection.
  [Fact]
  public void Step_and_stop_carry_the_id()
  {
    using var scene = new Scene();
    BlockSelection sel = scene.Selection("botdie");

    scene.Filler.OnBlockInteractStep(0.1f, scene.World, scene.Who, sel);
    scene.Filler.OnBlockInteractStop(0.2f, scene.World, scene.Who, sel);

    Assert.Equal("botdie", scene.Principal.StepSelection?.SelectionBoxId);
    Assert.Equal("botdie", scene.Principal.StopSelection?.SelectionBoxId);
  }

  // Fails when the help path clears the id.
  [Fact]
  public void Help_sees_the_id()
  {
    using var scene = new Scene();

    scene.Filler.GetPlacedBlockInteractionHelp(
      scene.World,
      scene.Selection("anvil"),
      scene.Who
    );

    Assert.Equal("anvil", scene.Principal.HelpSelection?.SelectionBoxId);
  }

  #endregion

  #region Reading the hit

  // Fails when alsoIds are dropped from Ids.
  [Fact]
  public void AlsoIds_follow_the_id()
  {
    using var scene = new Scene();

    ControlHit hit = ControlHit.Of(
      scene.PrincipalBlock,
      scene.PrincipalEntity,
      scene.Selection("anvil")
    );

    Assert.Equal("anvil", hit.Id);
    Assert.Equal(["anvil", "botdie"], hit.Ids);
  }

  // Fails when LookedAt does not check that the selection is on the structure.
  [Fact]
  public void LookedAt_is_empty_off_the_structure()
  {
    using var scene = new Scene();
    var elsewhere = new BlockPos(30, 5, 30);
    scene.TestWorld.Place(elsewhere, scene.PrincipalBlock);

    scene.Player.Player.CurrentBlockSelection.Returns(scene.Selection("anvil"));
    ControlHit on = scene.Looked();
    scene.Player.Player.CurrentBlockSelection.Returns(
      new BlockSelection { Position = elsewhere, SelectionBoxId = "anvil" }
    );
    ControlHit off = scene.Looked();

    Assert.Equal("anvil", on.Id);
    Assert.Null(off.Id);
    Assert.Empty(off.Ids);
  }

  #endregion

  #region Routing

  // Fails when the held predicates are ignored.
  [Fact]
  public void The_held_item_picks_the_verb()
  {
    using var scene = new Scene();

    scene.Player.Hold(null);
    scene.Start("anvil");
    string? bare = scene.Principal.Ran;
    scene.Player.Hold(scene.Die);
    scene.Start("anvil");

    Assert.Equal("anvil", bare);
    Assert.Equal("botdie", scene.Principal.Ran);
  }

  // Fails when the control takes any held item.
  [Fact]
  public void An_item_the_control_does_not_take_falls_through()
  {
    using var scene = new Scene();
    scene.Player.Hold(scene.Wrench);

    bool handled = scene.Start("anvil");

    Assert.False(handled);
    Assert.Null(scene.Principal.Ran);
  }

  // Fails when body hits are routed to the first id registered.
  [Fact]
  public void A_body_hit_goes_to_OnBody()
  {
    using var scene = new Scene();

    bool handled = scene.Start(null);

    Assert.True(handled);
    Assert.Equal("body", scene.Principal.Ran);
  }

  // Fails when OnBody falls back to the control handlers.
  [Fact]
  public void A_body_hit_refuses_a_control_verb()
  {
    using var scene = new Scene(withBody: false);

    bool handled = scene.Start(null);

    Assert.False(handled);
    Assert.Null(scene.Principal.Ran);
  }

  // Fails when the state is not read.
  [Fact]
  public void A_hidden_id_routes_as_a_body_hit()
  {
    using var scene = new Scene();
    scene.Principal.States["anvil"] = ControlState.Hidden;

    bool handled = scene.Start("anvil");

    Assert.True(handled);
    Assert.Equal("body", scene.Principal.Ran);
  }

  // Fails when Route returns true though no handler took the click.
  [Fact]
  public void Nothing_taken_returns_false()
  {
    using var scene = new Scene();
    scene.FillerEntity.AllowAttach = true;
    scene.Player.Hold(scene.Wrench);

    bool handled = scene.Start("anvil");

    Assert.False(handled);
  }

  #endregion

  #region Fixture

  private sealed class Scene : System.IDisposable
  {
    public readonly TestWorld TestWorld = new();
    public readonly TestPlayer Player;
    public readonly RoutedPrincipal Principal;
    public readonly Block PrincipalBlock;
    public readonly BlockEntity PrincipalEntity;
    public readonly BlockEntityStructureFiller FillerEntity;
    public readonly ItemStack Die = new(
      new Item { Code = new AssetLocation("test:die"), ItemId = 901 }
    );
    public readonly ItemStack Wrench = new(
      new Item { Code = new AssetLocation("test:wrench"), ItemId = 902 }
    );

    public Scene(bool withBody = true)
    {
      var router = new ControlRouter()
        .On("anvil", held => held == null, c => Ran(c, "anvil"))
        .On(
          "botdie",
          held => held?.Collectible.Code.Path == "die",
          c => Ran(c, "botdie")
        );
      if (withBody)
        router.OnBody(c => Ran(c, "body"));
      PrincipalBlock = TestBlocks.Configure(
        new RoutedBlock(router),
        "test:routed",
        50
      );
      PrincipalBlock.Attributes = new JsonObject(
        new JObject { ["controls"] = JArray.Parse(Controls) }
      );
      var entity = new RoutedPrincipal();
      Principal = entity;
      PrincipalEntity = entity;
      ((RoutedBlock)PrincipalBlock).Target = entity;
      TestWorld.Place(PrincipalPos, PrincipalBlock, entity);
      TestWorld.PlaceFiller(CellPos, null, PrincipalPos);
      FillerEntity = (BlockEntityStructureFiller)
        TestWorld.GetBlockEntity(CellPos)!;
      Player = TestWorld.Player();
    }

    public IWorldAccessor World => TestWorld.World;
    public IPlayer Who => Player.Player;
    public BlockStructureFiller Filler => TestWorld.Filler;

    public BlockSelection Selection(string? id) =>
      new()
      {
        Position = CellPos.Copy(),
        Face = BlockFacing.UP,
        SelectionBoxId = id,
      };

    public bool Start(string? id) =>
      Filler.OnBlockInteractStart(World, Who, Selection(id));

    public ControlHit Looked() =>
      ControlHit.LookedAt(PrincipalBlock, World, PrincipalPos, Who);

    private bool Ran(ControlContext context, string verb)
    {
      Principal.Ran = verb;
      return true;
    }

    public void Dispose() => TestWorld.Dispose();
  }

  private sealed class RoutedBlock(ControlRouter router)
    : Block,
      IFillerInteractionTarget
  {
    public RoutedPrincipal? Target;

    public bool OnFillerInteractStart(
      IWorldAccessor world,
      IPlayer byPlayer,
      BlockSelection principalSel,
      BlockPos clickedCell
    )
    {
      Target!.StartSelection = principalSel;
      Target.StartCell = clickedCell;
      return router.Route(world, byPlayer, principalSel, clickedCell);
    }

    public bool OnFillerInteractStep(
      float secondsUsed,
      IWorldAccessor world,
      IPlayer byPlayer,
      BlockSelection principalSel,
      BlockPos clickedCell
    )
    {
      Target!.StepSelection = principalSel;
      return true;
    }

    public void OnFillerInteractStop(
      float secondsUsed,
      IWorldAccessor world,
      IPlayer byPlayer,
      BlockSelection principalSel,
      BlockPos clickedCell
    ) => Target!.StopSelection = principalSel;

    public WorldInteraction[] GetFillerInteractionHelp(
      IWorldAccessor world,
      BlockSelection principalSel,
      IPlayer forPlayer,
      BlockPos clickedCell
    )
    {
      Target!.HelpSelection = principalSel;
      return [];
    }
  }

  private sealed class RoutedPrincipal : BlockEntity, IControlStates
  {
    public readonly Dictionary<string, ControlState> States = [];
    public BlockSelection? StartSelection;
    public BlockPos? StartCell;
    public BlockSelection? StepSelection;
    public BlockSelection? StopSelection;
    public BlockSelection? HelpSelection;
    public string? Ran;

    public ControlState? StateOf(string controlId) =>
      States.TryGetValue(controlId, out ControlState state) ? state : null;
  }

  #endregion
}
#endif
