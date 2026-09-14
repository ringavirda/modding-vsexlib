using System.Text;
using ExpandedLib.Blocks;
using ExpandedLib.Helpers;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Machines;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using ExpandedLib.Structures;
using Grains;
using HandMill.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace HandMill.BlockEntities;

[BlockEntityRegister]
public class BlockEntityMillCore : BlockEntityMultiblockMachine, IMpEnergyConsumer {
  private readonly HostMembership _membership;

  [Persist]
  private string _grainCode = "";

  [Persist]
  private int _grain;

  [Persist]
  private string _flourCode = "";

  [Persist]
  private int _flour;

  [Persist]
  private float _progress;

  public BlockEntityMillCore() {
    _membership = new HostMembership(this);
    Behaviors.Add(_membership);
  }

  private int Angle => ExOrientation.AngleFromSide(Block?.Variant?["side"]);

  public override void Initialize(ICoreAPI api) {
    // The shaft cell sits north of the core in the authored frame.
    _membership.Connectors = [ExOrientation.RotateFacing(BlockFacing.NORTH, Angle)];
    base.Initialize(api);
  }

  protected override void UpdateStructureRotation() => SetStructureAngle(Angle);

  protected override string GetIncompleteMessage(int missingCount) =>
    Lang.Get("handmill:millcore-incomplete", missingCount);

  protected override string GetCompleteMessage() =>
    Lang.Get("handmill:millcore-complete");

  private float Speed => this.NetworkAt<MpEnergyNetwork>(Pos)?.State?.Speed ?? 0f;

  private bool Grinding =>
    StructureComplete && _grain > 0 && Speed >= HandMillValues.MinGrindSpeed;

  /// <summary>The mill loads the run only while it is grinding.</summary>
  public float LoadTorque(float speed) => Grinding ? HandMillValues.GrindTorque : 0f;

  protected override void OnProductionTick(float dt) {
    if (!Grinding)
      return;
    GrainDef? entry = GrainCatalogue.ForItem(_grainCode);
    if (entry == null)
      return;
    _progress += dt;
    if (_progress < entry.Seconds)
      return;
    _progress = 0f;
    _grain--;
    _flourCode = entry.Flour;
    _flour++;
    MarkDirty();
  }

  /// <summary>Takes one piece from <paramref name="from"/> when it is a catalogued grain or sack and
  /// the hopper holds the same grain or nothing; a sack counts for <c>GrainsValues.SackSize</c>.</summary>
  public bool TryLoad(ItemSlot from) {
    string? code = from.Itemstack?.Collectible?.Code?.ToString();
    GrainDef? entry = GrainCatalogue.ForItem(code);
    if (entry == null || (_grain > 0 && _grainCode != entry.Grain))
      return false;
    from.TakeOut(1);
    from.MarkDirty();
    _grainCode = entry.Grain;
    _grain += code == entry.Grain ? 1 : GrainsValues.SackSize;
    MarkDirty();
    return true;
  }

  /// <summary>Hands every flour piece to <paramref name="player"/>, dropping what does not fit.</summary>
  public void TakeFlour(IPlayer player) {
    if (_flour == 0 || Api.World.GetItem(new AssetLocation(_flourCode)) is not { } flour)
      return;
    var stack = new ItemStack(flour, _flour);
    if (player.InventoryManager?.TryGiveItemstack(stack) != true)
      Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 1.0, 0.5));
    _flour = 0;
    MarkDirty();
  }

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) {
    base.GetBlockInfo(forPlayer, dsc);
    dsc.Lang("handmill:grain", _grain, _grainCode);
    dsc.Lang("handmill:flour", _flour);
    dsc.Lang("handmill:speed", Speed);
  }

  private sealed class HostMembership(BlockEntityMillCore owner)
    : BEBehaviorNetworkMember(owner) {
    public override string NetworkType {
      get => "mpenergy";
      protected set { }
    }
  }
}
