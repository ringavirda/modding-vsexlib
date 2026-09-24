using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

/// <summary><see cref="SourceLaws"/> over planted source text, one rule or exemption
/// each.</summary>
public class SourceLawsTests(ITestOutputHelper output) {
  // Source i is written as Planted{i}.cs, the file name every finding carries.
  private IReadOnlyList<string> Scan(
    Func<IEnumerable<string>, IReadOnlyList<string>> law,
    params string[] sources
  ) {
    using var files = new PlantedFiles();
    IReadOnlyList<string> findings = law(
      sources.Select((s, i) => files.Write($"Planted{i}.cs", s)).ToArray()
    );
    foreach (string line in findings)
      output.WriteLine(line);
    return findings;
  }

  #region StaleOnExchange

  // Fails when StaleOnExchange accepts a block entity that builds an animator and never overrides
  // OnExchanged.
  [Fact]
  [PlantedDefect(typeof(SourceLaws), nameof(SourceLaws.StaleOnExchange))]
  public void An_animator_with_no_exchange_override_is_named() =>
    Assert.Equal(
      "Planted0.cs:4: PumpEntity; InitializeAnimator with no OnExchanged override, so an "
        + "exchange keeps the old facing",
      Assert.Single(
        Scan(
          SourceLaws.StaleOnExchange,
          "class PumpEntity : BlockEntity {\n"
            + "  public override void Initialize(ICoreAPI api) {\n"
            + "    // util.RegisterRenderer(r);\n"
            + "    util.InitializeAnimator(\"pump\", mesh, shape, rot);\n"
            + "  }\n}"
        )
      )
    );

  // Fails when StaleOnExchange accepts an override that drops the base call.
  [Fact]
  [PlantedDefect(typeof(SourceLaws), nameof(SourceLaws.StaleOnExchange))]
  public void An_override_without_the_base_call_is_named() =>
    Assert.EndsWith(
      "ValveEntity; OnExchanged never calls base.OnExchanged, so Block stays the old block",
      Assert.Single(
        Scan(
          SourceLaws.StaleOnExchange,
          "class ValveEntity : ExBlockEntity {\n"
            + "  void Init() => capi.Event.RegisterRenderer(r, stage);\n"
            + "  public override void OnExchanged(Block block) => Rebuild();\n}"
        )
      )
    );

  // Fails when StaleOnExchange exempts every ExMeshCache use, not only those in OnTesselation.
  [Fact]
  [PlantedDefect(typeof(SourceLaws), nameof(SourceLaws.StaleOnExchange))]
  public void A_mesh_cached_outside_tesselation_is_named() =>
    Assert.Contains(
      "HopperEntity; ExMeshCache with no OnExchanged override",
      Assert.Single(
        Scan(
          SourceLaws.StaleOnExchange,
          "class HopperEntity : BlockEntity {\n"
            + "  public override void Initialize(ICoreAPI api) {\n"
            + "    _mesh = ExMeshCache.GetOrCreate(capi, Block, \"body\", Build);\n"
            + "  }\n}"
        )
      )
    );

  // Fails when StaleOnExchange stops following a base type through the files, or reads a
  // ToggleAnimator as nothing built.
  [Fact]
  [PlantedDefect(typeof(SourceLaws), nameof(SourceLaws.StaleOnExchange))]
  public void A_toggle_animator_on_a_derived_entity_is_named() =>
    Assert.Contains(
      "Planted1.cs:2: DoorEntity; ToggleAnimator",
      Assert.Single(
        Scan(
          SourceLaws.StaleOnExchange,
          "abstract class FurnaceEntity : BlockEntityContainer { }",
          "class DoorEntity : FurnaceEntity {\n"
            + "  void Init() { _toggle = new ToggleAnimator(this, Build); }\n}"
        )
      )
    );

  // Fails when StaleOnExchange reads a partial type's parts in the order the files are given, or
  // takes its base from the first part only, here an interface.
  [Fact]
  public void A_partial_types_first_hit_is_read_in_path_order() {
    using var files = new PlantedFiles();
    string first = files.Write(
      "A.cs",
      "partial class BoilerEntity : ITexPositionSource {\n"
        + "  void Init() => _anim = new ConstructedAnimator(this, Key);\n}"
    );
    string second = files.Write(
      "B.cs",
      "partial class BoilerEntity : BlockEntity {\n"
        + "  void Client() => capi.Event.RegisterRenderer(r, stage);\n}"
    );

    Assert.StartsWith(
      "A.cs:2: BoilerEntity; ConstructedAnimator",
      Assert.Single(SourceLaws.StaleOnExchange([second, first]))
    );
  }

  // Fails when StaleOnExchange flags an overriding type, a mesh taken in OnTesselation, a
  // non-entity type, a behaviour, or misses an override in another partial part.
  [Fact]
  public void Overrides_tesselation_meshes_and_non_entities_pass() =>
    Assert.Empty(
      Scan(
        SourceLaws.StaleOnExchange,
        "partial class WheelEntity : BlockEntity {\n"
          + "  void Init() => capi.Event.RegisterRenderer(r, stage);\n}\n"
          + "class TapEntity : BlockEntity {\n"
          + "  public override bool OnTesselation(ITerrainMeshPool m, ITesselatorAPI t) {\n"
          + "    m.AddMeshData(ExMeshCache.GetOrCreate(capi, Block, \"k\", Build));\n"
          + "    return true;\n  }\n}\n"
          + "class BarrelBlock : Block {\n"
          + "  void Init() => capi.Event.RegisterRenderer(r, stage);\n}\n"
          + "class SpinBehavior : BEBehaviorAnimatable {\n"
          + "  void Init() => animUtil.InitializeAnimator(\"x\", m, s, r);\n}",
        "partial class WheelEntity {\n"
          + "  public override void OnExchanged(Block block) {\n"
          + "    base.OnExchanged(block);\n  }\n}"
      )
    );

  #endregion

  #region UndrivenRotor

  private const string Flywheel =
    "using ExpandedLib.Industry.MechanicalPower;\n"
    + "class FlywheelEntity : BlockEntity {\n"
    + "  void Spin(float speed) {\n"
    + "    util.StartAnimation(new AnimationMetaData { Code = \"cycle\",\n"
    + "      AnimationSpeed = speed * 2f, EaseInSpeed = 3f });\n"
    + "  }\n";

  // Fails when UndrivenRotor accepts an animation speed set from a speed beside the network.
  [Fact]
  [PlantedDefect(typeof(SourceLaws), nameof(SourceLaws.UndrivenRotor))]
  public void A_rotor_on_its_own_clock_is_named() =>
    Assert.Equal(
      "Planted0.cs:5: FlywheelEntity; animation speed speed * 2f runs on its own clock "
        + "beside a mechanical network; take the frame from MPAnim.FrameFromAngle",
      Assert.Single(Scan(SourceLaws.UndrivenRotor, Flywheel + "}"))
    );

  // Fails when UndrivenRotor stops accepting a frame taken from the network angle.
  [Fact]
  public void A_rotor_framed_from_the_network_angle_passes() =>
    Assert.Empty(
      Scan(
        SourceLaws.UndrivenRotor,
        Flywheel
          + "  void Pin() => st.CurrentFrame = MPAnim.FrameFromAngle(port.DrivenAngleRad, 30);\n}"
      )
    );

  // Fails when UndrivenRotor names a literal speed or a type that never names the network.
  [Fact]
  public void A_literal_speed_or_a_type_off_the_network_passes() =>
    Assert.Empty(
      Scan(
        SourceLaws.UndrivenRotor,
        "using ExpandedLib.Industry.MechanicalPower;\n"
          + "class DoorEntity : BlockEntity {\n"
          + "  void Open() => Start(new AnimationMetaData { AnimationSpeed = 1.5f });\n}",
        "class FanEntity : BlockEntity {\n"
          + "  void Run(float speed) => Start(new AnimationMetaData { AnimationSpeed = speed });\n}"
      )
    );

  #endregion

  #region LetterFacing

  // Fails when LetterFacing accepts a bare FromCode, or reads one inside a string or comment.
  [Fact]
  [PlantedDefect(typeof(SourceLaws), nameof(SourceLaws.LetterFacing))]
  public void A_bare_FromCode_is_named_and_the_letter_fallback_passes() =>
    Assert.Equal(
      "Planted0.cs:1: BlockFacing.FromCode(Variant[\"side\"]); returns null on a single-letter "
        + "side state; fall back to BlockFacing.FromFirstLetter",
      Assert.Single(
        Scan(
          SourceLaws.LetterFacing,
          "var a = BlockFacing.FromCode(Variant[\"side\"]);\n"
            + "var b = BlockFacing.FromCode(face) ?? BlockFacing.FromFirstLetter(face[0]);\n"
            + "// var c = BlockFacing.FromCode(side);\n"
            + "string d = \"BlockFacing.FromCode(side)\";"
        )
      )
    );

  #endregion

  #region UnguardedSearch

  // Fails when UnguardedSearch accepts an index into the local a search is assigned to.
  [Fact]
  [PlantedDefect(typeof(SourceLaws), nameof(SourceLaws.UnguardedSearch))]
  public void An_unchecked_index_into_a_search_local_is_named() =>
    Assert.Equal(
      "Planted0.cs:3: matches[0]; reads a SearchBlocks result with no length check; a code "
        + "matching nothing throws",
      Assert.Single(
        Scan(
          SourceLaws.UnguardedSearch,
          "void Tint() {\n"
            + "  Block[] matches = Api.World.SearchBlocks(wanted);\n"
            + "  int color = matches[0].GetColor(capi, Pos);\n}"
        )
      )
    );

  // Fails when UnguardedSearch misses a read taken straight off the call.
  [Fact]
  [PlantedDefect(typeof(SourceLaws), nameof(SourceLaws.UnguardedSearch))]
  public void A_read_straight_off_the_call_is_named() =>
    Assert.Contains(
      ":1: SearchBlocks(code).First(); reads a SearchBlocks result",
      Assert.Single(
        Scan(
          SourceLaws.UnguardedSearch,
          "Block b = world.SearchBlocks(code).First();"
        )
      )
    );

  // Fails when UnguardedSearch names a read after a length check, a FirstOrDefault, or a local of
  // the same name in another block.
  [Fact]
  public void Checked_reads_and_FirstOrDefault_pass() =>
    Assert.Empty(
      Scan(
        SourceLaws.UnguardedSearch,
        "void A() {\n"
          + "  Block[] matches = Api.World.SearchBlocks(code);\n"
          + "  if (matches.Length > 0) block = matches[0];\n"
          + "  var found = world.SearchBlocks(code);\n"
          + "  if (found is { Length: > 0 }) use(found[0]);\n}\n"
          + "Block? B() {\n"
          + "  Block[] all = world.SearchBlocks(code);\n"
          + "  return all.FirstOrDefault();\n}\n"
          + "void C() {\n  Block[] all = Scan();\n  use(all[0]);\n}"
      )
    );

  #endregion

  [Fact]
  public void Key_is_the_file_and_subject() =>
    Assert.Equal(
      "ExOrientation.cs: BlockFacing.FromCode(side)",
      SourceLaws.Key(
        "ExOrientation.cs:181: BlockFacing.FromCode(side); returns null; fall back"
      )
    );
}
