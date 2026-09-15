using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Networks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ExpandedLib.Networks;

/// <summary>
/// Base class for <c>Block</c> types that auto-orient from the surrounding blocks of the same network
/// to form a connected run. Used by gas pipes and molten canals.
/// </summary>
public abstract class BlockNetworkNode
  : Block,
    IWrenchOrientable,
    INetworkConnector {
  /// <summary>Populated from the block's variant map; specifies the shape family (e.g. "straight", "bend").</summary>
  public string? Type { get; protected set; }

  /// <summary>Populated from the block's variant map; single-character codes name which faces have connectors (e.g. "ns" = north + south).</summary>
  public string? Orientation { get; protected set; }

  /// <summary>The mod system that governs all block networks and nodes. Server side only.</summary>
  public BlockNetworkModSystem? NetworkSystem { get; protected set; }

  /// <summary>Test seam: sets <see cref="Type"/> directly, the shape-family answer normally read off
  /// the block's variant map during load.</summary>
  internal void SetNetworkTypeForTest(string type) => Type = type;

  /// <summary>Test seam: sets <see cref="Orientation"/> directly, the connector-face code normally
  /// resolved from the surrounding network during placement.</summary>
  internal void ApplyOrientationForTest(string token) => Orientation = token;

  public override void OnLoaded(ICoreAPI api) {
    base.OnLoaded(api);
    PrecomputeRotatedBoxes();

    Type = Variant["type"] != null ? string.Intern(Variant["type"]) : null;
    Orientation =
      Variant["orientation"] != null
        ? string.Intern(Variant["orientation"])
        : null;

    if (api.Side == EnumAppSide.Server)
      NetworkSystem = api.ModLoader.GetModSystem<BlockNetworkModSystem>();
  }

  #region Placement and orientation
  /// <summary>Passes the computed orientation choices from <see cref="TryPlaceBlock"/> to <see cref="OnBlockPlaced"/> within the same placement call, keyed by world position.</summary>
  private static readonly ConcurrentDictionary<
    BlockPos,
    string[]
  > _tempOrientationsStore = new();

  /// <summary>
  /// Determines the best orientation for this block at the target position by
  /// examining neighbouring network blocks, then delegates to <c>DoPlaceBlock</c>.
  /// </summary>
  public override bool TryPlaceBlock(
    IWorldAccessor world,
    IPlayer byPlayer,
    ItemStack itemstack,
    BlockSelection blockSel,
    ref string failureCode
  ) {
    if (!world.BlockAccessor.GetBlock(blockSel.Position).IsReplacableBy(this)) {
      failureCode = "notreplaceable";
      return false;
    }
    if (Type == null)
      return false;

    string[] safeChoices = ComputeValidOrientations(
      world.BlockAccessor,
      blockSel.Position,
      Type,
      null
    );
    if (safeChoices.Length == 0) {
      // A plain code, shown via Lang.Get("placefailure-" + code); needs a matching lang entry.
      failureCode = "exlib-noorientation";
      return false;
    }

    // Prefer orientations that face the surface the player clicked.
    char targetFaceChar = blockSel.Face.Opposite.Code[0];
    string[] preferredChoices = safeChoices
      .Where(o => o.Contains(targetFaceChar))
      .ToArray();

    // A top/bottom click gives no horizontal hint; falls back to the player's look direction.
    if (preferredChoices.Length == 0) {
      char lookChar = SuggestedHVOrientation(byPlayer, blockSel)[
        0
      ].Opposite.Code[0];
      preferredChoices = safeChoices.Where(o => o.Contains(lookChar)).ToArray();
    }

    string[] finalChoices =
      preferredChoices.Length > 0 ? preferredChoices : safeChoices;

    _tempOrientationsStore[blockSel.Position] = finalChoices;

    AssetLocation newCode = CodeWithVariant("orientation", finalChoices[0]);
    Block? block = world.GetBlock(newCode);

    if (block != null) {
      bool placedVariant = block.DoPlaceBlock(
        world,
        byPlayer,
        blockSel,
        itemstack
      );
      // A refused variant placement never reaches OnBlockPlaced; the store entry is dropped here.
      if (!placedVariant)
        _tempOrientationsStore.TryRemove(blockSel.Position, out _);
      return placedVariant;
    }

    bool placed = base.TryPlaceBlock(
      world,
      byPlayer,
      itemstack,
      blockSel,
      ref failureCode
    );
    // The base placement path also skips OnBlockPlaced.
    if (!placed)
      _tempOrientationsStore.TryRemove(blockSel.Position, out _);
    return placed;
  }

  /// <summary>Stores the computed orientation choices on the block entity for wrench cycling, or falls back to a full recalculation.</summary>
  public override void OnBlockPlaced(
    IWorldAccessor world,
    BlockPos blockPos,
    ItemStack? byItemStack = null
  ) {
    base.OnBlockPlaced(world, blockPos, byItemStack);

    if (_tempOrientationsStore.TryRemove(blockPos, out string[]? finalChoices)) {
      if (
        world.BlockAccessor.GetBlockEntity(blockPos)
        is BlockEntityNetworkNode beNet
      ) {
        beNet.Orientation = Orientation;
        beNet.PossibleOrientations = finalChoices;
        beNet.MarkDirty(true);
      }
    } else {
      RecalculateAndSyncOrientations(world, blockPos);
    }

    // Node registration happens in DoPlaceBlock, when the block entity's network membership initialises:
    // not repeated here, to avoid a redundant O(N) BroadcastUpdate for large networks.
  }

  /// <summary>Computes the valid orientations for a block of <paramref name="type"/> at <paramref name="pos"/>, given compatible network neighbours (required) and non-network neighbours (forbidden).</summary>
  protected virtual string[] ComputeValidOrientations(
    IBlockAccessor blockAccessor,
    BlockPos pos,
    string type,
    string? currentOrientation
  ) {
    if (!AllowedOrientations.TryGetValue(type, out string[]? validOrientations))
      return [];

    List<char> requiredChars = [];
    List<char> forbiddenChars = [];

    foreach (var face in BlockFacing.ALLFACES) {
      BlockPos neighborPos = pos.AddCopy(face);
      Block neighborBlock = blockAccessor.GetBlock(neighborPos);

      if (
        BlockNetworkModSystem.IsCompatibleNetworkBlockAt(
          blockAccessor,
          neighborPos,
          neighborBlock,
          NetworkType
        ) && neighborBlock is INetworkConnector neighborNet
      ) {
        if (
          neighborNet.HasConnectorAt(blockAccessor, neighborPos, face.Opposite)
        )
          requiredChars.Add(face.Code[0]);
        else
          forbiddenChars.Add(face.Code[0]);
      } else if (
          currentOrientation != null
          && neighborBlock.CanAttachBlockAt(
            blockAccessor,
            this,
            neighborPos,
            face.Opposite
          )
        ) {
        if (currentOrientation.Contains(face.Code[0]))
          requiredChars.Add(face.Code[0]);
      }
    }

    // A linear (single-axis) shape connects via any one required face; a bendable shape requires all of them.
    bool connectsAny = validOrientations.All(IsSingleAxisOrientation);

    bool Matches(string orient) =>
      !forbiddenChars.Any(c => orient.Contains(c))
      && (
        connectsAny
          ? requiredChars.Count == 0
            || requiredChars.Any(c => orient.Contains(c))
          : requiredChars.All(c => orient.Contains(c))
      );

    var choices = validOrientations.Where(Matches).ToArray();

    // Fallback: relax the requirement for non-network faces if no orientation matched.
    if (
      choices.Length == 0
      && requiredChars.Count > 0
      && currentOrientation != null
    ) {
      requiredChars.RemoveAll(c => {
        // Orientation tokens are single letters; BlockFacing.FromCode only recognizes full words.
        BlockFacing? facing = BlockNetworkModSystem.SideToFace(c.ToString());
        if (facing == null)
          return false;
        BlockPos nPos = pos.AddCopy(facing);
        return !BlockNetworkModSystem.IsCompatibleNetworkBlockAt(
          blockAccessor,
          nPos,
          blockAccessor.GetBlock(nPos),
          NetworkType
        );
      });

      choices = validOrientations.Where(Matches).ToArray();
    }

    return choices;
  }

  /// <summary>True when every connector face in <paramref name="orientation"/> lies on the same axis (e.g. "ns", "we", "ud").</summary>
  private static bool IsSingleAxisOrientation(string orientation) =>
    orientation
      .Select(c => BlockFacing.FromFirstLetter(c)?.Axis)
      .Distinct()
      .Count() == 1;

  /// <summary>Recalculates this block's valid orientations when a neighbour changes and breaks it when unsupported: no connected network neighbours and no solid surface to attach to.</summary>
  public override void OnNeighbourBlockChange(
    IWorldAccessor world,
    BlockPos pos,
    BlockPos neighbour
  ) {
    if (Orientation == null)
      return;

    RecalculateAndSyncOrientations(world, pos);

    bool hasSolidSurface = false;
    foreach (var f in BlockFacing.ALLFACES) {
      BlockPos nPos = pos.AddCopy(f);
      if (
        world
          .BlockAccessor.GetBlock(nPos)
          .CanAttachBlockAt(world.BlockAccessor, this, nPos, f.Opposite)
      ) {
        hasSolidSurface = true;
        break;
      }
    }

    if (
      !world
        .Api.ModLoader.GetModSystem<BlockNetworkModSystem>()
        .GetConnectedNeighbors(world.BlockAccessor, pos, NetworkType)
        .Any() && !hasSolidSurface
    )
      world.BlockAccessor.BreakBlock(pos, null);
  }

  /// <summary>Removes this block's position from the network graph, then calls the base break logic.</summary>
  public override void OnBlockBroken(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer byPlayer,
    float dropQuantityMultiplier = 1
  ) {
    world
      .Api.ModLoader.GetModSystem<BlockNetworkModSystem>()
      .RemoveNode(world.BlockAccessor, pos);

    base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
  }
  #endregion

  #region IWrenchOrientable
  /// <summary>Cycles the block's orientation through <see cref="GetWrenchOrientations"/> and re-registers the node in the network graph.</summary>
  public virtual void Rotate(
    EntityAgent byEntity,
    BlockSelection blockSel,
    int dir
  ) {
    if (Type == null || Orientation == null)
      return;

    IWorldAccessor world = byEntity.World;
    BlockPos pos = blockSel.Position;
    BlockEntity? be = world.BlockAccessor.GetBlockEntity(pos);

    if (!CanWrenchRotate(world, pos))
      return;

    string[] choices = GetWrenchOrientations(world, pos);
    if (choices.Length == 0)
      return;

    int currentIndex = Array.IndexOf(choices, Orientation);
    if (currentIndex == -1)
      return;

    int nextIndex = (currentIndex + dir) % choices.Length;
    if (nextIndex < 0)
      nextIndex += choices.Length;

    BlockBehaviorExOrientable? orientable =
      GetBehavior<BlockBehaviorExOrientable>();

    // Every node def is expected to declare this behaviour; a missing one fails loudly here.
    if (orientable == null) {
      world.Logger.Error(
        "[exlib] {0} is a network node carrying no ExOrientable behaviour, so the wrench cannot "
          + "re-orient it. Declare it with ExBlockDef.NetworkOriented().",
        Code
      );
      return;
    }

    var netManager = world.Api.ModLoader.GetModSystem<BlockNetworkModSystem>();

    // Remove and add with broadcast keeps the graph and the block's connector faces in step during the swap.
    netManager.RemoveNode(world.BlockAccessor, pos);

    if (orientable.ApplyOrientation(world, pos, choices[nextIndex])) {
      be?.MarkDirty(true);

      foreach (var face in BlockFacing.ALLFACES)
        RecalculateAndSyncOrientations(world, pos.AddCopy(face));
    }

    netManager.AddNode(world.BlockAccessor, pos, NetworkType);
  }

  /// <summary>Returns the orientation cycle a wrench rotates through at <paramref name="pos"/>: recomputed from topology for full cubes, or read from the block entity for thin-profile pipes.</summary>
  protected string[] GetWrenchOrientations(IWorldAccessor world, BlockPos pos) {
    if (Type == null)
      return [];

    if (IsFullCube)
      return ComputeValidOrientations(world.BlockAccessor, pos, Type, null);

    return
      world.BlockAccessor.GetBlockEntity(pos) is BlockEntityNetworkNode beNet
      && beNet.PossibleOrientations is { Length: > 0 }
      ? beNet.PossibleOrientations
      : ComputeValidOrientations(world.BlockAccessor, pos, Type, null);
  }

  /// <summary>True when the block fills its whole cell (full-cube collision box); such blocks accept any orientation.</summary>
  protected virtual bool IsFullCube =>
    CollisionBoxes is { Length: 1 } boxes && IsFullCubeBox(boxes[0]);

  private static bool IsFullCubeBox(Cuboidf b) =>
    b.X1 <= 0 && b.Y1 <= 0 && b.Z1 <= 0 && b.X2 >= 1 && b.Y2 >= 1 && b.Z2 >= 1;

  /// <summary>Whether a wrench can rotate this block at <paramref name="pos"/>; false when only one orientation is valid.</summary>
  protected virtual bool CanWrenchRotate(IWorldAccessor world, BlockPos pos) =>
    GetWrenchOrientations(world, pos).Length > 1;
  #endregion

  #region Interaction help
  /// <summary>Appends the wrench rotate hint to the placed-block help when the block can be rotated here.</summary>
  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer
  ) {
    WorldInteraction[] baseHelp =
      base.GetPlacedBlockInteractionHelp(world, selection, forPlayer) ?? [];

    if (!CanWrenchRotate(world, selection.Position))
      return baseHelp;

    return baseHelp
      .Append(
        new WorldInteraction {
          ActionLangCode = "exlib:blockhelp-rotate",
          MouseButton = EnumMouseButton.Right,
          Itemstacks = ExItems.WrenchStacks(world),
        }
      )
      .ToArray();
  }
  #endregion

  #region Collision and selection boxes
  protected readonly Dictionary<string, Cuboidf[]> collisionBoxesCache = [];
  protected readonly Dictionary<string, Cuboidf[]> selectionBoxesCache = [];

  /// <summary>Pre-computes rotated collision and selection box arrays for every (type, orientation) pair.</summary>
  protected virtual void PrecomputeRotatedBoxes() {
    if (CollisionBoxes == null || CollisionBoxes.Length == 0)
      return;
    Vec3d pivot = new(0.5, 0.5, 0.5);

    foreach (var kvp in AllowedOrientations) {
      foreach (string orient in kvp.Value) {
        GetRotations(orient, out float rotX, out float rotY, out float rotZ);
        string cacheKey = $"{kvp.Key}-{orient}";

        Cuboidf[] rotatedCollision = new Cuboidf[CollisionBoxes.Length];
        for (int i = 0; i < CollisionBoxes.Length; i++)
          rotatedCollision[i] = CollisionBoxes[i]
            .RotatedCopy(rotX, rotY, rotZ, pivot);
        collisionBoxesCache[cacheKey] = rotatedCollision;

        Cuboidf[] baseSelection = SelectionBoxes ?? CollisionBoxes;
        Cuboidf[] rotatedSelection = new Cuboidf[baseSelection.Length];
        for (int i = 0; i < baseSelection.Length; i++)
          rotatedSelection[i] = baseSelection[i]
            .RotatedCopy(rotX, rotY, rotZ, pivot);
        selectionBoxesCache[cacheKey] = rotatedSelection;
      }
    }
  }

  public override Cuboidf[] GetCollisionBoxes(
    IBlockAccessor blockAccessor,
    BlockPos pos
  ) =>
    Type != null
    && Orientation != null
    && collisionBoxesCache.TryGetValue(
      $"{Type}-{Orientation}",
      out Cuboidf[]? boxes
    )
      ? boxes
      : base.GetCollisionBoxes(blockAccessor, pos);

  public override Cuboidf[] GetSelectionBoxes(
    IBlockAccessor blockAccessor,
    BlockPos pos
  ) =>
    Type != null
    && Orientation != null
    && selectionBoxesCache.TryGetValue(
      $"{Type}-{Orientation}",
      out Cuboidf[]? boxes
    )
      ? boxes
      : base.GetSelectionBoxes(blockAccessor, pos);

  /// <summary>Returns the Euler rotation angles in degrees for <paramref name="orientation"/>.</summary>
  protected virtual void GetRotations(
    string orientation,
    out float rotX,
    out float rotY,
    out float rotZ
  ) {
    rotX = 0;
    rotY = 0;
    rotZ = 0;

    switch (orientation) {
      // Base states (zero rotation)
      case "n":
      case "ns":
      case "nw":
      case "wne":
      case "nswe":
        break;

      // Y-axis 90 deg
      case "e":
      case "we":
      case "ws":
      case "swn":
        rotY = 90;
        break;

      // Y-axis 180 deg
      case "s":
      case "sn":
      case "se":
      case "esw":
        rotY = 180;
        break;

      // Y-axis 270 deg
      case "w":
      case "ew":
      case "en":
      case "nes":
        rotY = 270;
        break;

      // X-axis 90 deg
      case "u":
      case "ud":
      case "uwe":
        rotX = 90;
        break;

      // X-axis 270 deg
      case "d":
      case "du":
      case "dwe":
        rotX = 270;
        break;

      // Z-axis 90 deg variants
      case "dn":
      case "dnu":
      case "nsud":
        rotZ = 90;
        break;
      case "dw":
      case "dwu":
      case "weud":
        rotZ = 90;
        rotY = 90;
        break;
      case "ds":
      case "dsu":
        rotZ = 90;
        rotY = 180;
        break;
      case "de":
      case "deu":
        rotZ = 90;
        rotY = 270;
        break;

      // Z-axis 270 deg variants
      case "un":
        rotZ = 270;
        break;
      case "uw":
        rotZ = 270;
        rotY = 90;
        break;
      case "us":
        rotZ = 270;
        rotY = 180;
        break;
      case "ue":
        rotZ = 270;
        rotY = 270;
        break;

      // Complex multi-axis
      case "uns":
        rotX = 90;
        rotZ = 90;
        break;
      case "dns":
        rotZ = 90;
        rotX = 270;
        break;
    }
  }
  #endregion

  #region Drops
  public override BlockDropItemStack[] GetDropsForHandbook(
    ItemStack handbookStack,
    IPlayer forPlayer
  ) => [new BlockDropItemStack(handbookStack)];

  /// <summary>Always drops the fallback-orientation item, regardless of the in-world orientation variant.</summary>
  public override ItemStack[] GetDrops(
    IWorldAccessor worldMap,
    BlockPos pos,
    IPlayer? byPlayer,
    float dropQuantityMultiplier = 1f
  ) => [FallbackStack(worldMap)];

  /// <summary>The fallback-orientation stack, whatever this node drops.</summary>
  public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) =>
    FallbackStack(world);

  /// <summary>One stack of this def's fallback-orientation variant, or of this block when that variant
  /// is not registered.</summary>
  protected ItemStack FallbackStack(IWorldAccessor world) {
    string fallback = GetFallbackOrientation(Type);
    AssetLocation loc = CodeWithVariant("orientation", fallback);
    return new ItemStack(world.GetBlock(loc) ?? this);
  }

  /// <summary>Includes the node's material/rock/brick variant in its display name (e.g. "Iron Piping (Straight)").</summary>
  public override string GetHeldItemName(ItemStack itemStack) =>
    ExBlockNames.Decorate(this, base.GetHeldItemName(itemStack));
  #endregion

  #region Abstracts and virtuals
  /// <summary>Identifies which block network type this block belongs to (e.g. "gas", "molten").</summary>
  public abstract string NetworkType { get; }

  private Dictionary<string, string[]>? _allowedOrientations;

  /// <summary>Maps shape-type strings (e.g. "straight", "bend") to their valid orientation strings, derived from this block's code-first defs and cached on first read.</summary>
  public virtual Dictionary<string, string[]> AllowedOrientations =>
    _allowedOrientations ??= ExDefinitions.OrientationMap(
      ExDefinitions.DefinitionsOf(GetType(), Code?.Domain ?? "")
    );

  /// <summary>The orientation used for drops and handbook entries of <paramref name="type"/>: the first state <see cref="AllowedOrientations"/> lists for it, or "ns" when absent.</summary>
  protected virtual string GetFallbackOrientation(string? type) =>
    type != null
    && AllowedOrientations.TryGetValue(type, out string[]? states)
    && states.Length > 0
      ? states[0]
      : "ns";

  /// <summary>When true, this node acts as a fixed endpoint and is excluded from neighbour-discovery traversal.</summary>
  public virtual bool IsNetworkEndPoint => false;

  /// <summary>Returns true when a connection to <paramref name="neighborBlock"/> on <paramref name="face"/> is valid even though the neighbour is not a network block.</summary>
  public virtual bool IsValidNonNetworkConnection(
    Block neighborBlock,
    BlockFacing face
  ) => false;

  /// <summary>Whether this node will physically join <paramref name="neighbour"/>, beyond the geometric checks (matching connectors, same network type); implementations must answer symmetrically.</summary>
  public virtual bool AcceptsNeighbour(Block neighbour) => true;

  /// <summary>Returns true if <see cref="Orientation"/> contains the single-char code for <paramref name="face"/>.</summary>
  public virtual bool HasConnectorAt(BlockFacing face) =>
    Orientation != null && Orientation.Contains(face.Code[0]);

  /// <summary>Position-aware connector test, answering <see cref="INetworkMember.HasConnectorAt"/>. Defaults to the position-less answer.</summary>
  public virtual bool HasConnectorAt(
    IBlockAccessor world,
    BlockPos pos,
    BlockFacing face
  ) => HasConnectorAt(face);

  /// <summary>Returns all block faces that have a network connector, or null if unorientated; null and empty mean different things.</summary>
  public virtual BlockFacing[]? GetConnectorFaces() =>
    Orientation == null
      ? null
      : BlockNetworkModSystem.SidesToFaces(Orientation);

  public override bool CanAttachBlockAt(
    IBlockAccessor world,
    Block block,
    BlockPos pos,
    BlockFacing blockFace,
    Cuboidi attachmentArea
  ) => HasConnectorAt(blockFace);

  /// <summary>Recomputes the valid orientations for the block at <paramref name="pos"/>, updates the block entity, and swaps it out when the current orientation has become invalid.</summary>
  public virtual void RecalculateAndSyncOrientations(
    IWorldAccessor world,
    BlockPos pos
  ) {
    if (
      world.BlockAccessor.GetBlock(pos) is not BlockNetworkNode netBlock
      || netBlock.Type == null
    )
      return;
    if (
      world.BlockAccessor.GetBlockEntity(pos)
      is not BlockEntityNetworkNode beNet
    )
      return;

    string[] finalChoices = netBlock.ComputeValidOrientations(
      world.BlockAccessor,
      pos,
      netBlock.Type,
      netBlock.Orientation
    );

    if (finalChoices.Length == 0) {
      world.BlockAccessor.BreakBlock(pos, null);
      return;
    }

    // netBlock.Orientation, not this.Orientation: Rotate runs this against each neighbour of the wrenched block.
    beNet.Orientation = netBlock.Orientation;
    beNet.PossibleOrientations = finalChoices;
    beNet.MarkDirty(true);

    // Applied to netBlock, never to `this`: the same call runs against each neighbour from Rotate.
    if (
      netBlock.Orientation != null
      && !finalChoices.Contains(netBlock.Orientation)
    )
      netBlock
        .GetBehavior<BlockBehaviorExOrientable>()
        ?.ApplyOrientation(world, pos, finalChoices[0]);
  }
  #endregion
}
