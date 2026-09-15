using System;
using System.Text;
using BurdenMaker.Items;
using BurdenMaker.Rendering;
using ExpandedLib.Blocks;
using ExpandedLib.Catalogues;
using ExpandedLib.Industry.Materials;
using ExpandedLib.Registries;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace BurdenMaker.BlockEntities;

/// <summary>Two hoppers over a shared basin with one sliding gate; opening it drains the hoppers
/// into the basin over <see cref="BurdenMakerValues.BurdenmakerDrainSeconds"/>, stamped with the
/// mix loaded when the batch began.</summary>
[BlockEntityRegister]
public class BlockEntityBurdenmaker : ExBlockEntityContainer {
  // Fixed slot ranges: tank identity is a property of the index, not the contents.
  private const int OreSlots = 8; // 512 u
  private const int FluxSlots = 4; // 205 u
  private const int BunkerSlots = 18; // 1152 u

  private const int OreFirst = 0;
  private const int FluxFirst = OreFirst + OreSlots;
  private const int BunkerFirst = FluxFirst + FluxSlots;
  private const int TotalSlots = BunkerFirst + BunkerSlots;

  private readonly InventoryBurdenmaker _inventory;

  private ConstructedAnimator? _animator;

  // Set from FromTreeAttributes, read there before it is overwritten; not [Persist].
  private bool _gateOpen;

  // Batch state, persisted via ToTreeAttributes; the mix is fixed at the batch's first gate-open.
  private bool _batchInProgress;
  private BurdenMix _batchMix;
  private int _batchTotal;

  // Units/second; recomputed on each gate-open, not persisted.
  private float _drainRate;

  // Fractional units owed since the last whole-unit move; not persisted.
  private float _drainCarry;

  private long _drainTickId;

  public override InventoryBase Inventory => _inventory;

  public override string InventoryClassName => "burdenmaker";

  /// <summary>True once the player has finished all five construction stages.</summary>
  public bool IsConstructed => _animator?.IsConstructed ?? false;

  /// <summary>Whether the sliding lid is drawn back.</summary>
  public bool GateOpen => _gateOpen;

  public int OreUnits => UnitsIn(OreFirst, OreSlots);

  public int FluxUnits => UnitsIn(FluxFirst, FluxSlots);

  public int BurdenUnits => UnitsIn(BunkerFirst, BunkerSlots);

  /// <summary>Wide hopper fill fraction: 0 empty, 1 at <see cref="BurdenMakerValues.BurdenmakerOreCapacity"/>.</summary>
  public float OreFill =>
    (float)OreUnits / BurdenMakerValues.BurdenmakerOreCapacity;

  /// <summary>Narrow hopper fill fraction: 0 empty, 1 at <see cref="BurdenMakerValues.BurdenmakerFluxCapacity"/>.</summary>
  public float FluxFill =>
    (float)FluxUnits / BurdenMakerValues.BurdenmakerFluxCapacity;

  /// <summary>Basin fill fraction: 0 empty, 1 at the combined ore+flux capacity.</summary>
  public float BurdenFill =>
    (float)BurdenUnits
    / (
      BurdenMakerValues.BurdenmakerOreCapacity
      + BurdenMakerValues.BurdenmakerFluxCapacity
    );

  public BlockEntityBurdenmaker() {
    _inventory = new InventoryBurdenmaker(TotalSlots) { Machine = this };
  }

  #region Lifecycle

  public override void Initialize(ICoreAPI api) {
    base.Initialize(api);
    _inventory.LateInitialize(
      InventoryClassName + "-" + Pos.X + "/" + Pos.Y + "/" + Pos.Z,
      api
    );

    // Resolved on both sides; builds and poses on the client only.
    _animator = new ConstructedAnimator(this, () => AnimCacheKey);
    _animator.Initialize(ApplyPose);

    if (api is ICoreClientAPI capi)
      InitSurfaces(capi);

    // Resumes the tick on load if the gate is open and the hoppers hold anything.
    if (api.Side == EnumAppSide.Server && _gateOpen && OreUnits + FluxUnits > 0)
      StartDrain();
  }

  // Lazy: a wrench rotation changes the variant.
  private string AnimCacheKey =>
    "burdenmaker-" + Block.Variant["side"] + (_gateOpen ? "-open" : "-closed");

  public override void OnBlockRemoved() {
    StopDrain();
    _animator?.Dispose();
    DisposeSurfaces();
    base.OnBlockRemoved();
  }

  public override void OnBlockUnloaded() {
    StopDrain();
    _animator?.Dispose();
    DisposeSurfaces();
    base.OnBlockUnloaded();
  }

  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    tree.SetBool("gateOpen", _gateOpen);
    tree.SetBool("batchInProgress", _batchInProgress);
    tree.SetFloat("batchIron", _batchMix.Iron);
    tree.SetFloat("batchFlux", _batchMix.Flux);
    tree.SetInt("batchTotal", _batchTotal);
  }

  /// <summary>Reads the batch and gate state written by <see cref="ToTreeAttributes"/>; re-poses
  /// the client's animator when the gate state changes.</summary>
  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  ) {
    bool wasOpen = _gateOpen;
    base.FromTreeAttributes(tree, worldForResolving);
    _gateOpen = tree.GetBool("gateOpen");
    _batchInProgress = tree.GetBool("batchInProgress");
    _batchMix = new BurdenMix(
      tree.GetFloat("batchIron"),
      tree.GetFloat("batchFlux")
    );
    _batchTotal = tree.GetInt("batchTotal");
    if (Api?.Side == EnumAppSide.Client && _gateOpen != wasOpen)
      ApplyPose();
    UpdateSurfaces();
  }

  /// <summary>Poses the permanent animation: <c>closed</c> at rest, <c>open</c> while the lid is
  /// drawn back.</summary>
  private void ApplyPose() {
    string clip = _gateOpen ? "open" : "closed";
    _animator?.Pose(util => {
      util.StopAnimation(_gateOpen ? "closed" : "open");
      util.StartAnimation(
        new AnimationMetaData {
          Animation = clip,
          Code = clip,
          AnimationSpeed = 1f,
          EaseInSpeed = 3f,
          EaseOutSpeed = 3f,
        }.Init()
      );
    });
  }

  #endregion

  #region Ore surfaces (client only)

  // Footprints in 0-16 pixel space, block-local; floor/brim heights in block units.

  /// <summary>The wide ore hopper's interior: the two cells west of the dividing pier (x -12..15px),
  /// behind the front masonry lip (z -14..-2px).</summary>
  private static readonly Cuboidf[] WideHopperFootprint =
  [
    new(-12f, 0f, -14f, 15f, 0f, -2f),
  ];

  private const float WideHopperFloorY = 18f / 16f;
  private const float WideHopperBrimY = 32f / 16f;

  /// <summary>The narrow flux hopper's interior, past the pier (x 17..28px), same depth and height
  /// band as <see cref="WideHopperFootprint"/>.</summary>
  private static readonly Cuboidf[] NarrowHopperFootprint =
  [
    new(17f, 0f, -14f, 28f, 0f, -2f),
  ];

  /// <summary>The shared basin's interior (x -14..30px, z -12..14px), under both hoppers and the
  /// open reach behind them.</summary>
  private static readonly Cuboidf[] BasinFootprint =
  [
    new(-14f, 0f, -12f, 30f, 0f, 14f),
  ];

  private const float BasinFloorY = 2f / 16f;
  private const float BasinBrimY = 14f / 16f;

  private static readonly AssetLocation OreTexture = new(
    "game:textures/item/resource/crushed/hematite.png"
  );
  private static readonly AssetLocation FluxTexture = new(
    "game:textures/item/resource/quicklime.png"
  );

  /// <summary>The finished burden's surface texture, matching <see cref="ItemBurden"/>'s model.</summary>
  private static readonly AssetLocation BurdenTexture = new(
    "game:textures/block/coal/orecoalmix.png"
  );

  private OreSurfaceRenderer? _oreSurface;
  private OreSurfaceRenderer? _fluxSurface;
  private OreSurfaceRenderer? _burdenSurface;

  private void InitSurfaces(ICoreClientAPI capi) {
    // Degrees to radians; SurfaceRenderer.RotateY takes radians.
    float rotationY = (float)(Block.Shape.rotateY * Math.PI / 180.0);

    _oreSurface = new OreSurfaceRenderer(
      Pos,
      capi,
      WideHopperFootprint,
      rotationY,
      WideHopperFloorY,
      WideHopperBrimY,
      OreTexture
    );
    _fluxSurface = new OreSurfaceRenderer(
      Pos,
      capi,
      NarrowHopperFootprint,
      rotationY,
      WideHopperFloorY,
      WideHopperBrimY,
      FluxTexture
    );
    _burdenSurface = new OreSurfaceRenderer(
      Pos,
      capi,
      BasinFootprint,
      rotationY,
      BasinFloorY,
      BasinBrimY,
      BurdenTexture
    );

    capi.Event.RegisterRenderer(
      _oreSurface,
      EnumRenderStage.Opaque,
      "burdenmaker-ore"
    );
    capi.Event.RegisterRenderer(
      _fluxSurface,
      EnumRenderStage.Opaque,
      "burdenmaker-flux"
    );
    capi.Event.RegisterRenderer(
      _burdenSurface,
      EnumRenderStage.Opaque,
      "burdenmaker-burden"
    );
    UpdateSurfaces();
  }

  /// <summary>Pushes the current fill fractions into the three renderers. A no-op off the client,
  /// where <see cref="InitSurfaces"/> never ran.</summary>
  private void UpdateSurfaces() {
    if (_oreSurface == null)
      return;
    _oreSurface.Fill = OreFill;
    _fluxSurface!.Fill = FluxFill;
    _burdenSurface!.Fill = BurdenFill;
  }

  private void DisposeSurfaces() {
    _oreSurface?.Dispose();
    _fluxSurface?.Dispose();
    _burdenSurface?.Dispose();
    _oreSurface = null;
    _fluxSurface = null;
    _burdenSurface = null;
  }

  #endregion

  #region What each hopper takes

  /// <summary>Whether <paramref name="stack"/> belongs in the wide hopper, by <see cref="Roles.IronOre"/> role.</summary>
  public static bool IsOre(ItemStack? stack) =>
    stack?.Collectible?.Code is { } code
    && MaterialRoleRegistry.IsRole(Roles.IronOre, code);

  /// <summary>Whether <paramref name="stack"/> belongs in the narrow hopper: lime and the like.</summary>
  public static bool IsFlux(ItemStack? stack) =>
    stack?.Collectible?.Code is { } code
    && MaterialRoleRegistry.IsRole(Roles.Flux, code);

  #endregion

  #region Loading and taking

  /// <summary>Moves ore from <paramref name="from"/> into the wide hopper. Returns false if nothing moved.</summary>
  public bool TryLoadOre(ItemSlot from, bool wholeStack = false) =>
    TryLoad(
      from,
      wholeStack,
      IsOre,
      OreFirst,
      OreSlots,
      BurdenMakerValues.BurdenmakerOreCapacity
    );

  /// <summary>Moves flux from <paramref name="from"/> into the narrow hopper. Returns false if nothing moved.</summary>
  public bool TryLoadFlux(ItemSlot from, bool wholeStack = false) =>
    TryLoad(
      from,
      wholeStack,
      IsFlux,
      FluxFirst,
      FluxSlots,
      BurdenMakerValues.BurdenmakerFluxCapacity
    );

  /// <summary>Takes one stack back out of the wide hopper, or null when it is empty.</summary>
  public ItemStack? TryTakeOre() => TakeFrom(OreFirst, OreSlots);

  /// <summary>Takes one stack back out of the narrow hopper, or null when it is empty.</summary>
  public ItemStack? TryTakeFlux() => TakeFrom(FluxFirst, FluxSlots);

  /// <summary>Takes one stack of finished burden out of the basin, or null when it is empty.</summary>
  public ItemStack? TryWithdrawBurden() => TakeFrom(BunkerFirst, BunkerSlots);

  private bool TryLoad(
    ItemSlot from,
    bool wholeStack,
    System.Func<ItemStack?, bool> accepts,
    int first,
    int count,
    int capacity
  ) {
    if (from?.Itemstack is not { } held || !accepts(held))
      return false;

    int room = capacity - UnitsIn(first, count);
    if (room <= 0)
      return false;

    int wanted = Math.Min(wholeStack ? held.StackSize : 1, room);
    int moved = 0;
    for (int i = first; i < first + count && moved < wanted; i++) {
      ItemSlot slot = _inventory[i];
      // Pools onto the same material only; compared by code, not ItemStack.Satisfies (throws on a bare Item).
      if (
        !slot.Empty
        && !slot.Itemstack.Collectible.Code.Equals(held.Collectible.Code)
      )
        continue;

      int fits = slot.Empty
        ? held.Collectible.MaxStackSize
        : held.Collectible.MaxStackSize - slot.Itemstack.StackSize;
      if (fits <= 0)
        continue;

      int take = Math.Min(fits, wanted - moved);
      if (slot.Empty) {
        ItemStack one = held.Clone();
        one.StackSize = take;
        slot.Itemstack = one;
      } else {
        slot.Itemstack.StackSize += take;
      }
      slot.MarkDirty();
      moved += take;
    }

    if (moved == 0)
      return false;

    from.TakeOut(moved);
    from.MarkDirty();
    MarkDirty(true);
    return true;
  }

  private ItemStack? TakeFrom(int first, int count) {
    // Highest slot first: partial stacks empty before full ones.
    for (int i = first + count - 1; i >= first; i--) {
      ItemSlot slot = _inventory[i];
      if (slot.Empty)
        continue;
      ItemStack taken = slot.TakeOutWhole();
      slot.MarkDirty();
      MarkDirty(true);
      return taken;
    }
    return null;
  }

  private int UnitsIn(int first, int count) {
    int total = 0;
    for (int i = first; i < first + count; i++)
      if (!_inventory[i].Empty)
        total += _inventory[i].Itemstack?.StackSize ?? 0;
    return total;
  }

  #endregion

  #region The gate

  private const int DrainTickMs = 250;

  /// <summary>Toggles the lid, starting a new batch or resuming the one in progress; the batch mix
  /// is fixed at its first gate-open.</summary>
  public bool ToggleGate(out string? errorCode) {
    errorCode = null;

    if (_gateOpen) {
      _gateOpen = false;
      StopDrain();
      ApplyPose();
      MarkDirty(true);
      return true;
    }

    int ore = OreUnits;
    int flux = FluxUnits;
    if (ore + flux == 0) {
      errorCode = "burdenmaker-nothingloaded";
      return false;
    }
    if (BurdenUnits > 0 && !_batchInProgress) {
      errorCode = "burdenmaker-emptybunker";
      return false;
    }

    if (!_batchInProgress) {
      float total = ore + flux;
      _batchMix = new BurdenMix(ore / total, flux / total);
      _batchTotal = ore + flux;
      _batchInProgress = true;
    }

    _gateOpen = true;
    ApplyPose();
    StartDrain();
    MarkDirty(true);
    return true;
  }

  /// <summary>Server-only tick moving one slice of the batch from the hoppers to the basin, split
  /// by <see cref="_batchMix"/>.</summary>
  private void DrainTick(float dt) {
    int ore = OreUnits;
    int flux = FluxUnits;
    int remaining = ore + flux;
    if (remaining <= 0) {
      StopDrain();
      _batchInProgress = false;
      return;
    }

    // Floored, fraction carried to the next tick.
    _drainCarry += _drainRate * dt;
    int slice = (int)_drainCarry;
    int moveTotal = Math.Min(remaining, Math.Max(1, slice));
    _drainCarry -= moveTotal;
    int moveOre = Math.Min(
      ore,
      (int)Math.Round(moveTotal * _batchMix.IronFrac)
    );
    int moveFlux = moveTotal - moveOre;
    if (moveFlux > flux) {
      // Shortfall from rounding comes out of ore instead, capped at what the hopper holds.
      moveOre = Math.Min(ore, moveOre + (moveFlux - flux));
      moveFlux = flux;
    }

    RemoveUnits(OreFirst, OreSlots, moveOre);
    RemoveUnits(FluxFirst, FluxSlots, moveFlux);
    MergeBurden(moveOre + moveFlux);
    MarkDirty(true);

    if (OreUnits + FluxUnits <= 0) {
      StopDrain();
      _batchInProgress = false;
    }
  }

  /// <summary>Starts the drain tick at the rate implied by whatever remains. Idempotent; a no-op
  /// off the server.</summary>
  private void StartDrain() {
    if (Api?.Side != EnumAppSide.Server || _drainTickId != 0)
      return;
    int remaining = OreUnits + FluxUnits;
    _drainRate = Math.Max(
      1f,
      remaining / (float)BurdenMakerValues.BurdenmakerDrainSeconds
    );
    _drainCarry = 0f;
    _drainTickId = RegisterGameTickListener(DrainTick, DrainTickMs);
  }

  private void StopDrain() {
    if (_drainTickId == 0)
      return;
    UnregisterGameTickListener(_drainTickId);
    _drainTickId = 0;
  }

  /// <summary>Takes exactly <paramref name="amount"/> units out of the slot range, lowest slot first.</summary>
  private void RemoveUnits(int first, int count, int amount) {
    int left = amount;
    for (int i = first; i < first + count && left > 0; i++) {
      ItemSlot slot = _inventory[i];
      if (slot.Empty)
        continue;
      int take = Math.Min(slot.Itemstack.StackSize, left);
      slot.TakeOut(take);
      slot.MarkDirty();
      left -= take;
    }
  }

  /// <summary>Adds <paramref name="units"/> of burden to the basin, topping up existing stacks
  /// before opening new ones, stamped with <see cref="_batchMix"/>.</summary>
  private void MergeBurden(int units) {
    if (units <= 0)
      return;
    Item? item = Api?.World.GetItem(new AssetLocation("burdenmaker", "burden"));
    if (item == null)
      return;

    int max = Math.Max(1, item.MaxStackSize);
    int left = units;
    for (int i = BunkerFirst; i < BunkerFirst + BunkerSlots && left > 0; i++) {
      ItemSlot slot = _inventory[i];
      if (slot.Empty)
        continue;
      int room = max - slot.Itemstack.StackSize;
      if (room <= 0)
        continue;
      int add = Math.Min(room, left);
      slot.Itemstack.StackSize += add;
      slot.MarkDirty();
      left -= add;
    }
    for (int i = BunkerFirst; i < BunkerFirst + BunkerSlots && left > 0; i++) {
      ItemSlot slot = _inventory[i];
      if (!slot.Empty)
        continue;
      var stack = new ItemStack(item, Math.Min(max, left));
      Burden.Write(stack, _batchMix);
      slot.Itemstack = stack;
      slot.MarkDirty();
      left -= stack.StackSize;
    }
  }

  #endregion

  #region Readout

  /// <summary>Reports hopper contents, then the drain in progress or the mix preview.</summary>
  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb) {
    base.GetBlockInfo(forPlayer, sb);
    if (!IsConstructed)
      return;

    int ore = OreUnits;
    int flux = FluxUnits;
    sb.AppendLine(
      Lang.Get(
        "burdenmaker:burdenmaker-loaded",
        ore,
        BurdenMakerValues.BurdenmakerOreCapacity,
        flux,
        BurdenMakerValues.BurdenmakerFluxCapacity
      )
    );

    if (_batchInProgress) {
      int drained = _batchTotal - (ore + flux);
      int percent = _batchTotal > 0 ? (int)(100f * drained / _batchTotal) : 100;
      sb.AppendLine(Lang.Get("burdenmaker:burdenmaker-draining", percent));
    } else if (ore + flux > 0) {
      float total = ore + flux;
      var preview = new BurdenMix(ore / total, flux / total);
      sb.AppendLine(
        Lang.Get(
          "burdenmaker:burdenmaker-willmake",
          (int)(preview.FluxFrac * 100f),
          Lang.Get(Burden.ProfileLangKey(preview))
        )
      );
    }

    int stored = BurdenUnits;
    sb.AppendLine(
      stored > 0
        ? Lang.Get("burdenmaker:burdenmaker-stored", stored)
        : Lang.Get("burdenmaker:burdenmaker-empty")
    );
  }

  #endregion

  /// <summary>The inventory; gates acceptance by slot range.</summary>
  private class InventoryBurdenmaker(int size)
    : InventoryGeneric(size, null, null) {
    public BlockEntityBurdenmaker? Machine { get; set; }

    public override bool CanContain(ItemSlot sink, ItemSlot from) {
      int index = GetSlotId(sink);
      ItemStack? stack = from?.Itemstack;
      return index switch {
        >= OreFirst and < FluxFirst => IsOre(stack),
        >= FluxFirst and < BunkerFirst => IsFlux(stack),
        // The basin is filled by the gate, never by hand or by a chute.
        _ => false,
      };
    }
  }
}
