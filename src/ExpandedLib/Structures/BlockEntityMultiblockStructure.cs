using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ExpandedLib.Blocks;
using ExpandedLib.Helpers;
using ExpandedLib.Machines;
using ExpandedLib.Networks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace ExpandedLib.Structures;

/// <summary>
/// Base block entity for a multiblock machine. Runs a monitor tick that detects completion or
/// breakage and publishes the pattern as readiness for a hosted production process.
/// </summary>
public abstract class BlockEntityMultiblockStructure
  : BlockEntity,
    IProductionReadiness {
  protected MultiblockStructure? _structure;
  protected MultiblockStructure? _highlightedStructure;
  protected int _currentAngle = -1;
  private ExBlockState? _state;

  /// <summary>This structure's declared fields, built on first use.</summary>
  protected ExBlockState Persisted =>
    BlockEntityStateHost.GetOrCreate(this, ref _state, DeclareState);

  /// <summary>Declares the fields this structure persists. Called once, lazily. Default: nothing.</summary>
  protected virtual void DeclareState(ExBlockState state) { }

  /// <summary>The angle handed to <c>InitForUse</c> (<c>_currentAngle + initAngleOffset</c>).</summary>
  private int _structureInitAngle;
  private MultiblockFacings _facings = MultiblockFacings.None;
  private MultiblockCellRoles _roles = MultiblockCellRoles.None;
  private MultiblockConnectors _connectors = MultiblockConnectors.None;
  private long _completionTickId;

  /// <summary>Whether every block of the multiblock structure is currently in place.</summary>
  public bool StructureComplete { get; protected set; }

  /// <summary>Interval (ms) of the structure-completion monitor tick.</summary>
  protected virtual int CompletionTickMs => 3000;

  /// <summary>Whether the machine may run production this tick.</summary>
  protected virtual bool CanRunProduction => StructureComplete;

  /// <summary>This form's readiness answer, for a hosted process and anything else that asks.</summary>
  public bool IsReadyToProduce => CanRunProduction;

  public override void Initialize(ICoreAPI api) {
    base.Initialize(api);
    if (api.Side == EnumAppSide.Server) {
      // Must run before any tick reads the angle; _currentAngle starts at -1.
      UpdateStructureRotation();
      StartMonitorTick();
    }
  }

  /// <summary>Starts both the completion monitor and the production tick.</summary>
  protected void StartStructureTick() {
    StartMonitorTick();
    ProductionProcess.Start(this);
  }

  protected void StartMonitorTick() {
    if (_completionTickId == 0 && Api.Side == EnumAppSide.Server)
      _completionTickId = RegisterGameTickListener(
        OnMonitorStructureTick,
        CompletionTickMs
      );
  }

  /// <summary>Stops both ticks (used on block removal).</summary>
  protected void StopStructureTick() {
    ProductionProcess.Stop(this);
    if (_completionTickId != 0) {
      UnregisterGameTickListener(_completionTickId);
      _completionTickId = 0;
    }
  }

  private void OnMonitorStructureTick(float dt) {
    UpdateStructureRotation();
    if (_structure == null)
      return;

    bool nowComplete = IncompleteBlockCount() == 0;
    if (nowComplete == StructureComplete)
      return;

    StructureComplete = nowComplete;
    if (nowComplete) {
      OnStructureCompleted();
      ProductionProcess.Start(this);
    } else {
      OnStructureLost();
      if (ProductionReadiness.StopsProductionWhenNotReady(this))
        ProductionProcess.Stop(this);
    }
    MarkDirty(true);
  }

  /// <summary>Called when a previously complete structure becomes incomplete. Default: no-op.</summary>
  protected virtual void OnStructureLost() { }

  /// <summary>One unsatisfied footprint cell: what stands there, the wanted code, and the position.</summary>
  public readonly record struct MissingCell(
    Block Actual,
    AssetLocation Wanted,
    BlockPos At,
    string? OutwardFace
  );

  /// <summary>Whether losing the structure also unregisters the production tick. True by default.</summary>
  protected virtual bool StopsProductionOnStructureLost => true;

  /// <summary>Published readiness answer, mirroring <see cref="StopsProductionOnStructureLost"/>.</summary>
  public bool StopsProductionWhenNotReady => StopsProductionOnStructureLost;

  /// <summary>Recomputes the structure's rotation/angle from the block orientation.</summary>
  protected abstract void UpdateStructureRotation();

  /// <summary>Reloads the structure layout for <paramref name="angle"/> and clears any stale build projection.</summary>
  protected void SetStructureAngle(int angle, int initAngleOffset = 0) {
    if (_structure != null && _currentAngle == angle)
      return;

    _structure = Block.Attributes?[
      "multiblockStructure"
    ]?.AsObject<MultiblockStructure>();
    _structure?.InitForUse(angle + initAngleOffset);
    // Derived from the previous angle; dropped here so a wrench turn cannot read a stale facing.
    _codeByNumber = null;
    _cellsAccepting = null;
    _cellsWithRole = null;
    _currentAngle = angle;
    _structureInitAngle = angle + initAngleOffset;
    _facings = MultiblockFacings.FromAttributes(Block.Attributes);
    _roles = MultiblockCellRoles.FromAttributes(Block.Attributes);
    _connectors = MultiblockConnectors.FromAttributes(Block.Attributes);

    if (Api is ICoreClientAPI capi && _highlightedStructure != null) {
      _highlightedStructure.ClearHighlights(Api.World, capi.World.Player);
      _highlightedStructure = null;
    }
  }

  /// <summary>Converts a structure-local offset into a world position for the current rotation.</summary>
  protected virtual BlockPos GetGlobalPos(int localX, int localY, int localZ) =>
    ExOrientation.GlobalPos(Pos, localX, localY, localZ, _currentAngle);

  /// <summary>Ensures <see cref="_structure"/> and <see cref="_currentAngle"/> are populated.</summary>
  protected void EnsureStructureLoaded() {
    if (_structure == null)
      UpdateStructureRotation();
  }

  /// <summary>Whether <paramref name="worldCell"/> is one of the cells this structure occupies at its placed rotation.</summary>
  public bool OwnsCell(BlockPos worldCell) {
    EnsureStructureLoaded();
    var offsets = _structure?.TransformedOffsets;
    if (offsets == null)
      return false;
    foreach (var o in offsets)
      if (
        Pos.X + o.X == worldCell.X
        && Pos.Y + o.Y == worldCell.Y
        && Pos.Z + o.Z == worldCell.Z
      )
        return true;
    return false;
  }

  private static readonly BlockPos[] _noCells = [];

  private Dictionary<AssetLocation, BlockPos[]>? _cellsAccepting;

  /// <summary>The world cells of this footprint whose layout slot would accept <paramref name="blockCode"/> at the placed facing.</summary>
  /// <param name="blockCode">Concrete code of the block that would be placed, never a wildcard.</param>
  public IReadOnlyList<BlockPos> CellsAccepting(AssetLocation blockCode) {
    EnsureStructureLoaded();
    if (_structure?.TransformedOffsets is not { } offsets)
      return _noCells;

    _cellsAccepting ??= [];
    if (_cellsAccepting.TryGetValue(blockCode, out BlockPos[]? cached))
      return cached;

    var cells = new List<BlockPos>();
    foreach (BlockOffsetAndNumber offset in offsets)
      if (
        WantedCodeAt(offset) is AssetLocation wanted
        && WildcardUtil.Match(wanted, blockCode)
      )
        cells.Add(Pos.AddCopy(offset.X, offset.Y, offset.Z));

    BlockPos[] result = [.. cells];
    _cellsAccepting[blockCode] = result;
    return result;
  }

  private Dictionary<CellRole, BlockPos[]>? _cellsWithRole;

  /// <summary>The world cells this structure's layout marks with <paramref name="role"/>, rotation-correct for the placed facing.</summary>
  public IReadOnlyList<BlockPos> CellsWithRole(CellRole role) {
    EnsureStructureLoaded();
    if (_structure?.TransformedOffsets is not { } transformed)
      return _noCells;
    List<BlockOffsetAndNumber> authored = _structure.Offsets;

    _cellsWithRole ??= [];
    if (_cellsWithRole.TryGetValue(role, out BlockPos[]? cached))
      return cached;

    IReadOnlySet<(int X, int Y, int Z)> wanted = _roles.CellsOf(role);
    var cells = new List<BlockPos>();
    // Bounded on both lists: a hand-edited structure may have them mismatched in length.
    for (int i = 0; i < authored.Count && i < transformed.Count; i++)
      if (wanted.Contains((authored[i].X, authored[i].Y, authored[i].Z)))
        cells.Add(
          Pos.AddCopy(transformed[i].X, transformed[i].Y, transformed[i].Z)
        );

    BlockPos[] result = [.. cells];
    _cellsWithRole[role] = result;
    return result;
  }

  /// <summary>The outward faces this layout demands a network connector on at <paramref name="worldCell"/>.</summary>
  public IReadOnlyList<BlockFacing> ConnectorFacesAt(BlockPos worldCell) {
    EnsureStructureLoaded();
    if (_connectors.IsEmpty || _structure?.TransformedOffsets is not { } turned)
      return _noFaces;

    List<BlockOffsetAndNumber> authored = _structure.Offsets;
    for (int i = 0; i < turned.Count && i < authored.Count; i++) {
      if (
        Pos.X + turned[i].X != worldCell.X
        || Pos.InternalY + turned[i].Y != worldCell.Y
        || Pos.Z + turned[i].Z != worldCell.Z
      )
        continue;

      return
      [
        .. _connectors
          .OutwardFacesAt((authored[i].X, authored[i].Y, authored[i].Z))
          .Select(ExOrientation.FacingFromSide)
          .Where(f => f != null)
          .Select(f => ExOrientation.RotateFacing(f!, _structureInitAngle)),
      ];
    }
    return _noFaces;
  }

  private static readonly BlockFacing[] _noFaces = [];

  /// <summary>The authored (north-frame) offsets this layout marks with <paramref name="role"/>.</summary>
  public IReadOnlySet<(int X, int Y, int Z)> LocalCellsWithRole(CellRole role) {
    EnsureStructureLoaded();
    return _roles.CellsOf(role);
  }

  /// <summary>Scans a box around <paramref name="componentPos"/> for a <typeparamref name="T"/> anchor whose structure <see cref="OwnsCell">owns</see> that cell.</summary>
  /// <param name="horizontal">Box reach out from the component on each horizontal axis, in cells.</param>
  /// <param name="below">Box reach below the component, in cells.</param>
  /// <param name="above">Box reach above the component, in cells.</param>
  public static T? FindAnchorOwning<T>(
    IWorldAccessor world,
    BlockPos componentPos,
    int horizontal,
    int below,
    int above
  )
    where T : BlockEntityMultiblockStructure {
    for (int dy = -below; dy <= above; dy++)
      for (int dx = -horizontal; dx <= horizontal; dx++)
        for (int dz = -horizontal; dz <= horizontal; dz++) {
          BlockPos at = new(
            componentPos.X + dx,
            componentPos.Y + dy,
            componentPos.Z + dz,
            componentPos.dimension
          );
          if (
            world.BlockAccessor.GetBlockEntity(at) is T anchor
            && anchor.OwnsCell(componentPos)
          )
            return anchor;
        }
    return null;
  }

  /// <summary>Re-checks completeness, fires the completed/lost callbacks, and shows or clears the build outline.</summary>
  public virtual void Interact(IPlayer byPlayer) {
    UpdateStructureRotation();
    if (_structure == null)
      return;

    // Tallies missing blocks by wanted code; a misfacing connector is tallied apart from a missing block.
    var missingByCode = new Dictionary<AssetLocation, int>();
    var misfacing = new List<MissingCell>();
    int missingCount = IncompleteBlockCount(cell => {
      if (cell.OutwardFace != null) {
        misfacing.Add(cell);
        return;
      }
      // Air-satisfied or filler slots are not player-gathered.
      if (IsAutoFilled(cell.Wanted))
        return;
      missingByCode.TryGetValue(cell.Wanted, out int count);
      missingByCode[cell.Wanted] = count + 1;
    });
    bool wasComplete = StructureComplete;
    StructureComplete = missingCount == 0;

    if (Api.Side == EnumAppSide.Server) {
      if (StructureComplete && !wasComplete) {
        OnStructureCompleted();
        StartStructureTick();
        MarkDirty(true);
      } else if (!StructureComplete && wasComplete) {
        OnStructureLost();
        if (ProductionReadiness.StopsProductionWhenNotReady(this))
          ProductionProcess.Stop(this);
        MarkDirty(true);
      }
    }

    if (Api is ICoreClientAPI clientApi) {
      if (missingCount > 0) {
        ShowMissingBlocksReport(clientApi, missingByCode, misfacing);
        _highlightedStructure = _structure;
        clientApi.TriggerIngameError(
          this,
          "incomplete",
          GetIncompleteMessage(missingCount)
        );
        HighlightIncompleteSafe(_highlightedStructure, byPlayer);
      } else {
        clientApi.TriggerIngameError(this, "complete", GetCompleteMessage());
        _highlightedStructure?.ClearHighlights(Api.World, byPlayer);
        _highlightedStructure = null;
      }
    }
  }

  /// <summary>Number of structure cells not yet satisfied.</summary>
  /// <param name="onMissing">Called per unsatisfied cell, with the wanted code already rotated.</param>
  /// <returns>The count of unsatisfied cells, or 0 when the structure is not loaded.</returns>
  protected int IncompleteBlockCount(Action<MissingCell>? onMissing = null) {
    if (_structure?.TransformedOffsets is not { } transformed)
      return 0;

    List<BlockOffsetAndNumber> authored = _structure.Offsets;
    int missing = 0;

    // Indexed: the connector demand is authored in the north frame at the same index InitForUse rotated it from.
    for (int i = 0; i < transformed.Count; i++) {
      BlockOffsetAndNumber offset = transformed[i];
      if (WantedCodeAt(offset) is not AssetLocation wanted)
        continue;

      var at = new BlockPos(
        Pos.X + offset.X,
        Pos.InternalY + offset.Y,
        Pos.Z + offset.Z,
        Pos.dimension
      );
      Block actual = Api.World.BlockAccessor.GetBlockRaw(at.X, at.Y, at.Z);

      if (!WildcardUtil.Match(wanted, actual.Code)) {
        missing++;
        onMissing?.Invoke(new MissingCell(actual, wanted, at, null));
        continue;
      }

      if (
        i < authored.Count
        && UnopenedFace(authored[i], at, actual) is string face
      ) {
        missing++;
        onMissing?.Invoke(new MissingCell(actual, wanted, at, face));
      }
    }
    return missing;
  }

  /// <summary>The first outward face a connector cell demands that its occupant does not answer, or null.</summary>
  private string? UnopenedFace(
    BlockOffsetAndNumber authored,
    BlockPos at,
    Block actual
  ) {
    if (_connectors.IsEmpty)
      return null;

    foreach (
      string letter in _connectors.OutwardFacesAt(
        (authored.X, authored.Y, authored.Z)
      )
    ) {
      if (ExOrientation.FacingFromSide(letter) is not BlockFacing authoredFace)
        continue;

      BlockFacing face = ExOrientation.RotateFacing(
        authoredFace,
        _structureInitAngle
      );
      if (
        actual is INetworkMember member
        && member.HasConnectorAt(Api.World.BlockAccessor, at, face)
      )
        continue;

      return ExOrientation.TokenOf(face, asLetter: true);
    }
    return null;
  }

  /// <summary>The rotation-resolved code a transformed offset requires, or null.</summary>
  private AssetLocation? WantedCodeAt(BlockOffsetAndNumber offset) {
    if (_structure == null)
      return null;

    _codeByNumber ??= BuildCodeByNumber(_structure);
    return _codeByNumber.TryGetValue(offset.W, out AssetLocation? wanted)
      ? _facings.Rotate(wanted, _structureInitAngle)
      : null;
  }

  private Dictionary<int, AssetLocation>? _codeByNumber;

  private static Dictionary<int, AssetLocation> BuildCodeByNumber(
    MultiblockStructure structure
  ) {
    var map = new Dictionary<int, AssetLocation>();
    foreach (var kv in structure.BlockNumbers)
      map[kv.Value] = kv.Key;
    return map;
  }

  /// <summary>Highlights incomplete slots, falling back to a neutral tint when a wanted code resolves to no block.</summary>
  private void HighlightIncompleteSafe(
    MultiblockStructure structure,
    IPlayer player
  ) {
    var offsets = structure.TransformedOffsets;
    if (offsets == null)
      return;

    var positions = new List<BlockPos>();
    var colors = new List<int>();

    foreach (var offset in offsets) {
      // Same rotation-resolved code the completion walk uses.
      if (WantedCodeAt(offset) is not AssetLocation wanted)
        continue;

      Block actual = Api.World.BlockAccessor.GetBlockRaw(
        Pos.X + offset.X,
        Pos.InternalY + offset.Y,
        Pos.Z + offset.Z
      );
      if (WildcardUtil.Match(wanted, actual.Code))
        continue;

      positions.Add(new BlockPos(offset.X, offset.Y, offset.Z).Add(Pos));

      if (actual.Id != 0) {
        // Wrong solid block: red tint.
        colors.Add(ColorUtil.ColorFromRgba(215, 94, 94, 0x60));
        continue;
      }

      // Empty slot: tint with the wanted block's color, or neutral blue when it resolves to none.
      Block[] matches = Api.World.SearchBlocks(wanted);
      if (matches.Length == 0) {
        colors.Add(ColorUtil.ColorFromRgba(94, 94, 215, 0x60));
        continue;
      }

      int color = matches[0].GetColor(Api as ICoreClientAPI, Pos) & 0xFFFFFF;
      color |= 0x60 << 24;
      colors.Add(color);
    }

    Api.World.HighlightBlocks(
      player,
      MultiblockStructure.HighlightSlotId,
      positions,
      colors
    );
  }

  private static readonly AssetLocation AirCode = new("game:air");

  /// <summary>True when a slot is satisfied without the player gathering a block.</summary>
  private static bool IsAutoFilled(AssetLocation wantBlockCode) =>
    WildcardUtil.Match(wantBlockCode, AirCode)
    || WildcardUtil.Match(wantBlockCode, StructureFillers.FillerCode);

  /// <summary>Shows the player a chat breakdown of every block still missing and how many of each.</summary>
  private void ShowMissingBlocksReport(
    ICoreClientAPI clientApi,
    Dictionary<AssetLocation, int> missingByCode,
    IReadOnlyList<MissingCell> misfacing
  ) {
    if (missingByCode.Count == 0 && misfacing.Count == 0)
      return;

    // Strings come from exlib's own domain via the generated ExlibLang accessors.
    var sb = new StringBuilder();
    sb.Append(
      Lang.Get(
        missingByCode.Count > 0
          ? ExlibLang.StructureMissingHeader
          : ExlibLang.StructureMisfacingHeader
      )
    );

    foreach (
      var entry in missingByCode
        .OrderByDescending(e => e.Value)
        .ThenBy(e => ResolveBlockName(e.Key))
    ) {
      sb.Append('\n');
      sb.Append(
        Lang.Get(
          ExlibLang.StructureMissingLine,
          entry.Value,
          ResolveBlockName(entry.Key)
        )
      );
    }

    // One line per misfaced cell, naming the position.
    foreach (MissingCell cell in misfacing) {
      sb.Append('\n');
      sb.Append(
        Lang.Get(
          ExlibLang.StructureMisfacingLine,
          ResolveBlockName(cell.Wanted),
          $"{cell.At.X}, {cell.At.Y}, {cell.At.Z}",
          FaceName(cell.OutwardFace)
        )
      );
    }

    clientApi.ShowChatMessage(sb.ToString());
  }

  /// <summary>The player-facing name of an outward face letter.</summary>
  private static string FaceName(string? letter) =>
    Lang.Get(
      letter switch {
        "n" => ExlibLang.FacingN,
        "e" => ExlibLang.FacingE,
        "s" => ExlibLang.FacingS,
        "w" => ExlibLang.FacingW,
        "u" => ExlibLang.FacingU,
        _ => ExlibLang.FacingD,
      }
    );

  /// <summary>Resolves a structure block code, which may be a wildcard, to a human-readable display name.</summary>
  private string ResolveBlockName(AssetLocation wantBlockCode) {
    Block? block = Api.World.GetBlock(wantBlockCode);
    if (block == null) {
      Block[] matches = Api.World.SearchBlocks(wantBlockCode);
      if (matches.Length > 0)
        block = matches[0];
    }

    return block != null
      ? new ItemStack(block).GetName()
      : wantBlockCode.ToShortString();
  }

  /// <summary>Called when the structure transitions to complete. Default: no-op.</summary>
  protected virtual void OnStructureCompleted() { }

  /// <summary>Returns the ingame-error message shown when the structure is missing <paramref name="missingCount"/> blocks.</summary>
  protected abstract string GetIncompleteMessage(int missingCount);

  /// <summary>Returns the ingame-error message shown when the structure is complete.</summary>
  protected abstract string GetCompleteMessage();

  public override void OnBlockRemoved() {
    base.OnBlockRemoved();
    StopStructureTick();
    if (Api is ICoreClientAPI capi)
      _highlightedStructure?.ClearHighlights(Api.World, capi.World.Player);
  }

  /// <summary>Chunk unload: stops this instance's listeners and clears its client-side build outline.</summary>
  public override void OnBlockUnloaded() {
    base.OnBlockUnloaded();
    StopStructureTick();
    if (Api is ICoreClientAPI capi)
      _highlightedStructure?.ClearHighlights(Api.World, capi.World.Player);
  }

  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    tree.SetBool("structureComplete", StructureComplete);
    Persisted.ToTree(tree);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  ) {
    base.FromTreeAttributes(tree, worldForResolving);
    bool wasComplete = StructureComplete;
    StructureComplete = tree.GetBool("structureComplete");
    Persisted.FromTree(tree, worldForResolving);

    // Auto-hides the build projection once the structure completes.
    if (
      !wasComplete
      && StructureComplete
      && Api is ICoreClientAPI capi
      && _highlightedStructure != null
    ) {
      _highlightedStructure.ClearHighlights(Api.World, capi.World.Player);
      _highlightedStructure = null;
    }
  }

  public override void OnStoreCollectibleMappings(
    Dictionary<int, AssetLocation> blockIdMapping,
    Dictionary<int, AssetLocation> itemIdMapping
  ) {
    base.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);
    Persisted.StoreCollectibleMappings(
      Api.World,
      blockIdMapping,
      itemIdMapping
    );
  }

  public override void OnLoadCollectibleMappings(
    IWorldAccessor worldForResolve,
    Dictionary<int, AssetLocation> oldBlockIdMapping,
    Dictionary<int, AssetLocation> oldItemIdMapping,
    int schematicSeed,
    bool resolveImports
  ) {
    base.OnLoadCollectibleMappings(
      worldForResolve,
      oldBlockIdMapping,
      oldItemIdMapping,
      schematicSeed,
      resolveImports
    );
    Persisted.LoadCollectibleMappings(
      worldForResolve,
      oldBlockIdMapping,
      oldItemIdMapping
    );
  }

  /// <summary>Runs one monitor tick, as the registered listener would.</summary>
  internal void DriveMonitorTick() => OnMonitorStructureTick(0f);

  /// <summary>Recomputes this structure's rotation, optionally after setting the orientation variant.</summary>
  internal void ApplyStructureRotation(string? orientationOrSide = null) {
    if (orientationOrSide != null && Block != null) {
      if (Block.Variant.ContainsKey("side"))
        Block.Variant["side"] = orientationOrSide;
      else if (Block.Variant.ContainsKey("orientation"))
        Block.Variant["orientation"] = orientationOrSide;
    }
    UpdateStructureRotation();
  }
}
