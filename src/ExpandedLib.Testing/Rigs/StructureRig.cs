using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Structures;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ExpandedLib.Testing;

/// <summary>
/// Stands up a mega-block's multiblock footprint in a headless world: every layout cell gets a block
/// whose code satisfies it. Call order: <see cref="Occupy"/>, then <see cref="Raise"/>,
/// <see cref="TestWorld.Initialize"/> and <see cref="AwaitCompletion"/>, or <see cref="Complete"/>.
/// </summary>
public sealed class StructureRig {
  private static readonly AssetLocation AirCode = new("game:air");

  /// <summary>Block ids for the rig's stand-in blocks, kept clear of the low ids fixtures hand out.</summary>
  private const int FirstStandInId = 30000;

  private readonly TestWorld _world;
  private readonly BlockEntityMultiblockStructure _anchor;
  private readonly Dictionary<string, Block> _standIns = new();
  private int _nextId = FirstStandInId;

  /// <summary>The rotation the structure was raised at, in degrees (0 = north).</summary>
  public int Angle { get; }

  /// <summary>
  /// The world the structure stands in, exposed for callers that need to register a block type or
  /// factory, or advance the clock.
  /// </summary>
  public TestWorld World => _world;

  /// <summary>
  /// Every cell of the rotated layout: world position and the (possibly wildcard) code it wants, as
  /// the layout authors it. Oriented parts turn with the structure; other codes keep their authored
  /// form (<see cref="MultiblockFacings"/>).
  /// </summary>
  public IReadOnlyList<(BlockPos Pos, string Wanted)> Cells { get; }

  private StructureRig(
    TestWorld world,
    BlockEntityMultiblockStructure anchor,
    int angle,
    IReadOnlyList<(BlockPos, string)> cells
  ) {
    _world = world;
    _anchor = anchor;
    Angle = angle;
    Cells = cells;
  }

  /// <summary>
  /// Prepares a rig around an already-placed <paramref name="anchor"/>, attaching
  /// <paramref name="def"/>'s attributes to its block.
  /// </summary>
  /// <param name="world">The world <paramref name="anchor"/> stands in; the anchor's api must be
  /// set (<see cref="TestWorld.Attach"/>) before <see cref="Raise"/> or <see cref="Missing"/>.</param>
  /// <param name="anchor">The placed block entity whose missing cells the rig raises.</param>
  /// <param name="def">The definition whose <c>multiblockStructure</c> the anchor reads.</param>
  /// <param name="angle">The angle the anchor turns its layout to, in degrees: north 0, west 90,
  /// south 180, east 270, plus any per-machine offset.</param>
  /// <exception cref="InvalidOperationException">The anchor's block is not assigned,
  /// <paramref name="def"/> carries no <c>multiblockStructure</c>, or the anchor turns its layout to
  /// another angle than <paramref name="angle"/>.</exception>
  public static StructureRig Around(
    TestWorld world,
    BlockEntityMultiblockStructure anchor,
    ExBlockDef def,
    int angle = 0
  ) {
    if (anchor.Block == null)
      throw new InvalidOperationException(
        "The anchor must be placed (Block assigned) before a StructureRig is built around it."
      );

    JObject json = def.ToJson();
    if (json["attributes"] is not JObject attributes)
      throw new InvalidOperationException(
        $"Block definition '{def.Code}' has no attributes, so it carries no multiblockStructure."
      );
    if (attributes["multiblockStructure"] is not JObject)
      throw new InvalidOperationException(
        $"Block definition '{def.Code}' is not a multiblock: no 'multiblockStructure' attribute."
      );

    // UpdateStructureRotation reads these attributes off the block.
    anchor.Block.Attributes = new JsonObject(attributes);
    return Rig(world, anchor, attributes, angle);
  }

  /// <summary>
  /// Prepares a rig around an already-placed <paramref name="anchor"/> whose block already carries
  /// its <c>multiblockStructure</c> attribute, as a block defined in JSON does.
  /// </summary>
  /// <param name="world">The world <paramref name="anchor"/> stands in; the anchor's api must be
  /// set (<see cref="TestWorld.Attach"/>) before <see cref="Raise"/> or <see cref="Missing"/>.</param>
  /// <param name="anchor">The placed block entity whose missing cells the rig raises.</param>
  /// <param name="angle">The angle the anchor turns its layout to, in degrees: north 0, west 90,
  /// south 180, east 270, plus any per-machine offset.</param>
  /// <exception cref="InvalidOperationException">The anchor's block is not assigned or carries no
  /// <c>multiblockStructure</c>, or the anchor turns its layout to another angle than
  /// <paramref name="angle"/>.</exception>
  public static StructureRig Around(
    TestWorld world,
    BlockEntityMultiblockStructure anchor,
    int angle = 0
  ) {
    if (anchor.Block == null)
      throw new InvalidOperationException(
        "The anchor must be placed (Block assigned) before a StructureRig is built around it."
      );
    if (
      anchor.Block.Attributes?.Token is not JObject attributes
      || attributes["multiblockStructure"] is not JObject
    )
      throw new InvalidOperationException(
        $"Block '{anchor.Block.Code}' is not a multiblock: no 'multiblockStructure' attribute."
      );
    return Rig(world, anchor, attributes, angle);
  }

  private static StructureRig Rig(
    TestWorld world,
    BlockEntityMultiblockStructure anchor,
    JObject attributes,
    int angle
  ) {
    int turned = anchor.LayoutAngle;
    if (Normal(turned) != Normal(angle))
      throw new InvalidOperationException(
        $"The rig was asked for angle {angle}, but {anchor.GetType().Name} at {anchor.Pos} turns "
          + $"its layout to {turned}."
      );

    var layout = (JObject)attributes["multiblockStructure"]!;
    // Authored glyph per block number, read straight off the JSON to avoid AssetLocation re-domaining.
    var codeByNumber = ((JObject)layout["blockNumbers"]!)
      .Properties()
      .ToDictionary(p => (int)p.Value!, p => p.Name);

    MultiblockStructure structure = new JsonObject(
      layout
    ).AsObject<MultiblockStructure>()!;
    structure.InitForUse(angle);

    MultiblockFacings facings = MultiblockFacings.FromAttributes(
      new JsonObject(attributes)
    );

    var cells = new List<(BlockPos, string)>();
    foreach (BlockOffsetAndNumber offset in structure.TransformedOffsets!)
      cells.Add(
        (
          anchor.Pos.AddCopy(offset.X, offset.Y, offset.Z),
          Demand(facings, codeByNumber[offset.W], angle)
        )
      );
    return new StructureRig(world, anchor, angle, cells);
  }

  private static int Normal(int angle) => ((angle % 360) + 360) % 360;

  /// <summary>
  /// What a cell authored as <paramref name="authored"/> requires once the structure is turned to
  /// <paramref name="angle"/>.
  /// </summary>
  private static string Demand(
    MultiblockFacings facings,
    string authored,
    int angle
  ) {
    if (facings.IsEmpty)
      return authored;

    var code = new AssetLocation(authored);
    AssetLocation rotated = facings.Rotate(code, angle);
    return rotated.Equals(code) ? authored : rotated.ToString();
  }

  /// <summary>The world position of a structure-local offset at this rig's rotation.</summary>
  public BlockPos Cell(int localX, int localY, int localZ) =>
    ExOrientation.GlobalPos(_anchor.Pos, localX, localY, localZ, Angle);

  /// <summary>
  /// Places a real, functional block (and its entity) at <paramref name="pos"/>. <see cref="Raise"/>
  /// never replaces it.
  /// </summary>
  public StructureRig Occupy(BlockPos pos, Block block, BlockEntity? be = null) {
    _world.Place(pos, block, be);
    if (be != null)
      _world.Attach(be);
    return this;
  }

  /// <summary>Fills every empty cell the anchor reports missing with a stand-in matching what it
  /// wants, open the way the anchor's connector table says. Idempotent.</summary>
  /// <remarks>An occupied cell is never replaced, and a cell that wants air is left
  /// empty.</remarks>
  public StructureRig Raise() {
    var missing = new List<BlockEntityMultiblockStructure.MissingCell>();
    _anchor.IncompleteBlockCount(missing.Add);
    foreach (BlockEntityMultiblockStructure.MissingCell cell in missing) {
      if (_world.GetBlock(cell.At).Id != 0)
        continue;

      AssetLocation concrete = Concretize(cell.Wanted);
      // Compared on path alone: a domain-wildcarded shaft legend concretises to `*:air`.
      if (concrete.Path == AirCode.Path)
        continue;

      string[] faces =
      [
        .. _anchor
          .ConnectorFacesAt(cell.At)
          .Select(f => ExOrientation.TokenOf(f, asLetter: true)),
      ];
      _world.Place(
        cell.At,
        faces.Length > 0 ? ConnectorStandIn(concrete, faces) : StandIn(concrete)
      );
    }
    return this;
  }

  /// <summary>
  /// How many footprint cells the anchor reports unsatisfied; non-zero after <see cref="Raise"/>
  /// means a cell the test occupied does not satisfy the layout.
  /// </summary>
  public int Missing => _anchor.IncompleteBlockCount();

  /// <summary>
  /// Runs the machine's own completion monitor until it observes the finished footprint. The block
  /// entity must already be <see cref="TestWorld.Initialize">initialized</see>; each attempt advances
  /// one 3 s interval.
  /// </summary>
  public bool AwaitCompletion(int maxMonitorTicks = 2) {
    for (int i = 0; i < maxMonitorTicks && !_anchor.StructureComplete; i++)
      _world.AdvanceBlockEntityTime(3000);
    return _anchor.StructureComplete;
  }

  /// <summary>
  /// <see cref="Raise"/> + <see cref="TestWorld.Initialize"/> + <see cref="AwaitCompletion"/>, throwing
  /// with the anchor's missing-cell report if the machine does not complete.
  /// </summary>
  /// <exception cref="InvalidOperationException">The machine does not complete.</exception>
  public StructureRig Complete() {
    Raise();
    _world.Initialize(_anchor);
    if (!AwaitCompletion())
      throw new InvalidOperationException(
        $"{_anchor.GetType().Name} did not complete at angle {Angle}: {Missing} of "
          + $"{Cells.Count} cells unsatisfied.{MissingReport}"
      );
    return this;
  }

  /// <summary>
  /// The anchor's report of its unsatisfied cells, one line each: what the cell wants, the face it
  /// must open to when only that is wrong, and what it holds. The message <see cref="Complete"/>
  /// throws with.
  /// </summary>
  public string MissingReport {
    get {
      var lines = new List<string>();
      _anchor.IncompleteBlockCount(cell =>
        lines.Add(
          cell.OutwardFace is string face
            ? $"\n  {cell.At}: wants '{cell.Wanted}' open to '{face}', has '{cell.Actual.Code}'"
            : $"\n  {cell.At}: wants '{cell.Wanted}', has '{cell.Actual.Code}'"
        )
      );
      return string.Concat(lines);
    }
  }

  /// <summary>
  /// A stand-in for a connector cell: a network node whose connector faces are exactly the ones the
  /// layout demands there. Cached per code and face set.
  /// </summary>
  private Block ConnectorStandIn(AssetLocation code, string[] faces) {
    string token = string.Concat(faces);
    string key = code + "|" + token;
    if (_standIns.TryGetValue(key, out Block? cached))
      return cached;

    Block block = TestNetworkBlock.Create(
      "rig",
      token,
      _nextId++,
      code.ToString()
    );
    _standIns[key] = block;
    return block;
  }

  /// <summary>One stand-in block per distinct code.</summary>
  private Block StandIn(AssetLocation code) {
    if (_standIns.TryGetValue(code.ToString(), out Block? cached))
      return cached;

    var block = TestBlocks.Configure(new Block(), code.ToString(), _nextId++);
    _standIns[code.ToString()] = block;
    return block;
  }

  /// <summary>
  /// Turns a wanted code into a concrete one that satisfies it: an alternation <c>@(air|coalpile)</c>
  /// collapses to its first branch, and <c>*</c> becomes a literal segment.
  /// </summary>
  private static AssetLocation Concretize(AssetLocation wanted) =>
    new(
      wanted.Domain,
      ExWildcards.FirstAlternative(wanted.Path).Replace("*", "x")
    );
}

/// <summary>Test-only hook for a fixture that needs a structure's rotation recomputed without going
/// through <see cref="StructureRig"/>.</summary>
public static class StructureTestHooks {
  /// <summary>Recomputes <paramref name="structure"/>'s rotation, the same path a load or monitor tick
  /// uses. When <paramref name="orientationOrSide"/> is given, it is written to the block's variant map
  /// first; when null, the current variant is read as-is.</summary>
  public static void ApplyStructureRotation(
    this BlockEntityMultiblockStructure structure,
    string? orientationOrSide = null
  ) => structure.ApplyStructureRotation(orientationOrSide);
}
