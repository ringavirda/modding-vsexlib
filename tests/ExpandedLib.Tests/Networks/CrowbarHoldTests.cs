using System.Linq;
using ExpandedLib.Industry.Helpers;
using ExpandedLib.Industry.Molten;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Covers <see cref="CrowbarHold"/>: the timing of every crowbar verb, the break sound, the interaction
/// help and the use animation of a held crowbar aimed at an <see cref="ICrowbarVerb"/> block.</summary>
[Collection(ExHarmonyCollection.Name)]
public class CrowbarHoldTests {
  private static readonly BlockPos Pos = new(5, 6, 7, 0);

  private sealed class VerbBlock : Block, ICrowbarVerb {
    public bool Acts { get; set; } = true;

    public bool CrowbarActs(IBlockAccessor blocks, BlockPos pos) => Acts;
  }

  private static IPlayer Player(TestWorld world, EnumGameMode mode) {
    TestPlayer player = world.Player("holder");
    player.GameMode = mode;
    return player.Player;
  }

  // Fails when the survival hold ends early or late: 0.9 s holds on, a full second ends it.
  [Theory]
  [InlineData(0.9f, true)]
  [InlineData(1f, false)]
  public void Holding_runs_a_second_in_survival(float seconds, bool goesOn) {
    var world = new TestWorld();

    Assert.Equal(
      goesOn,
      CrowbarHold.Holding(seconds, Player(world, EnumGameMode.Survival))
    );
  }

  // Fails when Holding drops the Creative branch: 0.2 s still holds on.
  [Fact]
  public void Holding_runs_a_tenth_in_creative() {
    var world = new TestWorld();
    IPlayer player = Player(world, EnumGameMode.Creative);

    Assert.True(CrowbarHold.Holding(0.05f, player));
    Assert.False(CrowbarHold.Holding(0.2f, player));
  }

  // Fails when Released acts on a short hold, or refuses a hold past half a second.
  [Theory]
  [InlineData(0.5f, false)]
  [InlineData(0.6f, true)]
  public void Release_acts_past_half_a_second_in_survival(
    float seconds,
    bool acts
  ) {
    var world = new TestWorld();

    Assert.Equal(
      acts,
      CrowbarHold.Released(seconds, Player(world, EnumGameMode.Survival))
    );
  }

  // Fails when Released drops the Creative branch: a click with no hold does not act.
  [Fact]
  public void Release_acts_at_once_in_creative() {
    var world = new TestWorld();

    Assert.True(CrowbarHold.Released(0f, Player(world, EnumGameMode.Creative)));
  }

  // Fails when Claim skips the tool test: an empty hand claims, or when it skips the land claim.
  [Fact]
  public void Claim_needs_a_crowbar_and_the_land_claim() {
    var world = new TestWorld();
    TestPlayer holder = world.Player("claimed");
    IPlayer player = holder.Player;
    Item crowbar = world.RegisterItem("game:crowbar-iron");
    crowbar.Tool = EnumTool.Crowbar;
    var selection = new BlockSelection { Position = Pos };

    holder.Hold(null);
    Assert.False(CrowbarHold.Claim(world.World, player, selection));

    holder.Hold(new ItemStack(crowbar));
    Assert.True(CrowbarHold.Claim(world.World, player, selection));

    world
      .World.Claims.TryAccess(
        Arg.Any<IPlayer>(),
        Arg.Any<BlockPos>(),
        EnumBlockAccessFlags.BuildOrBreak
      )
      .Returns(false);
    Assert.False(CrowbarHold.Claim(world.World, player, selection));
  }

  // Fails when PlayBreak plays nothing, plays twice, or plays somewhere else than the cell.
  [Fact]
  public void PlayBreak_sounds_once_at_the_cell() {
    var world = new TestWorld();
    ExSounds.StopServer();

    CrowbarHold.PlayBreak(world.World, Pos, ExSounds.MetalBreak);

    world
      .World.Received(1)
      .PlaySoundAt(
        ExSounds.MetalBreak,
        Pos.X + 0.5,
        Pos.Y + 0.5,
        Pos.Z + 0.5,
        null,
        true,
        24f,
        0.8f
      );
  }

  // Fails when Help advertises a chisel or an unnamed tool, drops the right mouse, or loses a crowbar.
  [Fact]
  public void Help_names_every_crowbar_on_the_right_mouse() {
    var world = new TestWorld();
    world.RegisterItem("game:crowbar-iron").Tool = EnumTool.Crowbar;
    world.RegisterItem("game:crowbar-steel").Tool = EnumTool.Crowbar;
    world.RegisterItem("game:chisel-iron").Tool = EnumTool.Chisel;

    WorldInteraction help = CrowbarHold.Help(world.World, "test:break");

    Assert.Equal("test:break", help.ActionLangCode);
    Assert.Equal(EnumMouseButton.Right, help.MouseButton);
    Assert.Equal(
      ["game:crowbar-iron", "game:crowbar-steel"],
      help.Itemstacks.Select(s => s.Collectible.Code.ToString()).Order()
    );
  }

  private sealed class AnimationRig {
    public readonly TestWorld World = new();
    public readonly TestPlayer Player;
    public readonly ItemCrowbar Crowbar = new();
    public readonly VerbBlock Block = TestBlocks.Configure(
      new VerbBlock(),
      "test:crowbarverb",
      80
    );

    public AnimationRig() {
      Player = World.Player("aimer");
      World.Register(Block);
      World.Place(Pos, Block);
      Crowbar.HeldTpUseAnimation = "breakhit";
      ReflectionHelpers.SetField(Crowbar, "api", World.Api);
      Player.Entity.World = World.World;
    }

    public string Animation(bool aimed = true) {
      Player.Entity.BlockSelection = aimed
        ? new BlockSelection { Position = Pos }
        : null;
      return Crowbar.GetHeldTpUseAnimation(
        Player.Entity.RightHandItemSlot,
        Player.Entity
      );
    }
  }

  // Fails when the patch is not applied: the crowbar keeps vanilla's interactstatic off a beam.
  [Fact]
  public void Animation_is_the_crowbars_own_aimed_at_a_verb_block() {
    using var fixture = new HarmonyFixture(
      "exlibtest.crowbaranim",
      typeof(CrowbarUseAnimationPatch).Assembly
    );
    var rig = new AnimationRig();

    Assert.Equal("breakhit", rig.Animation());
  }

  // Fails when the patch ignores CrowbarActs or the selection: a cell that would not act, or no aim, changes the animation.
  [Fact]
  public void Animation_stays_interactstatic_where_the_verb_would_not_act() {
    using var fixture = new HarmonyFixture(
      "exlibtest.crowbaranim",
      typeof(CrowbarUseAnimationPatch).Assembly
    );
    var rig = new AnimationRig();

    rig.Block.Acts = false;
    Assert.Equal("interactstatic", rig.Animation());
    rig.Block.Acts = true;
    Assert.Equal("interactstatic", rig.Animation(aimed: false));
  }
}
