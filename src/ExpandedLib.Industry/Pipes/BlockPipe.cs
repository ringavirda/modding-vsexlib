using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.Pipes;

/// <summary>
/// The base pipe block: a self-orienting node of the unified "pipe" network, providing the
/// orientation tables shared by every straight/bend/junction variant.
/// </summary>
[BlockRegister]
public partial class BlockPipe
  : BlockNetworkNode,
    IBurstablePipe,
    IThroughputLimitedPipe,
    IExBlockDefProvider {
  public override string NetworkType => "pipe";

  #region Code-first definitions

  /// <summary>Tier name of the plated pipe family - the early loop's bootstrap rung, 2.5 atm and
  /// flanged.</summary>
  public const string PlatedTier = "plated";

  /// <summary>Tier name of the cast pipe family - the steam-era main, 4.5 atm and flanged.</summary>
  public const string CastTier = "cast";

  /// <summary>Tier name of the rolled pipe family - the HP main, 12 atm and welded.</summary>
  public const string RolledTier = "rolled";

  /// <summary>The defs this class declares itself, used only to derive
  /// <see cref="BlockNetworkNode.AllowedOrientations"/>; empty for the <c>exlib</c> domain.</summary>
  public static IEnumerable<ExBlockDef> Definitions(string domain) =>
    domain == "exlib" ? [] : SetSegments(domain, tier: null);

  /// <summary>The four plain pipe segments (straight / bend / T / X junction) of one
  /// <paramref name="tier"/> under <paramref name="domain"/>; a null <paramref name="tier"/> yields a
  /// tierless family.</summary>
  public static IEnumerable<ExBlockDef> Segments(string domain, string? tier) =>
    [
      Straight(domain, tier),
      Bend(domain, tier),
      TJunction(domain, tier),
      XJunction(domain, tier),
    ];

  /// <summary>The six forms of one <paramref name="tier"/> under <paramref name="domain"/>, in the order straight,
  /// bend, T junction, corner, X junction, junction box: <see cref="Segments"/> with <see cref="Corner"/> and
  /// <see cref="Junction"/> in place. A null <paramref name="tier"/> yields a tierless family.</summary>
  /// <param name="domain">The domain the blocks are declared in.</param>
  /// <param name="tier">The tier name, or null for a tierless family.</param>
  /// <returns>The six definitions.</returns>
  public static IEnumerable<ExBlockDef> SetSegments(
    string domain,
    string? tier
  ) =>
    [
      Straight(domain, tier),
      Bend(domain, tier),
      TJunction(domain, tier),
      Corner(domain, tier),
      XJunction(domain, tier),
      Junction(domain, tier),
    ];

  /// <summary>The asset path a tier's segment is declared and drawn at: <c>pipe/{tier}/{leaf}</c>, or
  /// <c>pipe/{leaf}</c> for a tierless family.</summary>
  internal static string Asset(string? tier, string leaf) =>
    tier == null ? $"pipe/{leaf}" : $"pipe/{tier}/{leaf}";

  // The surface shared by every pipe blocktype; each type overlays its variants/shapes/boxes.
  private static ExBlockDef Common(
    string domain,
    string? tier,
    string assetName,
    int maxStack,
    string creativeSelector
  ) {
    ExBlockDef def = ExBlockDef
      .Create(domain, "pipe", assetName)
      // Shared base class + BE; every tier's segments bind to these keys.
      .Class<BlockPipe>()
      .EntityClass<BlockEntityPipe>()
      .Material(EnumBlockMaterial.Metal)
      .MetalSounds()
      .MaxStackSize(maxStack)
      .CreativeTab("general", creativeSelector)
      .CreativeTab(domain, creativeSelector)
      // Grouped per tier: a groupBy selector with no domain is qualified with the block's own domain.
      .Handbook(HandbookGroups(tier))
      .Behavior("Lockable")
      // No blanket texture override: each tier's shape declares its own texture map.
      .RenderPass("OpaqueNoCull")
      .FaceCullMode("NeverCull")
      .LightAbsorption(0)
      .SideSolid(false)
      .SideOpaque(false);

    // The tier is the high-order axis: `pipe-{tier}-{type}-{orientation}`. Must be declared before
    // each segment adds `type`.
    return tier == null ? def : def.VariantGroup("tier", tier);
  }

  // The handbook groups one blocktype's variants into one entry per segment shape, within a tier.
  private static string[] HandbookGroups(string? tier) {
    string prefix = tier == null ? "pipe-" : $"pipe-{tier}-";
    return
    [
      $"{prefix}straight-*",
      $"{prefix}bend-*",
      $"{prefix}tjunction-*",
      $"{prefix}corner-*",
      $"{prefix}xjunction-*",
      $"{prefix}junction-*",
    ];
  }

  private static ExBlockDef Straight(string domain, string? tier) {
    string s = $"{domain}:" + Asset(tier, "straight");
    return Common(domain, tier, Asset(tier, "straight"), 16, "*-straight-ns")
      .VariantGroup("type", "straight")
      .VariantGroup("orientation", "ns", "we", "ud")
      .NetworkOriented()
      .ShapeByType("*-straight-ns", s)
      .ShapeByType("*-straight-we", s, rotateY: 90)
      .ShapeByType("*-straight-ud", s, rotateX: 90)
      .CollisionBox(0.3125f, 0.3125f, 0f, 0.6875f, 0.6875f, 1f)
      .SelectionBox(0.3125f, 0.3125f, 0f, 0.6875f, 0.6875f, 1f);
  }

  private static ExBlockDef Bend(string domain, string? tier) {
    string s = $"{domain}:" + Asset(tier, "bend");
    return Common(domain, tier, Asset(tier, "bend"), 8, "*-bend-nw")
      .VariantGroup("type", "bend")
      .VariantGroup(
        "orientation",
        "nw",
        "se",
        "en",
        "ws",
        "un",
        "us",
        "uw",
        "ue",
        "dn",
        "ds",
        "dw",
        "de"
      )
      .NetworkOriented()
      .ShapeByType("*-bend-nw", s)
      .ShapeByType("*-bend-en", s, rotateY: 270)
      .ShapeByType("*-bend-se", s, rotateY: 180)
      .ShapeByType("*-bend-ws", s, rotateY: 90)
      .ShapeByType("*-bend-dn", s, rotateZ: 90)
      .ShapeByType("*-bend-de", s, rotateY: 270, rotateZ: 90)
      .ShapeByType("*-bend-ds", s, rotateY: 180, rotateZ: 90)
      .ShapeByType("*-bend-dw", s, rotateY: 90, rotateZ: 90)
      .ShapeByType("*-bend-un", s, rotateZ: 270)
      .ShapeByType("*-bend-ue", s, rotateY: 270, rotateZ: 270)
      .ShapeByType("*-bend-us", s, rotateY: 180, rotateZ: 270)
      .ShapeByType("*-bend-uw", s, rotateY: 90, rotateZ: 270)
      .CollisionBox(0.3125f, 0.3125f, 0f, 0.6875f, 0.6875f, 0.6875f)
      .CollisionBox(0f, 0.3125f, 0.3125f, 0.6875f, 0.6875f, 0.6875f)
      .SelectionBox(0.3125f, 0.3125f, 0f, 0.6875f, 0.6875f, 0.6875f)
      .SelectionBox(0f, 0.3125f, 0.3125f, 0.6875f, 0.6875f, 0.6875f);
  }

  private static ExBlockDef TJunction(string domain, string? tier) {
    string s = $"{domain}:" + Asset(tier, "tjunction");
    return Common(domain, tier, Asset(tier, "tjunction"), 8, "*-tjunction-uns")
      .VariantGroup("type", "tjunction")
      .VariantGroup(
        "orientation",
        "uns",
        "uwe",
        "dns",
        "dwe",
        "nes",
        "esw",
        "swn",
        "wne",
        "dnu",
        "deu",
        "dsu",
        "dwu"
      )
      .NetworkOriented()
      .ShapeByType("*-tjunction-wne", s)
      .ShapeByType("*-tjunction-nes", s, rotateY: 270)
      .ShapeByType("*-tjunction-esw", s, rotateY: 180)
      .ShapeByType("*-tjunction-swn", s, rotateY: 90)
      .ShapeByType("*-tjunction-uwe", s, rotateX: 90)
      .ShapeByType("*-tjunction-uns", s, rotateX: 90, rotateZ: 90)
      .ShapeByType("*-tjunction-dwe", s, rotateX: 270)
      .ShapeByType("*-tjunction-dns", s, rotateX: 270, rotateZ: 90)
      .ShapeByType("*-tjunction-dnu", s, rotateZ: 90)
      .ShapeByType("*-tjunction-deu", s, rotateY: 270, rotateZ: 90)
      .ShapeByType("*-tjunction-dsu", s, rotateY: 180, rotateZ: 90)
      .ShapeByType("*-tjunction-dwu", s, rotateY: 90, rotateZ: 90)
      .CollisionBox(0f, 0.3125f, 0.3125f, 1f, 0.6875f, 0.6875f)
      .CollisionBox(0.3125f, 0.3125f, 0f, 0.6875f, 0.6875f, 0.3125f)
      .SelectionBox(0f, 0.3125f, 0.3125f, 1f, 0.6875f, 0.6875f)
      .SelectionBox(0.3125f, 0.3125f, 0f, 0.6875f, 0.6875f, 0.3125f);
  }

  /// <summary>The corner of one <paramref name="tier"/> under <paramref name="domain"/>: three mutually
  /// perpendicular arms, eight variants (<see cref="ExOrientations.PipeCorner"/>: the vertical letter, then the
  /// horizontal bend's token), one shape, <c>pipe/{tier}/corner</c>, drawn at <c>unw</c> (arms north, west, up) and
  /// turned per token. It is not in <see cref="Segments"/>.</summary>
  /// <param name="domain">The domain the block is declared in.</param>
  /// <param name="tier">The tier name, or null for a tierless family.</param>
  /// <returns>The definition of the corner.</returns>
  public static ExBlockDef Corner(string domain, string? tier) {
    string s = $"{domain}:" + Asset(tier, "corner");
    return Common(domain, tier, Asset(tier, "corner"), 8, "*-corner-unw")
      .VariantGroup("type", "corner")
      .VariantGroup("orientation", [.. ExOrientations.PipeCorner.Tokens])
      .NetworkOriented()
      .ShapeByType("*-corner-unw", s)
      .ShapeByType("*-corner-uen", s, rotateY: 270)
      .ShapeByType("*-corner-use", s, rotateY: 180)
      .ShapeByType("*-corner-uws", s, rotateY: 90)
      .ShapeByType("*-corner-den", s, rotateZ: 180)
      .ShapeByType("*-corner-dnw", s, rotateY: 90, rotateZ: 180)
      .ShapeByType("*-corner-dws", s, rotateY: 180, rotateZ: 180)
      .ShapeByType("*-corner-dse", s, rotateY: 270, rotateZ: 180)
      .CollisionBox(0.25f, 0.25f, 0.25f, 0.75f, 0.75f, 0.75f)
      .CollisionBox(0.3125f, 0.3125f, 0f, 0.6875f, 0.6875f, 0.25f)
      .CollisionBox(0f, 0.3125f, 0.3125f, 0.25f, 0.6875f, 0.6875f)
      .CollisionBox(0.3125f, 0.75f, 0.3125f, 0.6875f, 1f, 0.6875f)
      .SelectionBox(0.25f, 0.25f, 0.25f, 0.75f, 0.75f, 0.75f)
      .SelectionBox(0.3125f, 0.3125f, 0f, 0.6875f, 0.6875f, 0.25f)
      .SelectionBox(0f, 0.3125f, 0.3125f, 0.25f, 0.6875f, 0.6875f)
      .SelectionBox(0.3125f, 0.75f, 0.3125f, 0.6875f, 1f, 0.6875f);
  }

  private static ExBlockDef XJunction(string domain, string? tier) {
    string s = $"{domain}:" + Asset(tier, "xjunction");
    return Common(domain, tier, Asset(tier, "xjunction"), 8, "*-xjunction-nswe")
      .VariantGroup("type", "xjunction")
      .VariantGroup("orientation", "nswe", "nsud", "weud")
      .NetworkOriented()
      .ShapeByType("*-xjunction-nswe", s)
      .ShapeByType("*-xjunction-nsud", s, rotateZ: 90)
      .ShapeByType("*-xjunction-weud", s, rotateY: 90, rotateZ: 90)
      .CollisionBox(0f, 0.3125f, 0.3125f, 1f, 0.6875f, 0.6875f)
      .CollisionBox(0.3125f, 0.3125f, 0f, 0.6875f, 0.6875f, 1f)
      .SelectionBox(0f, 0.3125f, 0.3125f, 1f, 0.6875f, 0.6875f)
      .SelectionBox(0.3125f, 0.3125f, 0f, 0.6875f, 0.6875f, 1f);
  }

  /// <summary>The junction box of one <paramref name="tier"/> under <paramref name="domain"/>: one casting with a
  /// neck on all six faces, one variant (<c>nsweud</c>) and one shape, <c>pipe/{tier}/junction</c>. Its six
  /// connectors are always shown; a neck no pipe joins is capped, closed by a solid wall, or leaks. It is not in
  /// <see cref="Segments"/>.</summary>
  /// <param name="domain">The domain the block is declared in.</param>
  /// <param name="tier">The tier name, or null for a tierless family.</param>
  /// <returns>The definition of the box.</returns>
  public static ExBlockDef Junction(string domain, string? tier) {
    string s = $"{domain}:" + Asset(tier, "junction");
    ExBlockDef def = Common(
        domain,
        tier,
        Asset(tier, "junction"),
        8,
        "*-junction-nsweud"
      )
      .VariantGroup("type", "junction")
      .VariantGroup("orientation", "nsweud")
      .ShapeByType("*-junction-nsweud", s)
      .CollisionBox(0.25f, 0.25f, 0.25f, 0.75f, 0.75f, 0.75f)
      .SelectionBox(0.25f, 0.25f, 0.25f, 0.75f, 0.75f, 0.75f);
    foreach (
      (float x1, float y1, float z1, float x2, float y2, float z2) in ArmBoxes
    )
      def = def.CollisionBox(x1, y1, z1, x2, y2, z2)
        .SelectionBox(x1, y1, z1, x2, y2, z2);
    return def;
  }

  // One box from each face of the cell to the body, 0.3125..0.6875 across.
  private static readonly (
    float X1,
    float Y1,
    float Z1,
    float X2,
    float Y2,
    float Z2
  )[] ArmBoxes =
  [
    (0.3125f, 0.3125f, 0f, 0.6875f, 0.6875f, 0.25f),
    (0.3125f, 0.3125f, 0.75f, 0.6875f, 0.6875f, 1f),
    (0.75f, 0.3125f, 0.3125f, 1f, 0.6875f, 0.6875f),
    (0f, 0.3125f, 0.3125f, 0.25f, 0.6875f, 0.6875f),
    (0.3125f, 0.75f, 0.3125f, 0.6875f, 1f, 0.6875f),
    (0.3125f, 0f, 0.3125f, 0.6875f, 0.25f, 0.6875f),
  ];

  /// <summary>The box keeps an arm open where its neighbour refuses the joint, so hand assembly offers it beside a
  /// pipe of another joint family.</summary>
  internal override bool KeepsRefusedArmsOpen => Type == "junction";

  /// <summary>The asset path of the cap overlay drawn on a capped end: <c>pipe/{tier}/cap</c>, or
  /// <c>pipe/cap</c> for a tierless family, in this block's domain. The asset is the cap overlay drawing,
  /// <c>fittings/caps/pipe-block-{tier}-cap</c>, meshed with this block as its texture source.</summary>
  internal string CapAsset => Asset(Tier, "cap");

  private readonly Dictionary<int, MeshData[]> _capMeshes = [];
  private bool _capShapeLogged;

  /// <summary>The meshes of the cap overlays of a capped-face set, built once per set and kept. Client only.</summary>
  /// <param name="capi">The client API, to read the cap shape and log.</param>
  /// <param name="tesselator">The tesselator the shape is meshed with.</param>
  /// <param name="cappedFaces">The faces of the piece that carry a cap.</param>
  /// <returns>One mesh per capped face; empty when no face is capped or this tier ships no cap shape, which logs once.</returns>
  internal MeshData[] CapMeshes(
    ICoreClientAPI capi,
    ITesselatorAPI tesselator,
    IReadOnlyList<BlockFacing> cappedFaces
  ) {
    int key = PipeCapOverlays.Key(cappedFaces);
    if (key == 0)
      return [];
    if (_capMeshes.TryGetValue(key, out MeshData[]? kept))
      return kept;

    var loc = new AssetLocation(Code.Domain, $"shapes/{CapAsset}.json");
    Shape? shape = capi.Assets.TryGet(loc)?.ToObject<Shape>();
    if (shape == null && !_capShapeLogged) {
      _capShapeLogged = true;
      capi.Logger.Warning(
        "[exlib] {0}: no cap shape at {1}; capped ends draw without a cap.",
        Code,
        loc
      );
    }
    MeshData[] meshes =
    [
      .. shape == null
        ? []
        : PipeCapOverlays
          .For(cappedFaces)
          .Select(overlay =>
          {
            tesselator.TesselateShape(
              this,
              shape,
              out MeshData mesh,
              new Vec3f(overlay.RotateXDeg, overlay.RotateYDeg, 0)
            );
            return mesh;
          }),
    ];
    _capMeshes[key] = meshes;
    return meshes;
  }

  #endregion

  #region Tier

  /// <summary>The pipe family this block belongs to, from its <c>tier</c> variant, or null for a pipe
  /// declaring no tier axis.</summary>
  public virtual string? Tier => Variant["tier"];

  #endregion

  #region Burst rating (per-tier)

  // Each pipe tier registers its plain-segment burst pressure from its own config in ModSystem.Start.
  private static readonly Dictionary<string, Func<float>> _burstByTier = new();

  private const float DefaultBurstPressure = 5f;

  /// <summary>Drops every tier's burst, throughput (plain and per medium) and joint registration; run when a world starts
  /// loading, before the tiers' mods register again.</summary>
  internal static void ResetForWorld() {
    _burstByTier.Clear();
    _throughputByTier.Clear();
    _throughputByMedium.Clear();
    _jointByTier.Clear();
  }

  /// <summary>Registers the plain-segment burst pressure (atm) for a <paramref name="tier"/>; a
  /// later registration for the tier replaces the earlier one; one from another assembly logs a Warning
  /// naming both assemblies.</summary>
  public static void RegisterBurst(string tier, Func<float> burstPressure) =>
    RegisterTier(_burstByTier, "BlockPipe burst", tier, burstPressure);

  private static void RegisterTier(
    Dictionary<string, Func<float>> table,
    string name,
    string tier,
    Func<float> value
  ) {
    if (
      table.TryGetValue(tier, out Func<float>? previous)
      && Registrant(previous) != Registrant(value)
    )
      RegistryLog.Replaced(name, tier, Registrant(previous), Registrant(value));
    table[tier] = value;
  }

  private static string Registrant(Func<float> getter) =>
    getter.Method.DeclaringType?.Assembly.GetName().Name
    ?? "an unknown assembly";

  /// <summary>Pressure (atm) above which this pipe bursts, read from the per-tier registry keyed by
  /// this block's <see cref="Tier"/>, or <see cref="DefaultBurstPressure"/> when unregistered.</summary>
  public virtual float BurstPressure =>
    Tier != null && _burstByTier.TryGetValue(Tier, out var f)
      ? f()
      : DefaultBurstPressure;

  /// <summary>Whether this pipe takes part in over-pressure failure; only the plain segments of the
  /// base <see cref="BlockPipe"/> class do, by default.</summary>
  public virtual bool CanBurst => GetType() == typeof(BlockPipe);

  #endregion

  #region Throughput (per-tier)

  // How much a tier's pipe passes per second, registered per tier from each mod's ModSystem.
  private static readonly Dictionary<string, Func<float>> _throughputByTier =
    new();

  // Throughput for one medium of a tier, keyed "tier/medium"; read before the tier's value.
  private static readonly Dictionary<string, Func<float>> _throughputByMedium =
    new();

  private const float DefaultThroughput = 120f;

  /// <summary>Registers the throughput (L/s) for a <paramref name="tier"/>; a later registration
  /// for the tier replaces the earlier one; one from another assembly logs a Warning naming both
  /// assemblies.</summary>
  public static void RegisterThroughput(string tier, Func<float> throughput) =>
    RegisterTier(_throughputByTier, "BlockPipe throughput", tier, throughput);

  /// <summary>Registers the throughput (L/s) of a <paramref name="tier"/> for one
  /// <paramref name="medium"/>, the name the network carries (<c>PipeNetworkState.MediumType</c>, such
  /// as "Air" or "Steam"); a run carrying it reads this in place of the tier's
  /// <see cref="RegisterThroughput(string, Func{float})"/> value. A later registration for the pair
  /// replaces the earlier one; one from another assembly logs a Warning naming both.</summary>
  public static void RegisterThroughput(
    string tier,
    string medium,
    Func<float> throughput
  ) =>
    RegisterTier(
      _throughputByMedium,
      "BlockPipe medium throughput",
      $"{tier}/{medium}",
      throughput
    );

  /// <summary>Litres per second this pipe will pass whatever it carries, read from the per-tier
  /// registry keyed by this block's <see cref="Tier"/>, or <see cref="DefaultThroughput"/> when
  /// unregistered; unlimited when <see cref="CanBurst"/> is false.</summary>
  public virtual float MaxThroughput =>
    !CanBurst ? float.MaxValue
    : Tier != null && _throughputByTier.TryGetValue(Tier, out var f) ? f()
    : DefaultThroughput;

  /// <summary>Litres per second this pipe will pass of <paramref name="medium"/>: the value registered
  /// for this block's <see cref="Tier"/> and that medium, else <see cref="MaxThroughput"/>; unlimited
  /// when <see cref="CanBurst"/> is false.</summary>
  /// <param name="medium">Medium name, or <c>null</c> for none, which reads <see cref="MaxThroughput"/>.</param>
  /// <returns>L/s.</returns>
  public virtual float ThroughputFor(string? medium) =>
    CanBurst
    && Tier != null
    && medium != null
    && _throughputByMedium.TryGetValue($"{Tier}/{medium}", out var f)
      ? f()
      : MaxThroughput;

  #endregion

  #region Joint family (which tiers physically couple)

  // A pipe tier's joint, an axis independent of its pressure rating; registered per tier from each
  // mod's ModSystem.
  private static readonly Dictionary<string, string> _jointByTier = new();

  /// <summary>Default joint family: the bolted flange.</summary>
  public const string FlangedJoint = "flanged";

  /// <summary>Joint family of the rolled (HP) tier; it bolts to nothing.</summary>
  public const string WeldedJoint = "welded";

  /// <summary>Registers the joint family for a <paramref name="tier"/>; a later registration for
  /// the tier with another family replaces the earlier one and logs a Warning naming both
  /// families.</summary>
  public static void RegisterJoint(string tier, string jointFamily) {
    if (
      _jointByTier.TryGetValue(tier, out string? previous)
      && previous != jointFamily
    )
      RegistryLog.Replaced(
        "BlockPipe joint",
        tier,
        $"the {previous} joint",
        $"the {jointFamily} joint"
      );
    _jointByTier[tier] = jointFamily;
  }

  /// <summary>The coupling this pipe presents, resolved from the per-tier registry by its own
  /// <see cref="Tier"/>; a pipe whose tier is null or has no registered joint takes the
  /// flange.</summary>
  public virtual string JointFamily =>
    Tier != null && _jointByTier.TryGetValue(Tier, out string? joint)
      ? joint
      : FlangedJoint;

  /// <summary>A pipe couples to another pipe only when both present the same joint; a non-pipe
  /// neighbour is always accepted.</summary>
  public override bool AcceptsNeighbour(Block neighbour) =>
    neighbour is not BlockPipe other || other.JointFamily == JointFamily;

  #endregion

  #region Fixed pieces

  /// <summary>Re-makes a joint of this piece that weeps on the physical pipe run, closing the weep.</summary>
  protected internal override bool WrenchFixed(
    IWorldAccessor world,
    BlockPos pos,
    BlockSelection selection
  ) =>
    FlowRunAt(world, pos) is { } run
    && run.Store.RemakeAt(run.Store.IndexOf(NodeKey.Centre(pos)));

  /// <summary>Whether a joint of this piece is cracked on the physical pipe run.</summary>
  protected internal override bool IsCrackedAt(
    IWorldAccessor world,
    BlockPos pos
  ) =>
    FlowRunAt(world, pos) is { } run
    && (
      run.Store.DamageOf(run.Store.IndexOf(NodeKey.Centre(pos)))
      & PipeFlowStore.CrackedBit
    ) != 0;

  private static PipeFlowNetwork? FlowRunAt(IWorldAccessor world, BlockPos pos) {
    NodeKey key = NodeKey.Centre(pos);
    return
      world
        .Api.ModLoader.GetModSystem<BlockNetworkModSystem>()
        .GetNetworkAt(key)
        is PipeFlowNetwork run
      && run.Store.Nodes.ContainsKey(key)
      ? run
      : null;
  }

  #endregion

  // AllowedOrientations and GetFallbackOrientation are inherited from BlockNetworkNode, derived from
  // this block's own code-first defs resolved by runtime type.
}
