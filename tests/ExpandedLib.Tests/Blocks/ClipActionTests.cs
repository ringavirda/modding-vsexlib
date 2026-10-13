using System;
using System.IO;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Registries;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A block's declared clip actions: how they are written, read per variant and resolved
/// against the shape, and the key the shape's animator is cached under.</summary>
public class ClipActionTests {
  private static readonly string Fixtures = Path.Combine(
    RepoPaths.Root,
    "tests",
    "ExpandedLib.Tests",
    "Laws",
    "Fixtures",
    "cliptrace"
  );

  private static TestWorld NewWorld() =>
    new TestWorld()
      .ClientAssets("cliptrace", Fixtures)
      .RegisterClass("test-plain", typeof(PlainEntity));

  private static Block Define(TestWorld world, ExBlockDef def) =>
    world.DefineBlock(def, DefinitionCodes.Expand(def).First());

  private static ExBlockDef Def(string code) =>
    ExBlockDef.Create("cliptrace", code).EntityClass("test-plain");

  private static Shape ShapeOf(TestWorld world, string name) =>
    ExMeshCache.LoadShape(
      world.ClientApi,
      new AssetLocation($"cliptrace:shapes/{name}.json")
    )!;

  [Fact]
  public void Clip_action_defaults_come_from_the_shape() {
    using var world = NewWorld();
    ClipActions door = ClipActions.Of(
      Define(
        world,
        Def("door").ClipAction(new ClipAction("open") { State = "s" })
      )
    );
    ClipActions fan = ClipActions.Of(
      Define(
        world,
        Def("fan").ClipAction(new ClipAction("spin") { State = "s" })
      )
    );

    ClipAction held = door.Resolve(ShapeOf(world, "door")).Single();
    ClipAction spun = fan.Resolve(ShapeOf(world, "fan")).Single();

    Assert.Equal(ClipPose.Frame, held.Pose);
    Assert.Equal(1f, held.Seconds!.Value, 3);
    Assert.Equal(["leaf"], held.Parts);
    Assert.Equal(ClipPose.Run, spun.Pose);
    Assert.Equal(2f, spun.Seconds!.Value, 3);
    Assert.Equal(["rotor"], spun.Parts);
  }

  [Fact]
  public void Clip_actions_resolve_per_variant() {
    using var world = NewWorld();
    ExBlockDef def = Def("pacedoor")
      .VariantGroup("pace", "slow", "fast")
      .ClipActionByType(
        "*-slow",
        new ClipAction("open") { State = "s", Seconds = 2f }
      )
      .ClipActionByType(
        "*-fast",
        new ClipAction("open") { State = "s", Seconds = 0.5f }
      );

    float[] seconds =
    [
      .. DefinitionCodes
        .Expand(def)
        .Select(v => ClipActions.Of(world.DefineBlock(def, v)))
        .Select(a => a.Declared.Single().Seconds!.Value),
    ];

    Assert.Equal([2f, 0.5f], seconds);
  }

  [Fact]
  public void An_action_with_neither_state_nor_by_is_refused() {
    using var world = NewWorld();
    Block block = Define(world, Def("idle").ClipAction(new ClipAction("open")));

    var e = Assert.Throws<InvalidOperationException>(() =>
      ClipActions.Of(block)
    );

    Assert.Contains("neither a state nor a by line", e.Message);
  }

  [Fact]
  public void An_action_naming_no_clip_is_refused() {
    using var world = NewWorld();
    Block block = Define(
      world,
      Def("clipless")
        .Attribute("clipActions", new JArray(new JObject { ["state"] = "s" }))
    );

    var e = Assert.Throws<InvalidOperationException>(() =>
      ClipActions.Of(block)
    );

    Assert.Contains("names no clip", e.Message);
  }

  [Fact]
  public void A_lock_is_refused() {
    using var world = NewWorld();
    Block block = Define(
      world,
      Def("locked")
        .ClipAction(
          new ClipAction("open") { Pose = ClipPose.Lock, State = "s" }
        )
    );

    var e = Assert.Throws<InvalidOperationException>(() =>
      ClipActions.Of(block)
    );

    Assert.Contains("lock", e.Message);
  }

  [Fact]
  public void An_unknown_pose_is_refused() {
    using var world = NewWorld();
    Block block = Define(
      world,
      Def("posed")
        .Attribute(
          "clipActions",
          new JArray(
            new JObject {
              ["clip"] = "open",
              ["state"] = "s",
              ["pose"] = "frames",
            }
          )
        )
    );

    var e = Assert.Throws<InvalidOperationException>(() =>
      ClipActions.Of(block)
    );

    Assert.Contains("pose 'frames'", e.Message);
  }

  [Theory]
  [InlineData("[1, 1]")]
  [InlineData("[1]")]
  [InlineData("[0, 1, 2]")]
  public void A_range_that_is_not_two_distinct_numbers_is_refused(string range) {
    using var world = NewWorld();
    Block block = Define(
      world,
      Def("ranged")
        .Attribute(
          "clipActions",
          new JArray(
            new JObject {
              ["clip"] = "open",
              ["state"] = "s",
              ["range"] = JToken.Parse(range),
            }
          )
        )
    );

    var e = Assert.Throws<InvalidOperationException>(() =>
      ClipActions.Of(block)
    );

    Assert.Contains("not two distinct numbers", e.Message);
  }

  [Fact]
  public void A_clip_the_shape_lacks_is_refused_on_resolve() {
    using var world = NewWorld();
    ClipActions actions = ClipActions.Of(
      Define(
        world,
        Def("ghost").ClipAction(new ClipAction("missing") { State = "s" })
      )
    );

    var e = Assert.Throws<InvalidOperationException>(() =>
      actions.Resolve(ShapeOf(world, "door"))
    );

    Assert.Contains("'missing'", e.Message);
  }

  [Fact]
  public void A_block_without_clip_actions_declares_none() {
    using var world = NewWorld();

    Assert.Empty(ClipActions.Of(Define(world, Def("plain"))).Declared);
  }

  [Fact]
  public void Declared_actions_keep_every_field_and_their_order() {
    using var world = NewWorld();
    var full = new ClipAction("open") {
      Pose = ClipPose.Play,
      State = "doorOpen",
      Range = (10f, 20f),
      When = 1f,
      By = ["lang-a", "lang-b"],
      Seconds = 3f,
      Parts = ["leaf"],
    };
    var second = new ClipAction("shut") { By = ["lang-c"] };

    ClipActions actions = ClipActions.Of(
      Define(world, Def("both").ClipAction(full).ClipAction(second))
    );

    Assert.Equal(
      [full.Clip, second.Clip],
      actions.Declared.Select(a => a.Clip)
    );
    ClipAction read = actions.Declared[0];
    Assert.Equal(full.Pose, read.Pose);
    Assert.Equal(full.State, read.State);
    Assert.Equal(full.Range, read.Range);
    Assert.Equal(full.When, read.When);
    Assert.Equal(full.By, read.By);
    Assert.Equal(full.Seconds, read.Seconds);
    Assert.Equal(full.Parts, read.Parts);
  }

  [Fact]
  public void Animator_keys_name_the_shape_and_its_overlays() {
    var plain = new Block {
      Shape = new CompositeShape { Base = new AssetLocation("a:one") },
    };
    var overlaid = new Block {
      Shape = new CompositeShape {
        Base = new AssetLocation("a:one"),
        Overlays =
        [
          new CompositeShape { Base = new AssetLocation("b:two") },
          new CompositeShape { Base = new AssetLocation("c:three") },
        ],
      },
    };

    Assert.Equal("a:one", AnimatorKeys.Of(plain));
    Assert.Equal("a:one+b:two+c:three", AnimatorKeys.Of(overlaid));
  }

  private sealed class PlainEntity : BlockEntity { }
}
