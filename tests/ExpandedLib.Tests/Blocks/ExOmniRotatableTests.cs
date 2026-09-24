using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// <see cref="BlockBehaviorExOmniRotatable"/>, vanilla's <c>OmniRotatable</c> over a dashed code: each
/// placement rule and place mode, the place-mode crafting cycle, rotation and both flips.
/// </summary>
public class ExOmniRotatableTests {
  private const string Stem = "exlib:slag-slab";

  #region Placement

  // Fails when a placement rule builds the code from the first dash-segment, or picks another
  // state than vanilla's rule for that face, hit, yaw and mode.
  [Theory]
  [InlineData("sides-block", 0, "east", 1, 0.5, 0.5, 0, "west")]
  [InlineData("sides-block", 0, "east", 1, 0.5, 0.1, 0, "north")]
  [InlineData("sides-block", 0, "east", 1, 0.5, 0.9, 0, "south")]
  [InlineData("sides-block", 0, "east", 1, 0.1, 0.5, 0, "down")]
  [InlineData("sides-block", 0, "east", 1, 0.9, 0.5, 0, "up")]
  [InlineData("sides-block", 0, "up", 0.5, 1, 0.5, 0, "down")]
  [InlineData("sides-block", 0, "up", 0.5, 1, 0.1, 0, "north")]
  [InlineData("sides-block", 0, "up", 0.9, 1, 0.5, 0, "east")]
  [InlineData("sides-block", 0, "up", 0.1, 1, 0.5, 0, "west")]
  [InlineData("sides-block", 0, "south", 0.5, 0.5, 1, 0, "north")]
  [InlineData("sides-block", 0, "south", 0.1, 0.5, 1, 0, "west")]
  [InlineData("sides-block", 0, "south", 0.5, 0.9, 1, 0, "up")]
  [InlineData("sides-player", 0, "up", 0.5, 1, 0.5, 0, "down")]
  [InlineData("sides-player", 0, "east", 1, 0.5, 0.5, 2, "north")]
  [InlineData("h", 0, "up", 0.5, 1, 0.5, 1, "east")]
  [InlineData("h", 0, "north", 0.5, 0.5, 0, 1, "north")]
  [InlineData("v", 0, "up", 0.5, 1, 0.5, 0, "up")]
  [InlineData("v", 0, "east", 1, 0.2, 0.5, 0, "up")]
  [InlineData("v", 0, "east", 1, 0.8, 0.5, 0, "down")]
  [InlineData("hv", 0, "down", 0.5, 0, 0.5, 0, "down-south")]
  [InlineData("hv4-block", 0, "east", 1, 0.5, 0.1, 0, "left-west")]
  [InlineData("hv4-block", 0, "east", 1, 0.5, 0.9, 0, "right-west")]
  [InlineData("hv4-block", 0, "east", 1, 0.1, 0.5, 0, "up-west")]
  [InlineData("hv4-player", 0, "north", 0.9, 0.5, 0, 1, "right-east")]
  [InlineData("hv4-player", 0, "north", 0.5, 0.9, 0, 1, "down-east")]
  [InlineData("none", 0, "up", 0.5, 1, 0.5, 0, "north")]
  [InlineData("sides-block", 1, "up", 0.5, 1, 0.5, 0, "down")]
  [InlineData("sides-block", 1, "east", 1, 0.2, 0.5, 0, "down")]
  [InlineData("sides-block", 1, "east", 1, 0.8, 0.5, 0, "up")]
  [InlineData("sides-block", 2, "east", 1, 0.5, 0.5, 0, "west")]
  [InlineData("sides-block", 2, "up", 0.5, 1, 0.5, 0, "north")]
  public void A_placement_lands_the_state_vanillas_rule_picks_on_the_whole_code(
    string kind,
    int mode,
    string face,
    double hx,
    double hy,
    double hz,
    int yawQuarters,
    string expected
  ) {
    var rig = new SlabRig(kind);

    Assert.True(
      rig.Place(face, new Vec3d(hx, hy, hz), yawQuarters, mode),
      rig.Failure
    );

    Assert.Equal($"{Stem}-{expected}", rig.PlacedCode);
  }

  // Fails when the null check on the picked state is removed.
  [Fact]
  public void A_state_the_blocktype_skips_is_refused() {
    var rig = new SlabRig("sides-block", skip: "down");

    Assert.False(rig.Place("up", new Vec3d(0.5, 1, 0.5)));

    Assert.Equal("cantplace", rig.Failure);
    Assert.Null(rig.PlacedCode);
  }

  // Fails when the picked state's CanPlaceBlock is skipped.
  [Fact]
  public void An_occupied_cell_is_refused_by_the_picked_state() {
    var rig = new SlabRig("sides-block");
    rig.World.Place(
      SlabRig.Pos,
      TestBlocks.Configure(new Block(), "game:rock-granite", 90)
    );

    Assert.False(rig.Place("up", new Vec3d(0.5, 1, 0.5)));

    Assert.Equal(
      "game:rock-granite",
      rig.World.GetBlock(SlabRig.Pos).Code.ToString()
    );
  }

  #endregion

  #region Place-mode crafting

  // Fails when the crafted stack's place mode is not advanced from the input's, or not cleared
  // after vertical.
  [Theory]
  [InlineData(0, 1)]
  [InlineData(1, 2)]
  [InlineData(2, 0)]
  public void Crafting_a_slab_alone_cycles_its_place_mode(int from, int to) {
    var rig = new SlabRig("sides-block");
    var input = new ItemStack(rig.Held);
    if (from != 0)
      input.Attributes.SetInt("slabPlaceMode", from);
    var inputSlot = new DummySlot(input);
    var output = new DummySlot(new ItemStack(rig.Held));
    var handled = EnumHandling.PassThrough;

    rig.Behaviour.OnCreatedByCrafting(
      [new DummySlot(), inputSlot],
      output,
      null!,
      ref handled
    );

    Assert.Equal(to, output.Itemstack.Attributes.GetInt("slabPlaceMode", 0));
    Assert.Equal(
      to != 0,
      output.Itemstack.Attributes.HasAttribute("slabPlaceMode")
    );
  }

  // Fails when the crafting cycle runs for an input that does not carry the behaviour.
  [Fact]
  public void Crafting_from_another_block_leaves_the_place_mode() {
    var rig = new SlabRig("sides-block");
    var output = new DummySlot(new ItemStack(rig.Held));
    var handled = EnumHandling.PassThrough;

    rig.Behaviour.OnCreatedByCrafting(
      [
        new DummySlot(
          new ItemStack(TestBlocks.Configure(new Block(), "game:plain", 91))
        ),
      ],
      output,
      null!,
      ref handled
    );

    Assert.False(output.Itemstack.Attributes.HasAttribute("slabPlaceMode"));
  }

  #endregion

  #region Rotation and flips

  // Fails when a rotation builds the code from the first dash-segment or turns the wrong way.
  [Theory]
  [InlineData("north", 90, "east")]
  [InlineData("north", 180, "south")]
  [InlineData("east", 270, "north")]
  [InlineData("west", 0, "west")]
  public void A_rotation_turns_a_horizontal_rot_on_the_whole_code(
    string from,
    int angle,
    string to
  ) {
    var rig = new SlabRig("sides-block");
    var handling = EnumHandling.PassThrough;

    AssetLocation code = rig.BehaviourOf(from)
      .GetRotatedBlockCode(angle, ref handling);

    Assert.Equal($"{Stem}-{to}", code.ToString());
    Assert.Equal(EnumHandling.PreventDefault, handling);
  }

  // Fails when a vertical rot is rotated or claims the rotation.
  [Fact]
  public void A_rotation_leaves_a_vertical_rot_to_the_block() {
    var rig = new SlabRig("sides-block");
    var handling = EnumHandling.PassThrough;

    AssetLocation code = rig.BehaviourOf("up")
      .GetRotatedBlockCode(90, ref handling);

    Assert.Equal($"{Stem}-up", code.ToString());
    Assert.Equal(EnumHandling.PassThrough, handling);
  }

  // Fails when rotateV4's left and right are not swapped where vanilla swaps them.
  [Theory]
  [InlineData("left-west", 90, "right-north")]
  [InlineData("right-south", 270, "left-east")]
  [InlineData("left-north", 90, "left-east")]
  [InlineData("up-north", 90, "up-east")]
  public void A_rotation_under_rotateV4_swaps_left_and_right_as_vanilla(
    string from,
    int angle,
    string to
  ) {
    var rig = new SlabRig("hv4-block");
    var handling = EnumHandling.PassThrough;

    AssetLocation code = rig.BehaviourOf(from)
      .GetRotatedBlockCode(angle, ref handling);

    Assert.Equal($"{Stem}-{to}", code.ToString());
  }

  // Fails when a horizontal flip builds from the first dash-segment or reverses a rot on another
  // axis.
  [Theory]
  [InlineData("north", "Z", "south")]
  [InlineData("east", "X", "west")]
  [InlineData("east", "Z", "east")]
  public void A_horizontal_flip_reverses_a_rot_on_its_axis(
    string from,
    string axis,
    string to
  ) {
    var rig = new SlabRig("sides-block");
    var handling = EnumHandling.PassThrough;

    AssetLocation code = rig.BehaviourOf(from)
      .GetHorizontallyFlippedBlockCode(
        System.Enum.Parse<EnumAxis>(axis),
        ref handling
      );

    Assert.Equal($"{Stem}-{to}", code.ToString());
  }

  // Fails when a vertical flip builds from the first dash-segment, or skips rot or v.
  [Theory]
  [InlineData("sides-block", "up", "down")]
  [InlineData("sides-block", "north", "north")]
  [InlineData("hv4-block", "up-east", "down-east")]
  [InlineData("hv4-block", "left-east", "left-east")]
  public void A_vertical_flip_reverses_a_vertical_rot_or_v(
    string kind,
    string from,
    string to
  ) {
    var rig = new SlabRig(kind);
    var handling = EnumHandling.PassThrough;

    AssetLocation code = rig.BehaviourOf(from)
      .GetVerticallyFlippedBlockCode(ref handling);

    Assert.Equal($"{Stem}-{to}", code.ToString());
  }

  #endregion

  #region Rig

  /// <summary>Every state of a slab blocktype <c>exlib:slag-slab</c>, each carrying its own
  /// <see cref="BlockBehaviorExOmniRotatable"/> configured by the kind: <c>rot</c> alone, or
  /// <c>v</c> then <c>rot</c> for the <c>hv</c> kinds.</summary>
  private sealed class SlabRig {
    internal static readonly BlockPos Pos = new(64, 16, 64, 0);

    private static readonly string[] Rots =
    [
      "north",
      "east",
      "south",
      "west",
      "up",
      "down",
      "left",
      "right",
    ];

    private static readonly string[] Vs = ["up", "down", "left", "right"];

    private readonly Dictionary<
      string,
      BlockBehaviorExOmniRotatable
    > _byState = [];

    internal SlabRig(string kind, string? skip = null) {
      string properties = kind switch {
        "sides-block" => """{"rotateSides":true,"facing":"block"}""",
        "sides-player" => """{"rotateSides":true}""",
        "h" => """{"rotateH":true}""",
        "v" => """{"rotateV":true}""",
        "hv" => """{"rotateH":true,"rotateV":true}""",
        "hv4-block" =>
          """{"rotateH":true,"rotateV":true,"rotateV4":true,"facing":"block"}""",
        "hv4-player" => """{"rotateH":true,"rotateV":true,"rotateV4":true}""",
        _ => "{}",
      };
      (string, string)[][] states = kind.StartsWith(
        "hv",
        StringComparison.Ordinal
      )
        ?
        [
          .. Vs.SelectMany(v =>
            Rots.Take(4).Select(r => new[] { ("v", v), ("rot", r) })
          ),
        ]
        : [.. Rots.Select(r => new[] { ("rot", r) })];

      int id = 1;
      foreach ((string, string)[] variants in states) {
        string state = string.Join("-", variants.Select(v => v.Item2));
        if (state == skip)
          continue;
        Block block = TestBlocks.Configure(
          new Block(),
          $"{Stem}-{state}",
          id++,
          variants
        );
        var behaviour = new BlockBehaviorExOmniRotatable(block);
        behaviour.Initialize(
          new JsonObject(Newtonsoft.Json.Linq.JToken.Parse(properties))
        );
        block.BlockBehaviors = [behaviour];
        block.CollectibleBehaviors = [behaviour];
        World.Register(block);
        _byState[state] = behaviour;
        if (Held == null) {
          Held = block;
          Behaviour = behaviour;
        }
      }
    }

    internal TestWorld World { get; } = new();

    /// <summary>The first registered state, the one placements are made with.</summary>
    internal Block Held { get; } = null!;

    internal BlockBehaviorExOmniRotatable Behaviour { get; } = null!;

    internal string? Failure { get; private set; }

    internal string? PlacedCode {
      get {
        Block at = World.GetBlock(Pos);
        return at.BlockId == 0 ? null : at.Code.ToString();
      }
    }

    internal BlockBehaviorExOmniRotatable BehaviourOf(string state) =>
      _byState[state];

    /// <summary>Places <see cref="Held"/> against <paramref name="face"/> at
    /// <paramref name="hit"/>, the player standing south of the cell with yaw
    /// <paramref name="yawQuarters"/> quarter turns, the stack in place mode
    /// <paramref name="mode"/>.</summary>
    internal bool Place(
      string face,
      Vec3d hit,
      int yawQuarters = 0,
      int mode = 0
    ) {
      TestPlayer player = World.Player();
      player.Entity.Pos.SetPos(Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 5.5);
      player.Entity.Pos.Yaw = yawQuarters * GameMath.PIHALF;
      player.Entity.LocalEyePos.Returns(new Vec3d());
      var stack = new ItemStack(Held);
      if (mode != 0)
        stack.Attributes.SetInt("slabPlaceMode", mode);
      var handling = EnumHandling.PassThrough;
      string failure = "";
      bool placed = Behaviour.TryPlaceBlock(
        World.World,
        player.Player,
        stack,
        new BlockSelection {
          Position = Pos.Copy(),
          Face = BlockFacing.FromCode(face),
          HitPosition = hit,
        },
        ref handling,
        ref failure
      );
      Failure = placed ? null : failure;
      return placed;
    }
  }

  #endregion
}
