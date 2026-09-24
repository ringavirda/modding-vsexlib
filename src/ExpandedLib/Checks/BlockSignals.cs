using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Networks;
using ExpandedLib.Structures;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;
using Vintagestory.ServerMods;

namespace ExpandedLib.Checks;

/// <summary>What one registered block declares, read off the block and the class registry it was
/// loaded with: the facts the harness laws, the loaded checks and the live generic layer choose
/// their cases by.</summary>
/// <remarks>A block entity class the registry holds is constructed once, without an api, so that
/// the behaviours its constructor adds are seen beside the ones the block declares; each declared
/// or footprint-hosted behaviour is constructed on a bare host entity of the block. A footprint's
/// hosted behaviours and ports count as the block's own.</remarks>
internal sealed class BlockSignals {
  /// <summary>The entity behaviour name a construction's stages are declared under.</summary>
  internal const string ConstructionBehavior = "ExRightClickConstructable";

  /// <summary>The network type of an mpenergy run.</summary>
  internal const string MpEnergy = "mpenergy";

  private BlockSignals(Block block) => Block = block;

  /// <summary>The block read.</summary>
  internal Block Block { get; }

  /// <summary>The block's own class.</summary>
  internal Type BlockClass => Block.GetType();

  /// <summary>The entity class key the block names, or null.</summary>
  internal string? EntityClass => Block.EntityClass;

  /// <summary>The type the registry holds under <see cref="EntityClass"/>, or null when the block
  /// names none or the registry holds no such key.</summary>
  internal Type? EntityType { get; private init; }

  /// <summary>The type names of the block's behaviours, in declaration order.</summary>
  internal IReadOnlyList<string> BlockBehaviors { get; private init; } = [];

  /// <summary>The block entity's behaviours: the declared ones by name, then the ones its
  /// constructor adds by type name.</summary>
  internal IReadOnlyList<string> EntityBehaviors { get; private init; } = [];

  /// <summary>The variant groups the block's placement writes: its orientation groups
  /// (<see cref="OrientationGroups"/>) and a network node's <c>orientation</c>.</summary>
  internal IReadOnlyList<string> PlacedGroups { get; private init; } = [];

  /// <summary>The footprint cells a filler host declares in the north frame, empty for a block
  /// that is no <see cref="IFillerHost"/> or declares no <c>fillerOffsets</c>.</summary>
  internal IReadOnlyList<FillerOffset> Footprint { get; private init; } = [];

  /// <summary>The <c>multiblockStructure</c> attribute of a block whose entity is a
  /// <see cref="BlockEntityMultiblockStructure"/>, else null.</summary>
  internal JsonObject? Layout { get; private init; }

  /// <summary>The layout offsets marked <see cref="CellRoles.NoSnow"/>, north frame; empty without
  /// a <see cref="Layout"/>.</summary>
  internal IReadOnlyCollection<(int X, int Y, int Z)> NoSnowCells {
    get;
    private init;
  } = [];

  /// <summary>How many construction stages the <see cref="ConstructionBehavior"/> entity behaviour
  /// declares; 0 without one.</summary>
  internal int Stages { get; private init; }

  /// <summary>The network types the block joins, distinct, in the order found: a
  /// <see cref="BlockNetworkNode"/>'s own, each network membership of its entity or a hosted cell,
  /// and each footprint port.</summary>
  internal IReadOnlyList<string> Networks { get; private init; } = [];

  /// <summary>Whether the block, its entity or a hosted cell connects to vanilla mechanical power:
  /// the block is an <see cref="IMechanicalPowerBlock"/> other than the structure filler, or a
  /// behaviour of its entity or a hosted cell is a <see cref="BEBehaviorMPBase"/>.</summary>
  internal bool MpConnector { get; private init; }

  /// <summary>Whether the block entity holds an inventory
  /// (<see cref="IBlockEntityContainer"/>).</summary>
  internal bool Inventory { get; private init; }

  /// <summary>Whether the block joins an mpenergy run.</summary>
  internal bool MpEnergyMember => Networks.Contains(MpEnergy);

  /// <summary>The signal names this block carries, in census order.</summary>
  internal IEnumerable<string> Names() {
    if (BlockClass != typeof(Block))
      yield return "block class";
    if (EntityClass != null)
      yield return "entity class";
    if (BlockBehaviors.Count + EntityBehaviors.Count > 0)
      yield return "behaviours";
    if (PlacedGroups.Count > 0)
      yield return "placed groups";
    if (Footprint.Count > 0)
      yield return "footprint";
    if (Layout != null)
      yield return "layout";
    if (NoSnowCells.Count > 0)
      yield return "nosnow cells";
    if (Stages > 0)
      yield return "stages";
    if (Networks.Count > 0)
      yield return "network";
    if (MpEnergyMember)
      yield return "mpenergy";
    if (MpConnector)
      yield return "mp connector";
    if (Inventory)
      yield return "inventory";
  }

  /// <summary>Every name <see cref="Names"/> can yield, in census order.</summary>
  internal static IReadOnlyList<string> AllNames() =>
    [
      "block class",
      "entity class",
      "behaviours",
      "placed groups",
      "footprint",
      "layout",
      "nosnow cells",
      "stages",
      "network",
      "mpenergy",
      "mp connector",
      "inventory",
    ];

  /// <summary>Reads the signals of <paramref name="block"/>.</summary>
  /// <param name="classes">The registry the block was loaded with; its entity and behaviour keys
  /// resolve there.</param>
  /// <exception cref="InvalidOperationException">The block's entity class or one of its
  /// behaviours throws while constructed; the message names the block.</exception>
  internal static BlockSignals Of(Block block, IClassRegistryAPI classes) {
    try {
      return Read(block, classes);
    } catch (Exception e) when (e is not InvalidOperationException) {
      throw new InvalidOperationException(
        $"Reading the signals of {block.Code} threw: {e.Message}",
        e
      );
    }
  }

  private static BlockSignals Read(Block block, IClassRegistryAPI classes) {
    Type? entityType = block.EntityClass is { } key
      ? classes.GetBlockEntity(key)
      : null;
    BlockEntity? entity =
      entityType != null ? classes.CreateBlockEntity(block.EntityClass) : null;
    BlockEntityBehaviorType[] declared = block.BlockEntityBehaviors ?? [];
    List<BlockEntityBehavior> added = entity?.Behaviors.ToList() ?? [];

    var host = new Host { Block = block, Pos = new BlockPos(0, 0, 0) };
    var behaviors =
      new List<(BlockEntityBehavior Behavior, JsonObject? Properties)>();
    foreach (BlockEntityBehaviorType type in declared)
      if (Construct(classes, host, type.Name) is { } made)
        behaviors.Add((made, type.properties));
    behaviors.AddRange(added.Select(b => (b, (JsonObject?)null)));

    List<FillerOffset> footprint = block is IFillerHost filler
      ? StructureFillers.ReadOffsets(filler.FillerOffsets)
      : [];
    var hosted =
      new List<(BlockEntityBehavior Behavior, JsonObject? Properties)>();
    foreach (FillerOffset cell in footprint)
      foreach (FillerBehavior spec in cell.Behaviors ?? [])
        if (Construct(classes, host, spec.Code) is { } made)
          hosted.Add((made, spec.Properties));

    var networks = new List<string>();
    if (block is BlockNetworkNode node)
      networks.Add(node.NetworkType);
    foreach (var (behavior, properties) in behaviors.Concat(hosted))
      if (behavior is INetworkMember member)
        networks.Add(
          string.IsNullOrEmpty(member.NetworkType)
            ? properties?["networkType"].AsString() ?? ""
            : member.NetworkType
        );
    networks.AddRange(footprint.Select(c => c.PortNetworkType ?? ""));

    bool layout =
      entityType != null
      && typeof(BlockEntityMultiblockStructure).IsAssignableFrom(entityType)
      && block.Attributes?["multiblockStructure"].Exists == true;

    return new BlockSignals(block) {
      EntityType = entityType,
      BlockBehaviors =
      [
        .. (block.BlockBehaviors ?? []).Select(b => b.GetType().Name),
      ],
      EntityBehaviors =
      [
        .. declared.Select(b => b.Name),
        .. added.Select(b => b.GetType().Name),
      ],
      PlacedGroups = [.. PlacedGroupsOf(block)],
      Footprint = footprint,
      Layout = layout ? block.Attributes!["multiblockStructure"] : null,
      NoSnowCells = layout
        ? MultiblockCellRoles
          .FromAttributes(block.Attributes)
          .CellsOf(CellRoles.NoSnow)
        : [],
      Stages = StagesOf(declared),
      Networks = [.. networks.Where(n => n.Length > 0).Distinct()],
      MpConnector =
        (block is IMechanicalPowerBlock && block is not BlockStructureFiller)
        || behaviors.Concat(hosted).Any(b => b.Behavior is BEBehaviorMPBase),
      Inventory =
        entityType != null
        && typeof(IBlockEntityContainer).IsAssignableFrom(entityType),
    };
  }

  /// <summary>The behaviour the registry holds under <paramref name="name"/>, constructed on
  /// <paramref name="host"/>; null when it holds none.</summary>
  private static BlockEntityBehavior? Construct(
    IClassRegistryAPI classes,
    BlockEntity host,
    string name
  ) =>
    classes.GetBlockEntityBehaviorClass(name) != null
      ? classes.CreateBlockEntityBehavior(host, name)
      : null;

  private static int StagesOf(BlockEntityBehaviorType[] declared) =>
    declared
      .Where(b => b.Name == ConstructionBehavior)
      .Select(b => b.properties?["stages"].Token as JArray)
      .FirstOrDefault()
      ?.Count
    ?? 0;

  /// <summary>The variant groups <paramref name="block"/>'s placement writes: its orientation
  /// groups, and <see cref="BlockBehaviorExOrientable.OrientationVariant"/> for a network node,
  /// whose placement and neighbours rewrite its connector faces.</summary>
  internal static IEnumerable<string> PlacedGroupsOf(Block block) {
    var placed = new List<string>(OrientationGroups(block));
    if (
      block is BlockNetworkNode
      && !placed.Contains(BlockBehaviorExOrientable.OrientationVariant)
    )
      placed.Add(BlockBehaviorExOrientable.OrientationVariant);
    return placed;
  }

  /// <summary>The variant groups a loaded block's orientation behaviours and block class write
  /// when it is placed, among the groups it carries.</summary>
  internal static IEnumerable<string> OrientationGroups(Block block) {
    bool Has(string name) => block.Variant?.ContainsKey(name) == true;
    foreach (string group in ClassGroups(block.GetType()).Where(Has))
      yield return group;
    foreach (BlockBehavior behavior in block.BlockBehaviors ?? []) {
      string? group = behavior switch {
        BlockBehaviorExOrientable ex => ex.VariantKey,
        BlockBehaviorHorizontalOrientable => Has("horizontalorientation")
          ? "horizontalorientation"
          : "side",
        BlockBehaviorNWOrientable => Has("orientation")
          ? "orientation"
          : "side",
        BlockBehaviorPillar pillar => (string?)
          (pillar.propertiesAtString is { } json ? JObject.Parse(json) : null)?[
            "rotationVariantCode"
          ]
          ?? "rotation",
        BlockBehaviorOmniRotatable => "rot",
        _ => null,
      };
      if (group != null && Has(group))
        yield return group;
    }
  }

  /// <summary>The groups a block class writes when it places its block
  /// (<c>BlockStairs.TryPlaceBlock</c>); empty for any other class.</summary>
  internal static string[] ClassGroups(Type blockClass) =>
    typeof(BlockStairs).IsAssignableFrom(blockClass)
      ? ["horizontalorientation", "verticalorientation"]
      : [];

  // Stands in for the block entity a declared or hosted behaviour is built on.
  private sealed class Host : BlockEntity { }
}
