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

/// <summary>
/// The burdenmaker's block entity: two hoppers over a shared basin with one sliding gate between them.
/// Materials go in and come out freely, one or a stack at a time, with no batch state. The
/// <c>RightClickConstructable</c> behaviour suppresses the default mesh, so the machine renders through a
/// permanent animation re-tessellated to the currently-built elements; the resting clip <c>closed</c> must
/// keep running or the mesh goes with it, and its sibling <c>open</c> is a held pose cleared by
/// <c>StopAnimation</c>. Storage is a real multi-slot inventory, so <see cref="BlockEntityContainer"/>'s
/// break-spill covers drops.
/// </summary>
[BlockEntityRegister]
public class BlockEntityBurdenmaker : ExBlockEntityContainer {
  // Slot layout. Fixed ranges, so "which tank" is a property of the index and never derived from a slot's
  // contents. Counts are sized against a worst-case stack of 64 so the configured unit capacity always
  // binds before the slots do; `MaxStackSize` belongs to the loaded item, so this cannot be derived at
  // compile time.
  private const int OreSlots = 8; // 512 u
  private const int FluxSlots = 4; // 205 u
  private const int BunkerSlots = 18; // 1152 u

  private const int OreFirst = 0;
  private const int FluxFirst = OreFirst + OreSlots;
  private const int BunkerFirst = FluxFirst + FluxSlots;
  private const int TotalSlots = BunkerFirst + BunkerSlots;

  private readonly InventoryBurdenmaker _inventory;

  private ConstructedAnimator? _animator;

  // Hand-rolled rather than [Persist]: a value that only saves and syncs is not enough here, because
  // ToggleGate's own ApplyPose call never runs on the client (the server owns every mutation). The
  // client's pose has to come from the sync itself, which needs to see the old value before it is
  // overwritten.
  private bool _gateOpen;

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

  /// <summary>
  /// Basin fill fraction: 0 empty, 1 at the combined ore+flux capacity, which is the most one
  /// gate-open batch can ever stamp.
  /// </summary>
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

    // Resolved on both sides (IsConstructed gates server-side logic); it only builds and poses on the client.
    _animator = new ConstructedAnimator(this, () => AnimCacheKey);
    _animator.Initialize(ApplyPose);

    if (api is ICoreClientAPI capi)
      InitSurfaces(capi);
  }

  // Must stay lazy: a wrench rotation changes the variant, and a key captured at Initialize would keep
  // handing back the old orientation's cached mesh.
  private string AnimCacheKey =>
    "burdenmaker-" + Block.Variant["side"] + (_gateOpen ? "-open" : "-closed");

  public override void OnBlockRemoved() {
    _animator?.Dispose();
    DisposeSurfaces();
    base.OnBlockRemoved();
  }

  public override void OnBlockUnloaded() {
    _animator?.Dispose();
    DisposeSurfaces();
    base.OnBlockUnloaded();
  }

  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    tree.SetBool("gateOpen", _gateOpen);
  }

  /// <summary>
  /// Reads the gate state written by <see cref="ToTreeAttributes"/> - a save load and every resync the
  /// server pushes through <c>MarkDirty</c>. <see cref="ToggleGate"/> poses the server's own animator
  /// directly, but the server never touches the client's, so the client's pose has to come from here: a
  /// changed value re-poses on arrival instead of waiting for a click that will never come.
  /// </summary>
  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  ) {
    bool wasOpen = _gateOpen;
    base.FromTreeAttributes(tree, worldForResolving);
    _gateOpen = tree.GetBool("gateOpen");
    if (Api?.Side == EnumAppSide.Client && _gateOpen != wasOpen)
      ApplyPose();
    UpdateSurfaces();
  }

  /// <summary>
  /// Holds the machine visible via a permanent pose (RCC draws no mesh of its own): <c>closed</c> at rest,
  /// <c>open</c> while the lid is drawn back.
  /// </summary>
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

  // Footprints (0-16 pixel space, block-local) and floor/brim heights (block units), measured off
  // assets/burdenmaker/shapes/ore/burdenmaker.json with `vsshape measure --group Root/Hoppers` and
  // `--group Root/Base`. Each hopper's masonry lip (Root/HopperMasonry, y26-32px) flares two pixels
  // past the wall below it (y16-26px); the footprint follows the lip, the widest ring a fill quad
  // can sit inside without poking through a wall. The basin floor is the base slab's top (y2px); its
  // brim sits two pixels short of the hopper walls' foot (y16px), matching the same two-pixel margin
  // the hopper lips carry below their own metal throat (y34px).

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

  /// <summary>The finished burden's surface, taken from <see cref="ItemBurden"/>'s own model texture
  /// rather than a fresh pick, so the basin reads as the same material the item shows in hand.</summary>
  private static readonly AssetLocation BurdenTexture = new(
    "game:textures/block/coal/orecoalmix.png"
  );

  private OreSurfaceRenderer? _oreSurface;
  private OreSurfaceRenderer? _fluxSurface;
  private OreSurfaceRenderer? _burdenSurface;

  private void InitSurfaces(ICoreClientAPI capi) {
    // Degrees to radians: Shape.rotateY carries the per-side spin ShapeSpunPerOrientation baked in,
    // but SurfaceRenderer's own RotateY call, like the footprint boxes, works in radians.
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

  /// <summary>
  /// Whether <paramref name="stack"/> belongs in the wide hopper: crushed iron ore. Resolved
  /// through <see cref="MaterialRoleRegistry"/> (role <see cref="Roles.IronOre"/>), the same role check
  /// the flux hopper uses, so ores contributed by another mod's <c>materialroles.json</c> are accepted
  /// here exactly as any other machine reading the role would.
  /// </summary>
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
      // Only pool onto the same material: two ore types in one hopper would leave the burden's stamp
      // disagreeing with what went in, with no per-slot record to recover it from. Compared by code
      // rather than through ItemStack.Satisfies, which throws on a bare Item.
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
    // Highest slot first, so repeated takes empty the tank from the top and a partial stack does not
    // linger between two full ones.
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

  /// <summary>
  /// Opens the lid: both hoppers drain instantaneously into the basin as one stamped burden batch. The
  /// basin must be empty first, which is what marks batch boundaries without a batch state machine - one
  /// gate-open is one batch, and pouring onto a previous batch would average two stamps.
  /// </summary>
  public bool ToggleGate(out string? errorCode) {
    errorCode = null;

    if (_gateOpen) {
      _gateOpen = false;
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
    if (BurdenUnits > 0) {
      errorCode = "burdenmaker-emptybunker";
      return false;
    }

    ItemStack? batch = MakeBurden(ore, flux);
    if (batch == null) {
      errorCode = "burdenmaker-nothingloaded";
      return false;
    }

    ClearRange(OreFirst, OreSlots);
    ClearRange(FluxFirst, FluxSlots);
    StoreBurden(batch);

    _gateOpen = true;
    ApplyPose();
    MarkDirty(true);
    return true;
  }

  /// <summary>
  /// One batch of <c>burdenmaker:burden</c>, stamped with the proportions actually loaded.
  /// </summary>
  private ItemStack? MakeBurden(int ore, int flux) {
    Item? item = Api?.World.GetItem(new AssetLocation("burdenmaker", "burden"));
    if (item == null)
      return null;

    int units = ore + flux;
    float total = Math.Max(1, units);
    var stack = new ItemStack(item, units);
    Burden.Write(stack, new BurdenMix(ore / total, flux / total));
    return stack;
  }

  private void StoreBurden(ItemStack batch) {
    int max = Math.Max(1, batch.Collectible.MaxStackSize);
    int left = batch.StackSize;
    for (int i = BunkerFirst; i < BunkerFirst + BunkerSlots && left > 0; i++) {
      ItemStack part = batch.Clone();
      part.StackSize = Math.Min(max, left);
      _inventory[i].Itemstack = part;
      _inventory[i].MarkDirty();
      left -= part.StackSize;
    }
  }

  private void ClearRange(int first, int count) {
    for (int i = first; i < first + count; i++)
      if (!_inventory[i].Empty) {
        _inventory[i].Itemstack = null;
        _inventory[i].MarkDirty();
      }
  }

  #endregion

  #region Readout

  /// <summary>
  /// Reports both hopper contents and the flux fraction the current pair would produce, named through the
  /// same <see cref="Burden.ProfileLangKey"/> the tooltip grades a mix with. The preview is computed
  /// from the hoppers rather than the basin, so it answers while the mix can still be changed.
  /// </summary>
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

    if (ore + flux > 0) {
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

  /// <summary>
  /// The inventory. Beyond storage its only job is the per-range acceptance gate, so an automated feed
  /// cannot put lime in the ore hopper.
  /// </summary>
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
