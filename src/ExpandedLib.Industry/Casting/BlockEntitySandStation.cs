using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ExpandedLib.Blocks;
using ExpandedLib.FastForwarding;
using ExpandedLib.Helpers;
using ExpandedLib.Industry.Helpers;
using ExpandedLib.Industry.Molten;
using ExpandedLib.Machines;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace ExpandedLib.Industry.Casting;

/// <summary>
/// A one-cell sand station: ram it with the station's moulding sand, impress a pattern and bed more pieces,
/// fill it from a launder or a carried pour, and shake out the casting once it is hardened.
/// </summary>
/// <remarks>The block names its station in <c>attributes.sandStation.station</c>, optionally the pour cup in
/// <c>attributes.sandStation.cup</c> as <c>[x1, y1, z1, x2, y2, z2]</c> in voxels, drawn facing north and turned
/// by the block's <c>side</c> variant, and declares a <see cref="BEBehaviorMoltenCell"/>. Every refusal and
/// info line is exlib's (<c>exlib-sandstation-*</c>, <c>exlib:sandstation-*</c>). Server-side state only;
/// a derived class widens the behaviour through the protected internal members.</remarks>
[BlockEntityRegister]
public class BlockEntitySandStation : ExBlockEntity, ILiquidMetalSink {
  private const int PullRatePerTick = 50;

  [Persist("cc_sand")]
  private SandStationLevel _sand = SandStationLevel.Empty;

  [Persist("cc_pattern")]
  private string? _patternCode;

  [Persist("cc_pressed")]
  private int _pressed;

  private float? _fillTemperature;
  private bool _cupHit;
  private MoldSpec? _spec;

  private BEBehaviorMoltenCell? Cell => GetBehavior<BEBehaviorMoltenCell>();

  /// <summary>Creates the station and registers its one-second tick with the fast-forward group.</summary>
  public BlockEntitySandStation() {
    Behaviors.Add(new BEBehaviorFastForward(this, OnServerTick, 1000));
  }

  #region State

  /// <summary>The code of the <see cref="CastingStationKind"/> this block is: the block's <c>sandStation.station</c> attribute; null when it declares none.</summary>
  protected virtual string? StationCode =>
    Block?.Attributes?["sandStation"]?["station"]?.AsString();

  /// <summary>The station's kind from <see cref="CastingStationRegistry.Shared"/>, or null when the block names none or an unregistered one.</summary>
  public CastingStationKind? Kind =>
    CastingStationRegistry.Shared.TryGet(
      StationCode,
      out CastingStationKind? kind
    )
      ? kind
      : null;

  /// <summary>The station's current sand level.</summary>
  public SandStationLevel Sand => _sand;

  /// <summary>Whether a pattern has been rammed into the sand.</summary>
  public bool HasImpression => _patternCode != null;

  /// <summary>Pieces bedded into the impression so far; 0 when there is none.</summary>
  public int Pressed => _patternCode == null ? 0 : _pressed;

  /// <summary>The spec of the impressed pattern at this station, or null when there is no impression.</summary>
  public MoldSpec? Spec {
    get {
      if (_patternCode == null)
        return null;
      _spec ??= ResolveSpec(_patternCode);
      return _spec;
    }
  }

  private bool ClosedImpression => Spec?.Mould == MouldKind.Closed;

  private int PressedCapacity =>
    Spec is { } spec ? Pressed * spec.UnitsPerPiece + spec.Share : 0;

  private MoldSpec? ResolveSpec(string patternCode) {
    Item? pattern = Api?.World.GetItem(new AssetLocation(patternCode));
    return
      StationCode is { } code
      && MoldSpec.TryParse(
        pattern?.Attributes?[MoldSpec.AttributeKey],
        code,
        out MoldSpec? spec,
        out _
      )
      ? spec
      : null;
  }

  private string PatternName {
    get {
      if (_patternCode == null)
        return "";
      AssetLocation loc = new(_patternCode);
      string key = $"{loc.Domain}:item-{loc.Path}";
      string name = Lang.GetMatching(key);
      return name == key ? _patternCode : name;
    }
  }

  #endregion

  #region Seams

  /// <summary>Whether the station may draw metal from its feed this tick; <see cref="SandStationLogic.CanIntake"/> unless a station narrows it.</summary>
  protected internal virtual bool IntakeAllowed() =>
    Cell is { } cell
    && SandStationLogic.CanIntake(
      HasImpression,
      cavityFull: cell.CellAmount >= cell.MaxUnitCapacity,
      solidified: cell.Solidified
    );

  /// <summary>Lets a station resolve its own verbs before the shared casting verbs.</summary>
  /// <param name="byPlayer">The clicking player.</param>
  /// <param name="held">The active hotbar stack, or null for an empty hand.</param>
  /// <returns>True when the click was handled; the base station handles none.</returns>
  protected internal virtual bool OnStationInteract(
    IPlayer byPlayer,
    ItemStack? held
  ) => false;

  /// <summary>Called with the units drawn from the launder each time a tick pulls metal.</summary>
  /// <param name="accepted">Units this pull put in the cavity; above 0.</param>
  protected internal virtual void OnPulled(int accepted) { }

  /// <summary>Whether the cast in the cavity is all scrap whatever it holds; false for the base station.</summary>
  protected internal virtual bool PourIsScrap => false;

  /// <summary>Called once the cavity is emptied by a shake-out, after the station is rammed full again.</summary>
  protected internal virtual void OnShakenOut() { }

  /// <summary>Called after each launder pull or carried pour put metal in the cavity.</summary>
  /// <param name="units">Units that entered the cavity; above 0.</param>
  /// <param name="metal">Item code of the metal.</param>
  /// <param name="temperatureC">Temperature of the cavity's metal in degrees Celsius.</param>
  protected internal virtual void OnMetalMoved(
    int units,
    string metal,
    float temperatureC
  ) { }

  /// <summary>Called at the end of each one-second server tick.</summary>
  /// <param name="dt">Seconds since the previous tick.</param>
  protected internal virtual void OnStationTick(float dt) { }

  /// <summary>The box a carried pour must land in, in block units (0 to 1) facing north, or null when any hit on the block opens the pour; by default the block's <c>sandStation.cup</c> attribute, given in voxels.</summary>
  protected internal virtual Cuboidf? CupBox =>
    Block?.Attributes?["sandStation"]?["cup"]?.AsArray<float>()
      is { Length: 6 } c
      ? new Cuboidf(
        c[0] / 16f,
        c[1] / 16f,
        c[2] / 16f,
        c[3] / 16f,
        c[4] / 16f,
        c[5] / 16f
      )
      : null;

  /// <summary>Whether <paramref name="hitPosition"/>, in block units, lies in the cup turned to this block's facing.</summary>
  internal bool InCup(Vec3d hitPosition) =>
    CupBox is not { } cup
    || ExOrientation
      .RotateBoxes([cup], ExOrientation.AngleFromSide(Block.Variant["side"]))[0]
      .Contains(hitPosition.X, hitPosition.Y, hitPosition.Z);

  #endregion

  #region Lifecycle

  /// <inheritdoc/>
  public override void Initialize(ICoreAPI api) {
    base.Initialize(api);
    if (Spec != null)
      Cell?.SetCapacity(PressedCapacity);
  }

  /// <inheritdoc/>
  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  ) {
    base.FromTreeAttributes(tree, worldForResolving);
    _spec = null;
  }

  /// <inheritdoc/>
  protected override void DeclareState(ExBlockState state) {
    state.Tree(
      "cc_filltemp",
      tree => {
        if (_fillTemperature is { } fillTemperature)
          tree.SetFloat("cc_filltemp", fillTemperature);
        else
          tree.RemoveAttribute("cc_filltemp");
      },
      (tree, world) =>
        _fillTemperature = tree.HasAttribute("cc_filltemp")
          ? tree.GetFloat("cc_filltemp")
          : null
    );
  }

  #endregion

  #region Server tick: pull from the launder, cool

  private void OnServerTick(float dt) {
    if (Cell is not { } cell)
      return;

    if (IntakeAllowed())
      PullFromLaunder(cell);

    cell.EnsureMetalStack(Api.World);
    cell.UpdateThermal(Api.World);
    OnStationTick(dt);
  }

  private void PullFromLaunder(BEBehaviorMoltenCell cell) {
    if (LaunderFace is not { } face)
      return;
    if (
      this.BlockEntityAt<BlockEntity>(Pos.AddCopy(face)) is not IMoltenCell src
      || src.Solidified
      || src.Sealed
      || src.CellAmount <= 0
    )
      return;

    int want = Math.Min(PullRatePerTick, src.CellAmount);
    int accepted = cell.PushMetalRaw(
      want,
      src.CellMetalType,
      src.CellTemperature,
      Api.World
    );
    if (accepted <= 0)
      return;
    src.DrainMetal(accepted);
    _fillTemperature = cell.CellTemperature;
    OnMetalMoved(accepted, cell.CellMetalType, cell.CellTemperature);
    OnPulled(accepted);
  }

  /// <summary>The horizontal face the launder sits on; null when the block has no <c>side</c> variant and takes no launder feed.</summary>
  public virtual BlockFacing? LaunderFace => LaunderFaceOf(Block);

  /// <summary>The horizontal face a station block's launder sits on: its <c>side</c> variant's facing turned around.</summary>
  /// <param name="block">A station block.</param>
  /// <returns>The face, or null when the block has no <c>side</c> variant.</returns>
  public static BlockFacing? LaunderFaceOf(Block block) =>
    ExOrientation.FacingFromSide(block.Variant["side"])?.Opposite;

  #endregion

  #region Pour-in (ILiquidMetalSink)

  /// <inheritdoc/>
  public bool CanReceiveAny =>
    Cell is { } cell
    && SandStationLogic.CanIntake(
      HasImpression,
      cavityFull: cell.CellAmount >= cell.MaxUnitCapacity,
      solidified: cell.Solidified
    );

  /// <inheritdoc/>
  public bool CanReceive(ItemStack metal) =>
    CanReceiveAny
    && Cell is { } cell
    && (
      cell.CellAmount <= 0
      || cell.CellMetalType == metal.Collectible.Code.ToString()
    );

  /// <inheritdoc/>
  public void BeginFill(Vec3d hitPosition) => _cupHit = InCup(hitPosition);

  /// <inheritdoc/>
  /// <remarks>Takes nothing when the pour began outside the cup; the pour temperature is kept for the misrun check.</remarks>
  public void ReceiveLiquidMetal(
    ItemStack metal,
    ref int amount,
    float temperature
  ) {
    if (!_cupHit || !CanReceive(metal) || Cell is not { } cell)
      return;
    string code = metal.Collectible.Code.ToString();
    int accepted = cell.PushMetalRaw(amount, code, temperature, Api.World);
    if (accepted <= 0)
      return;
    amount -= accepted;
    _fillTemperature = cell.CellTemperature;
    OnMetalMoved(accepted, code, cell.CellTemperature);
  }

  /// <inheritdoc/>
  public void OnPourOver() => _cupHit = false;

  #endregion

  #region Interactions (ram / imprint / shake-out)

  /// <summary>Routes a right-click to the one action it resolves to.</summary>
  /// <param name="byPlayer">The clicking player.</param>
  /// <returns>True when the click was handled; false lets the block's default interaction run.</returns>
  public bool OnInteract(IPlayer byPlayer) {
    if (Cell is not { } cell || Kind is not { } kind)
      return false;

    ItemStack? held = byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack;
    bool holdingSand = kind.Matches(held?.Collectible?.Code?.ToString());
    bool holdingPattern = CastingPatterns.IsPattern(held?.Collectible);

    if (OnStationInteract(byPlayer, held))
      return true;

    SandStationAction action = SandStationLogic.Decide(
      holdingSand,
      holdingPattern,
      emptyHand: held == null,
      _sand,
      HasImpression,
      hasMetal: cell.CellAmount > 0,
      isHardened: cell.IsHardened,
      closedImpression: ClosedImpression
    );

    switch (action) {
      case SandStationAction.RamSand:
        return RamSand(byPlayer);
      case SandStationAction.Imprint:
        return Imprint(byPlayer, held!);
      case SandStationAction.Press:
        return Press(byPlayer, held!);
      case SandStationAction.Harvest:
        return Harvest(byPlayer, cell);
      case SandStationAction.Bedded:
        return Refuse(byPlayer, "exlib-sandstation-bedded");
      case SandStationAction.TooHot:
        return Refuse(byPlayer, "exlib-sandstation-toohot");
      default:
        return false;
    }
  }

  private bool Refuse(IPlayer byPlayer, string code, params object[] langParams) {
    if (Api.Side == EnumAppSide.Server)
      (byPlayer as IServerPlayer)?.SendIngameError(code, null, langParams);
    return true;
  }

  private bool RamSand(IPlayer byPlayer) {
    if (Api.Side == EnumAppSide.Server) {
      _sand = SandStationLevel.Full;
      if (byPlayer.WorldData?.CurrentGameMode != EnumGameMode.Creative)
        byPlayer.InventoryManager.ActiveHotbarSlot.TakeOut(1);
      byPlayer.InventoryManager.ActiveHotbarSlot.MarkDirty();
      ExSounds.Play(Api, Pos, ExSounds.StoneCrush, 0.5f);
      MarkDirty(true);
    }
    return true;
  }

  private bool Imprint(IPlayer byPlayer, ItemStack pattern) {
    if (
      !MoldSpec.TryParseAll(
        pattern.Collectible.Attributes?[MoldSpec.AttributeKey],
        out IReadOnlyList<MoldSpec> specs,
        out _
      )
    )
      return Refuse(byPlayer, "exlib-sandstation-badpattern");

    MoldSpec? spec = specs.FirstOrDefault(s =>
      string.Equals(s.Station, StationCode, StringComparison.OrdinalIgnoreCase)
    );
    if (spec == null)
      return Refuse(
        byPlayer,
        "exlib-sandstation-wrongstation",
        Block.GetPlacedBlockName(Api.World, Pos)
      );

    if (Api.Side == EnumAppSide.Server) {
      _patternCode = pattern.Collectible.Code.ToString();
      _spec = spec;
      _pressed = 1;
      _sand = SandStationLogic.SandAfterImprint(
        _sand,
        spec.Mould == MouldKind.Closed
      );
      Cell?.SetCapacity(PressedCapacity);
      WearPattern(byPlayer, pattern);
      ExSounds.Play(Api, Pos, ExSounds.StoneCrush, 0.6f);
      MarkDirty(true);
    }
    return true;
  }

  private bool Press(IPlayer byPlayer, ItemStack pattern) {
    if (Api.Side != EnumAppSide.Server)
      return true;
    if (Spec is not { } spec || _patternCode == null)
      return false;

    SandStationPressOutcome outcome = SandStationLogic.Press(
      _patternCode,
      pattern.Collectible.Code.ToString(),
      _pressed,
      spec.Pieces
    );
    if (outcome != SandStationPressOutcome.Bed)
      return Refuse(
        byPlayer,
        outcome == SandStationPressOutcome.Full
          ? "exlib-sandstation-pressfull"
          : "exlib-sandstation-pressotherpattern"
      );

    _pressed++;
    Cell?.SetCapacity(PressedCapacity);
    WearPattern(byPlayer, pattern);
    ExSounds.Play(Api, Pos, ExSounds.StoneCrush, 0.6f);
    MarkDirty(true);
    return true;
  }

  private void WearPattern(IPlayer byPlayer, ItemStack pattern) {
    if (byPlayer.WorldData?.CurrentGameMode != EnumGameMode.Creative)
      pattern.Collectible.DamageItem(
        Api.World,
        byPlayer.Entity,
        byPlayer.InventoryManager.ActiveHotbarSlot
      );
  }

  /// <summary>Spawns what a shake-out of the set casting gives and empties the cell; nothing while the metal is liquid or the cell is empty. Server only.</summary>
  public void ShakeOutOnBreak() {
    if (
      Api?.Side != EnumAppSide.Server
      || Spec is not { } spec
      || Cell is not { Solidified: true, CellAmount: > 0 } cell
    )
      return;
    bool misrun = SandStationLogic.IsMisrun(_fillTemperature, spec.MinPourTemp);
    foreach (ItemStack stack in BuildHarvest(spec, cell, misrun))
      Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.6, 0.5));
    cell.ClearContents();
  }

  private bool Harvest(IPlayer byPlayer, BEBehaviorMoltenCell cell) {
    if (Api.Side == EnumAppSide.Client)
      return true;
    if (Spec is not { } spec)
      return false;

    bool misrun = SandStationLogic.IsMisrun(_fillTemperature, spec.MinPourTemp);
    foreach (ItemStack stack in BuildHarvest(spec, cell, misrun))
      if (byPlayer.InventoryManager?.TryGiveItemstack(stack) != true)
        Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.6, 0.5));

    cell.ClearContents();
    cell.ClearCapacity();
    _patternCode = null;
    _spec = null;
    _pressed = 0;
    _fillTemperature = null;
    _sand = SandStationLogic.AfterShakeOut;
    OnShakenOut();
    ExSounds.Play(Api, Pos, ExSounds.StoneCrush, 0.7f);
    MarkDirty(true);
    return true;
  }

  private IEnumerable<ItemStack> BuildHarvest(
    MoldSpec spec,
    BEBehaviorMoltenCell cell,
    bool misrun
  ) {
    SandStationYield yield = SandStationLogic.YieldOf(
      misrun || PourIsScrap,
      cell.CellAmount,
      spec.UnitsPerPiece,
      SandStationLogic.FillBatches(spec.Fill, spec.Pieces, _pressed)
    );

    if (yield.Parts > 0) {
      spec.Output.Resolve(Api.World, "sand station output for " + Block.Code);
      if (spec.Output.ResolvedItemstack?.Clone() is { } part) {
        part.StackSize = yield.Parts;
        yield return part;
      }
    }

    if (yield.ScrapUnits > 0) {
      ItemStack? scrap = MoltenChisel.BuildRecovery(
        Api.World,
        new AssetLocation(cell.CellMetalType),
        cell.CellTemperature,
        yield.ScrapUnits
      );
      if (scrap != null)
        yield return scrap;
    }
  }

  #endregion

  #region Block info

  /// <inheritdoc/>
  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) {
    base.GetBlockInfo(forPlayer, dsc);
    if (Cell is not { } cell)
      return;

    if (!HasImpression) {
      dsc.AppendLine(
        Lang.Get(
          _sand == SandStationLevel.Full
            ? "exlib:sandstation-needspattern"
            : "exlib:sandstation-needssand"
        )
      );
      return;
    }

    if (cell.CellAmount <= 0) {
      dsc.AppendLine(
        Spec is { Pieces: > 1 } spec
          ? Lang.Get(
            "exlib:sandstation-readypieces",
            PatternName,
            _pressed,
            spec.Pieces
          )
          : Lang.Get("exlib:sandstation-ready", PatternName)
      );
      return;
    }

    dsc.AppendLine(
      Lang.Get(
        "exlib:sandstation-casting",
        cell.CellAmount,
        cell.MaxUnitCapacity,
        Lang.Get(
          cell.IsHardened
            ? "exlib:metalstate-hardened"
            : "exlib:metalstate-liquid"
        ),
        ExMeasure.Temperature(cell.CellTemperature)
      )
    );
  }

  #endregion
}
