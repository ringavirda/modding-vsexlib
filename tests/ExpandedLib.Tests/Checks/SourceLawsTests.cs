using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

  // Fails when StaleOnExchange reads a MeshData field written by ??=, a field written through out,
  // or an auto-property, as nothing built.
  [Fact]
  [PlantedDefect(typeof(SourceLaws), nameof(SourceLaws.StaleOnExchange))]
  public void A_mesh_field_with_no_exchange_override_is_named() =>
    Assert.Equal(
      [
        "Planted0.cs:4: CapEntity; MeshData field _cap" + NoOverride,
        "Planted0.cs:11: BarrelEntity; MeshData field _body" + NoOverride,
        "Planted0.cs:16: HoodEntity; MeshData field Hood" + NoOverride,
      ],
      Scan(
        SourceLaws.StaleOnExchange,
        "class CapEntity : BlockEntity {\n"
          + "  private MeshData? _cap;\n"
          + "  public override bool OnTesselation(ITerrainMeshPool m, ITesselatorAPI t) {\n"
          + "    _cap ??= Build(t);\n"
          + "    return true;\n  }\n}\n"
          + "class BarrelEntity : BlockEntity {\n"
          + "  private MeshData _body;\n"
          + "  void Tess(ITesselatorAPI t) {\n"
          + "    t.TesselateShape(Block, shape, out _body);\n  }\n}\n"
          + "class HoodEntity : BlockEntity {\n"
          + "  public MeshData? Hood { get; set; }\n"
          + "  void Tess() => Hood = Build();\n}"
      )
    );

  // Fails when StaleOnExchange accepts an override that keeps a MeshData field, reads a ??= in the
  // override as a clear, misses a clear through this., or counts a write outside the override.
  [Fact]
  [PlantedDefect(typeof(SourceLaws), nameof(SourceLaws.StaleOnExchange))]
  public void An_override_that_keeps_a_mesh_field_is_named() =>
    Assert.Equal(
      [
        "Planted0.cs:10: PedestalEntity._mold" + Kept("_mold"),
        "Planted0.cs:10: PedestalEntity._side" + Kept("_side"),
      ],
      Scan(
        SourceLaws.StaleOnExchange,
        "class PedestalEntity : BlockEntity {\n"
          + "  private MeshData? _mold;\n"
          + "  private MeshData? _end;\n"
          + "  private MeshData? _side;\n"
          + "  void Tess(ITesselatorAPI t) {\n"
          + "    t.TesselateBlock(MoldBlock, out _mold);\n"
          + "    _end = Cap(t);\n"
          + "    _side = Cap(t);\n"
          + "  }\n"
          + "  public override void OnExchanged(Block block) {\n"
          + "    base.OnExchanged(block);\n"
          + "    _renderer?.Dispose();\n"
          + "    this._end = null;\n"
          + "    _side ??= Cap(null);\n"
          + "  }\n}"
      )
    );

  // Fails when StaleOnExchange counts a MeshData local, or an initialiser as a write, or names a
  // field the override clears.
  [Fact]
  public void Mesh_locals_initialisers_and_cleared_fields_pass() =>
    Assert.Empty(
      Scan(
        SourceLaws.StaleOnExchange,
        "class ShaftEntity : BlockEntity {\n"
          + "  private MeshData? _shaft;\n"
          + "  private MeshData? _spare = null;\n"
          + "  bool Tess(ITesselatorAPI t) { _shaft ??= Build(t); return true; }\n"
          + "  public override void OnExchanged(Block block) {\n"
          + "    base.OnExchanged(block);\n"
          + "    _shaft = null;\n  }\n}\n"
          + "class GearEntity : BlockEntity {\n"
          + "  private MeshData? _spare = null;\n"
          + "  void Draw(ITesselatorAPI t) { MeshData built; built = Build(t); Add(built); }\n}"
      )
    );

  private const string NoOverride =
    " with no OnExchanged override, so an exchange keeps the old facing";

  private static string Kept(string field) =>
    $"; OnExchanged never assigns or clears the MeshData field {field}, so the mesh it caches "
    + "keeps the old facing";

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

  #region CachedTunables

  private const string Unreached = "; an /exmod config edit never reaches it";

  private static readonly Assembly[] Exlib = [typeof(ExlibConfig).Assembly];

  // Fails when CachedTunables accepts a value copied into a field in Initialize.
  [Fact]
  [PlantedDefect(typeof(SourceLaws), nameof(SourceLaws.CachedTunables))]
  public void A_value_copied_in_Initialize_is_named() =>
    Assert.Equal(
      "Planted0.cs:4: PipeEntity._leak; copies ExlibValues.GasLeakRate in Initialize"
        + Unreached,
      Assert.Single(
        Scan(
          f => SourceLaws.CachedTunables(f, Exlib),
          "class PipeEntity : BlockEntity {\n"
            + "  public override void Initialize(ICoreAPI api) {\n"
            + "    base.Initialize(api);\n"
            + "    _leak = ExlibValues.GasLeakRate * 2f;\n"
            + "  }\n}"
        )
      )
    );

  // Fails when CachedTunables skips a field or property initialiser, or loses a property's name
  // behind its accessor block.
  [Fact]
  [PlantedDefect(typeof(SourceLaws), nameof(SourceLaws.CachedTunables))]
  public void Initialisers_are_named() =>
    Assert.Equal(
      [
        "Planted0.cs:2: Tank.Limit; copies ExlibValues.LitresPerPipe at construction"
          + Unreached,
        "Planted0.cs:3: Tank.Capacity; copies ExlibValues.MoltenFlowRate at construction"
          + Unreached,
        "Planted0.cs:4: Tank.Rates; copies ExlibValues.GasLeakRate at construction"
          + Unreached,
      ],
      Scan(
        f => SourceLaws.CachedTunables(f, Exlib),
        "class Tank {\n"
          + "  static readonly float Limit = ExlibValues.LitresPerPipe;\n"
          + "  [ProtoMember(1)] public int Capacity { get; set; } = ExlibValues.MoltenFlowRate;\n"
          + "  Dictionary<string, float> Rates = new() { [\"gas\"] = ExlibValues.GasLeakRate };\n"
          + "}"
      )
    );

  // Fails when CachedTunables stops following a method the constructor calls, follows one only a
  // lambda calls, or names a local.
  [Fact]
  [PlantedDefect(typeof(SourceLaws), nameof(SourceLaws.CachedTunables))]
  public void A_copy_in_a_method_the_constructor_calls_is_named() =>
    Assert.Equal(
      "Planted0.cs:6: Stove._max; copies ExlibValues.AmbientTemperature in Cache, called from "
        + "the constructor"
        + Unreached,
      Assert.Single(
        Scan(
          f => SourceLaws.CachedTunables(f, Exlib),
          "class Stove {\n"
            + "  public Stove() { Cache(); Listen(() => Tick()); }\n"
            + "  void Tick() { _heat = ExlibValues.MoltenFlowRate; }\n"
            + "  void Cache() {\n"
            + "    float local = ExlibValues.GasLeakRate;\n"
            + "    _max = ExlibValues.AmbientTemperature;\n"
            + "  }\n}"
        )
      )
    );

  // Fails when CachedTunables skips a ModSystem's Start or StartServerSide, stops following a base
  // through the files to ModSystem, or reads Start on a type that is no ModSystem.
  [Fact]
  [PlantedDefect(typeof(SourceLaws), nameof(SourceLaws.CachedTunables))]
  public void A_copy_in_a_mod_system_start_is_named() =>
    Assert.Equal(
      [
        "Planted0.cs:3: FamilySystem.Registry.Fallback; copies ExlibValues.MoltenFlowRate in "
          + "Start"
          + Unreached,
        "Planted0.cs:10: SteelSystem._rate; copies ExlibValues.GasLeakRate in StartServerSide"
          + Unreached,
      ],
      Scan(
        f => SourceLaws.CachedTunables(f, Exlib),
        "class FamilySystem : ModSystem {\n"
          + "  public override void Start(ICoreAPI api) {\n"
          + "    Registry.Fallback = ExlibValues.MoltenFlowRate;\n"
          + "    Register(() => ExlibValues.LitresPerPipe);\n"
          + "  }\n}\n"
          + "class SteelSystem : FamilySystem {\n"
          + "  public override void StartServerSide(ICoreServerAPI api) {\n"
          + "    base.StartServerSide(api);\n"
          + "    _rate = ExlibValues.GasLeakRate;\n"
          + "  }\n}\n"
          + "class Machine {\n"
          + "  public void Start() { _heat = ExlibValues.AmbientTemperature; }\n}"
      )
    );

  // Fails when CachedTunables names a live read (an expression body, a lambda, a local, another
  // method) or a store registered without Manageable.
  [Fact]
  public void Live_reads_and_unmanaged_stores_pass() =>
    Assert.Empty(
      Scan(
        f =>
          SourceLaws.CachedTunables(
            f,
            [typeof(ExlibConfig).Assembly, typeof(SourceLawsTests).Assembly]
          ),
        "class Pipe : BlockEntity {\n"
          + "  float Leak => ExlibValues.GasLeakRate;\n"
          + "  Func<float> Rate = () => ExlibValues.LitresPerPipe;\n"
          + "  int Seed = ExModSystemTestValues.Tunable;\n"
          + "  public override void Initialize(ICoreAPI api) {\n"
          + "    RegisterGameTickListener(dt => _flow = ExlibValues.MoltenFlowRate, 100);\n"
          + "    float cap = ExlibValues.LitresPerPipe;\n"
          + "    _seed = ExModSystemTestValues.Tunable;\n"
          + "  }\n"
          + "  void Tick() { _flow = ExlibValues.MoltenFlowRate; }\n}"
      )
    );

  // Fails when CachedTunables runs with no manageable store to read instead of throwing.
  [Fact]
  public void Assemblies_with_no_manageable_store_throw() =>
    Assert.Contains(
      "Manageable",
      Assert
        .Throws<ArgumentException>(() =>
          SourceLaws.CachedTunables([], [typeof(SourceLawsTests).Assembly])
        )
        .Message
    );

  #endregion

  #region DisplayOnlyTunables

  private const string TextOnly =
    "; every read sits inside a Lang.Get argument list, so the value changes the text and "
    + "nothing else";

  // Fails when DisplayOnlyTunables accepts a value every read of which is a Lang.Get argument, or
  // counts a nameof as a read.
  [Fact]
  [PlantedDefect(typeof(SourceLaws), nameof(SourceLaws.DisplayOnlyTunables))]
  public void A_value_only_the_text_reads_is_named() =>
    Assert.Equal(
      "Planted0.cs:3: ExlibValues.EvaporationLitresPerDay" + TextOnly,
      Assert.Single(
        Scan(
          f => SourceLaws.DisplayOnlyTunables(f, Exlib),
          "class Converter {\n"
            + "  string Info(int units) =>\n"
            + "    Lang.Get(\"x:scrap\", ExlibValues.EvaporationLitresPerDay * units);\n"
            + "  string Key = nameof(ExlibValues.EvaporationLitresPerDay);\n"
            + "  float Leak() => ExlibValues.GasLeakRate * Lang.Get(\"x\").Length;\n}"
        )
      )
    );

  // Fails when DisplayOnlyTunables stops reading a property that stands for the value as the
  // value.
  [Fact]
  [PlantedDefect(typeof(SourceLaws), nameof(SourceLaws.DisplayOnlyTunables))]
  public void A_value_shown_through_a_property_that_stands_for_it_is_named() =>
    Assert.Equal(
      "Planted1.cs:1: ExlibValues.PipeOverpressureSeconds" + TextOnly,
      Assert.Single(
        Scan(
          f => SourceLaws.DisplayOnlyTunables(f, Exlib),
          "class Control {\n"
            + "  private static float Grace => ExlibValues.PipeOverpressureSeconds;\n}",
          "string Info() => Lang.Get(\"x:grace\", Control.Grace);"
        )
      )
    );

  // Fails when DisplayOnlyTunables names a value the simulation reads, directly or through a
  // property that stands for it.
  [Fact]
  public void A_value_the_simulation_reads_passes() =>
    Assert.Empty(
      Scan(
        f => SourceLaws.DisplayOnlyTunables(f, Exlib),
        "class Control {\n"
          + "  static float Grace => ExlibValues.PipeOverpressureSeconds;\n"
          + "  string Info() => Lang.Get(\"k\", Grace, ExlibValues.GasLeakRate);\n"
          + "  bool Burst(float t) => t > Grace;\n"
          + "  float Leak() => ExlibValues.GasLeakRate;\n}"
      )
    );

  #endregion

  #region UnreadTunables

  private const string Unread =
    "; no source reads it, so the setting changes nothing";

  private const string ExlibConfigSource =
    "class ExlibConfig {\n"
    + "  public float GasLeakRate { get; set; } = 8f;\n"
    + "  public float LitresPerPipe { get; set; } = 30f;\n}";

  private IReadOnlyList<string> Unreads() =>
    Scan(
      f => SourceLaws.UnreadTunables(f, Exlib),
      ExlibConfigSource,
      "class Pipe {\n"
        + "  float Leak() => ExlibValues.LiquidLeakRate;\n"
        + "  float Flow(ExlibConfig c) => c.MoltenFlowRate;\n"
        + "  string Info() => Lang.Get(\"k\", ExlibValues.MoltenMinFlowAmount);\n"
        + "  static float Grace => ExlibValues.PipeOverpressureSeconds;\n"
        + "  bool Burst(float t) => t > Grace;\n"
        + "  void Edit() => ExlibValues.Edit(c => c.GasLeakRate = 2f);\n"
        + "  string Key = nameof(ExlibConfig.LitresPerPipe);\n"
        + "  static float Idle => ExlibValues.MpIdleTorque;\n"
        + "  protected override float Dry => ExlibValues.EvaporationLitresPerDay;\n}"
    );

  // Fails when UnreadTunables accepts a value no file reads, counts a write, a nameof on the config
  // type or an unused property standing for it as a read, or loses the declaration's line.
  [Fact]
  [PlantedDefect(typeof(SourceLaws), nameof(SourceLaws.UnreadTunables))]
  public void A_value_no_source_reads_is_named() {
    IReadOnlyList<string> found = Unreads();

    Assert.Contains("Planted0.cs:2: ExlibValues.GasLeakRate" + Unread, found);
    Assert.Contains("Planted0.cs:3: ExlibValues.LitresPerPipe" + Unread, found);
    Assert.Contains(
      "Planted0.cs:1: ExlibValues.AmbientTemperature" + Unread,
      found
    );
    Assert.Contains("Planted0.cs:1: ExlibValues.MpIdleTorque" + Unread, found);
  }

  // Fails when UnreadTunables names a value read through the accessor, a config instance, a
  // Lang.Get argument, a property that stands for it, or an override its base reads.
  [Fact]
  public void Values_read_any_way_pass() {
    IReadOnlyList<string> found = Unreads();

    foreach (
      string value in new[]
      {
        "LiquidLeakRate",
        "MoltenFlowRate",
        "MoltenMinFlowAmount",
        "PipeOverpressureSeconds",
        "EvaporationLitresPerDay",
      }
    )
      Assert.DoesNotContain(found, f => f.Contains($"ExlibValues.{value};"));
  }

  // Fails when UnreadTunables runs over files that do not declare the config type instead of
  // throwing.
  [Fact]
  public void Files_without_the_config_type_throw() =>
    Assert.Contains(
      "ExlibConfig",
      Assert
        .Throws<ArgumentException>(() =>
          Scan(f => SourceLaws.UnreadTunables(f, Exlib), "class Pipe { }")
        )
        .Message
    );

  #endregion

  #region ContainerDialogPackets

  // Fails when ContainerDialogPackets stops following a base through the files, skips a dialog
  // declared without a body, or accepts a container that opens a dialog and handles no packet.
  [Fact]
  [PlantedDefect(typeof(SourceLaws), nameof(SourceLaws.ContainerDialogPackets))]
  public void A_container_dialog_with_no_packet_handler_is_named() =>
    Assert.Equal(
      "Planted1.cs:2: HopperEntity; opens HopperDialog with no OnReceivedClientPacket override, "
        + "so its slot clicks never reach the server",
      Assert.Single(
        Scan(
          SourceLaws.ContainerDialogPackets,
          "abstract class TankEntity : BlockEntityContainer { }\n"
            + "class HopperDialog(string t) : GuiDialogBlockEntity(t);",
          "class HopperEntity : TankEntity {\n"
            + "  void Open() => _dialog = new HopperDialog(\"t\", Inventory, Pos, capi);\n}"
        )
      )
    );

  // Fails when ContainerDialogPackets passes a dialog opener whose base it cannot read.
  [Fact]
  [PlantedDefect(typeof(SourceLaws), nameof(SourceLaws.ContainerDialogPackets))]
  public void A_dialog_opener_on_an_unread_base_is_named() =>
    Assert.EndsWith(
      "ForgeEntity; opens ForgeDialog, but its base ModdedContainer is in neither the files nor "
        + "the game or exlib, so its packet handling cannot be read",
      Assert.Single(
        Scan(
          SourceLaws.ContainerDialogPackets,
          "class ForgeDialog : GuiDialogBlockEntity { }\n"
            + "class ForgeEntity : ModdedContainer {\n"
            + "  void Open() => new ForgeDialog(t, inv, Pos, capi);\n}"
        )
      )
    );

  // Fails when ContainerDialogPackets reads a dialog it cannot resolve as no dialog, whether it is
  // outside the files or declared in them over a base it cannot resolve.
  [Fact]
  [PlantedDefect(typeof(SourceLaws), nameof(SourceLaws.ContainerDialogPackets))]
  public void A_dialog_it_cannot_resolve_is_named() =>
    Assert.Equal(
      [
        "Planted0.cs:2: KilnEntity" + UnreadDialog("ForeignDialog"),
        "Planted0.cs:6: OvenEntity" + UnreadDialog("OvenDialog"),
      ],
      Scan(
        SourceLaws.ContainerDialogPackets,
        "class KilnEntity : BlockEntityContainer {\n"
          + "  void Open() => new ForeignDialog(t, inv, Pos, capi);\n}\n"
          + "class OvenDialog : ModdedDialogBase { }\n"
          + "class OvenEntity : BlockEntityContainer {\n"
          + "  void Open() => new OvenDialog(t, inv, Pos, capi);\n}"
      )
    );

  // Fails when ContainerDialogPackets names a game dialog that is no block entity dialog, a
  // Dialogue type, or an unresolved dialog on a container that handles its packets.
  [Fact]
  public void Resolved_dialogs_dialogue_and_handled_packets_pass() =>
    Assert.Empty(
      Scan(
        SourceLaws.ContainerDialogPackets,
        "class ConfirmEntity : BlockEntityContainer {\n"
          + "  void Ask() => new GuiDialogConfirm(capi, \"t\", ok => { });\n}\n"
          + "class TalkEntity : BlockEntityContainer {\n"
          + "  object Talk() => new DialogueConfig();\n}\n"
          + "class HeldEntity : BlockEntityContainer {\n"
          + "  void Open() => new ForeignDialog(t, inv, Pos, capi);\n"
          + "  public override void OnReceivedClientPacket(IPlayer p, int id, byte[] d) { }\n}"
      )
    );

  private static string UnreadDialog(string dialog) =>
    $"; opens {dialog} with no OnReceivedClientPacket override, and {dialog} is in neither the "
    + "files nor the game or exlib, so whether its slot clicks reach the server cannot be read";

  // Fails when ContainerDialogPackets names a type that handles its packets itself, through a
  // base in the files or through a game or exlib base, or that is no container.
  [Fact]
  public void Handled_packets_and_other_types_pass() =>
    Assert.Empty(
      Scan(
        SourceLaws.ContainerDialogPackets,
        "class PanDialog : GuiDialogBlockEntity { }\n"
          + "class PanEntity : BlockEntityContainer {\n"
          + "  void Open() => new PanDialog(t, inv, Pos, capi);\n"
          + "  public override void OnReceivedClientPacket(IPlayer p, int id, byte[] d) { }\n}\n"
          + "abstract class Station : BlockEntityContainer {\n"
          + "  public sealed override void OnReceivedClientPacket(IPlayer p, int i, byte[] d) { }\n"
          + "}\n"
          + "class Lathe : Station { void Open() => new PanDialog(t, inv, Pos, capi); }\n"
          + "class Bench : BlockEntityMachineStation {\n"
          + "  object D() => new PanDialog(t, inv, Pos, capi);\n}\n"
          + "class Chest : BlockEntityOpenableContainer {\n"
          + "  void Open() => new PanDialog(t, inv, Pos, capi);\n}\n"
          + "class Sign : BlockEntity { void Open() => new PanDialog(t, inv, Pos, capi); }\n"
          + "class Crate : BlockEntityContainer { object Fill() => new ItemStack(block); }"
      )
    );

  #endregion

  #region InlineParticles

  private const string Inline =
    "; builds SimpleParticleProperties inline; take the effect from ExParticles";

  // Fails when InlineParticles accepts a SimpleParticleProperties built inline, names the outer
  // type for a nested one, or reads one in a comment or string.
  [Fact]
  [PlantedDefect(typeof(SourceLaws), nameof(SourceLaws.InlineParticles))]
  public void Particles_built_inline_are_named() =>
    Assert.Equal(
      ["Planted0.cs:3: Smoke" + Inline, "Planted0.cs:5: Chimney" + Inline],
      Scan(
        SourceLaws.InlineParticles,
        "class Chimney {\n"
          + "  class Smoke {\n"
          + "    static readonly object P = new SimpleParticleProperties(1, 2, c);\n"
          + "  }\n"
          + "  void Puff() => Spawn(new SimpleParticleProperties { MinQuantity = 1 });\n"
          + "  // new SimpleParticleProperties(\n"
          + "  string s = \"new SimpleParticleProperties(\";\n}"
      )
    );

  // Fails when InlineParticles names ExParticles itself, or loses the file's name for a
  // construction outside every type.
  [Fact]
  public void ExParticles_is_exempt_and_file_scope_takes_the_file_name() {
    using var files = new PlantedFiles();
    string home = files.Write(
      "src/ExpandedLib.Industry/Helpers/ExParticles.cs",
      "static class ExParticles { object P = new SimpleParticleProperties(1, 2, c); }"
    );
    string script = files.Write(
      "Script.cs",
      "var p = new SimpleParticleProperties(1, 2, c);"
    );

    Assert.Equal(
      "Script.cs:1: Script" + Inline,
      Assert.Single(SourceLaws.InlineParticles([home, script]))
    );
  }

  #endregion

  // Fails when Key returns a key for a line with no subject or no reason instead of throwing.
  [Theory]
  [InlineData("ExOrientation.cs:181 FromCode(side); returns null")]
  [InlineData("ExOrientation.cs: FromCode(side) returns null")]
  [InlineData("no finding at all")]
  public void Key_throws_on_a_line_that_is_no_finding(string line) =>
    Assert.Throws<ArgumentException>(() => SourceLaws.Key(line));

  [Fact]
  public void Key_is_the_file_and_subject() =>
    Assert.Equal(
      "ExOrientation.cs: BlockFacing.FromCode(side)",
      SourceLaws.Key(
        "ExOrientation.cs:181: BlockFacing.FromCode(side); returns null; fall back"
      )
    );
}
