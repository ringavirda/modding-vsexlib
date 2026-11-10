using System;
using System.Collections.Generic;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>
/// A belt between two pulleys of an "mpshaft" run, its law in <see cref="MpBeltWear"/>. Declared, it takes ports 0 and
/// 1 of its cell, <c>fromPulley</c> and <c>toPulley</c> (size 1 or 2, 1 when absent) and <c>crossed</c>. Strung with
/// <see cref="Connect"/> to a key in another cell, it joins the two cells' runs into one: this end holds the link, the
/// belt of the far cell holds the back pointer, and each lists the other through <see cref="INetworkFarNeighbours"/>.
/// </summary>
[BlockEntityBehaviorRegister]
public class BEBehaviorMpBelt(BlockEntity blockentity)
  : BlockEntityBehavior(blockentity),
    IMpEnergyLink,
    INetworkFarNeighbours {
  private const string FarKey = "mpBeltFar";
  private const string FromKeyName = "mpBeltFrom";
  private const string ToKeyName = "mpBeltTo";
  private const string BackKey = "mpBeltBack";
  private const string SizesKey = "mpBeltSizes";
  private const string SignKey = "mpBeltSign";

  private readonly MpBeltWear _belt = new();
  private BlockPos? _far;
  private bool _back;
  private NodeKey _from;
  private NodeKey _to;
  private bool _connected;
  private int _sign = 1;

  /// <summary>Raised once when the belt wears out with shaft failure on; the owner drops the belt as scrap.</summary>
  public event Action? Snapped {
    add => _belt.Snapped += value;
    remove => _belt.Snapped -= value;
  }

  /// <summary>Accumulated wear, 0 to 1; saved with the block. At 1 the belt is worn out.</summary>
  public float Wear {
    get => _belt.Wear;
    set => _belt.Wear = value;
  }

  /// <summary>True once the belt has snapped; it carries nothing from then on.</summary>
  public bool Broken => _belt.Broken;

  public NodeKey From => _from;

  public NodeKey To => _to;

  public float Ratio =>
    MpBeltWear.PulleyDiameter(_belt.ToSize)
    / MpBeltWear.PulleyDiameter(_belt.FromSize);

  public int Sign => _sign;

  public float Efficiency => 1f;

  /// <summary>False at the back end of a belt strung across cells, which holds only the pointer to the end that holds the link.</summary>
  public bool Engaged => _connected && !_back && !Broken;

  /// <summary>The cell at the other end of a belt strung across cells, or null for a belt within one cell or one not fitted.</summary>
  public BlockPos? FarCell => _connected ? _far : null;

  /// <summary>The far cell, listed so the graph walk joins it when its own belt lists this cell back.</summary>
  public IEnumerable<BlockPos> FarNeighbours => FarCell is { } far ? [far] : [];

  public float CapNm => MpBeltWear.CapNm(_belt.FromSize, _belt.ToSize);

  public bool WornOut => _belt.WornOut;

  public override void Initialize(ICoreAPI api, JsonObject properties) {
    base.Initialize(api, properties);
    if (_connected)
      return;
    Connect(
      NodeKey.Port(Blockentity.Pos, 0),
      NodeKey.Port(Blockentity.Pos, 1),
      properties?["fromPulley"].AsInt(1) ?? 1,
      properties?["toPulley"].AsInt(1) ?? 1,
      properties?["crossed"].AsBool() ?? false
    );
  }

  /// <summary>Fits the belt between two pulleys. When <paramref name="to"/> lies in another cell the belt is strung across: the belt behaviour of that cell, when it has one, takes the back pointer, and the two runs are joined.</summary>
  /// <param name="from">Port key of the driving pulley's side, in this cell when the belt is strung across.</param>
  /// <param name="to">Port key of the driven pulley's side; a key in another cell strings the belt across to it.</param>
  /// <param name="fromSize">Size of the pulley at <paramref name="from"/>, 1 or 2.</param>
  /// <param name="toSize">Size of the pulley at <paramref name="to"/>, 1 or 2.</param>
  /// <param name="crossed">True when the belt is crossed, so the driven pulley turns against the driving one.</param>
  /// <exception cref="ArgumentException">A pulley size is not 1 or 2, or the belt is strung across and <paramref name="from"/> is not in this cell.</exception>
  public void Connect(
    NodeKey from,
    NodeKey to,
    int fromSize,
    int toSize,
    bool crossed = false
  ) {
    bool across = !Equals(to.Pos, from.Pos);
    if (across && !from.Pos.Equals(Blockentity.Pos))
      throw new ArgumentException(
        "A belt strung across cells holds its driving end in its own cell.",
        nameof(from)
      );
    _belt.Fit(fromSize, toSize);
    BlockPos? before = _far;
    ReleaseFar();
    _from = from;
    _to = to;
    _sign = crossed ? -1 : 1;
    _back = false;
    _far = across ? to.Pos.Copy() : null;
    _connected = true;
    if (_far != null)
      BeltAt(_far)?.TakeBack(from, to, Blockentity.Pos);
    if (_far != null || before != null)
      Relink(_far, before);
  }

  /// <summary>Takes the belt off: both ends of a belt strung across are cleared and the graph is walked again, splitting the runs it joined. A belt within one cell is left fitted.</summary>
  public void Disconnect() {
    BlockPos? far = _far;
    if (far == null)
      return;
    ReleaseFar();
    _far = null;
    _back = false;
    _connected = false;
    Relink(far, null);
  }

  private void TakeBack(NodeKey from, NodeKey to, BlockPos holder) {
    ReleaseFar();
    _from = from;
    _to = to;
    _far = holder.Copy();
    _back = true;
    _connected = true;
  }

  // Clears the pointer the far end holds to this cell.
  private void ReleaseFar() {
    if (
      _far is { } far
      && BeltAt(far) is { } other
      && Blockentity.Pos.Equals(other._far)
    ) {
      other._far = null;
      other._back = false;
      other._connected = false;
    }
  }

  private BEBehaviorMpBelt? BeltAt(BlockPos pos) =>
    Blockentity
      .Api?.World.BlockAccessor.GetBlockEntity(pos)
      ?.GetBehavior<BEBehaviorMpBelt>();

  private void Relink(params BlockPos?[] cells) {
    if (Blockentity.Api is not { Side: EnumAppSide.Server } api)
      return;
    var system = api.ModLoader.GetModSystem<BlockNetworkModSystem>();
    IBlockAccessor world = api.World.BlockAccessor;
    system.Relink(world, Blockentity.Pos);
    foreach (BlockPos? cell in cells)
      if (cell != null)
        system.Relink(world, cell);
  }

  public float SlipTorque(float relativeSpeed) => _belt.SlipTorque;

  /// <summary>Energy (J) the belt can slip before it wears out: its slip torque at <c>MpBeltRatedRadS</c> over one in-game day.</summary>
  public float LifeJ => _belt.LifeJ;

  public void Advance(float dt, float hours, float slipPowerW) =>
    _belt.Advance(slipPowerW, hours);

  public void Break() => _belt.Break();

  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    _belt.Save(tree);
    if (_far == null || !_connected)
      return;
    SetPos(tree, FarKey, _far);
    SetKey(tree, FromKeyName, _from);
    SetKey(tree, ToKeyName, _to);
    tree.SetBool(BackKey, _back);
    tree.SetInt(SizesKey, _belt.FromSize * 10 + _belt.ToSize);
    tree.SetInt(SignKey, _sign);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldAccessForResolve
  ) {
    base.FromTreeAttributes(tree, worldAccessForResolve);
    _belt.Load(tree);
    if (!tree.HasAttribute(FarKey + "X"))
      return;
    _far = GetPos(tree, FarKey);
    _from = GetKey(tree, FromKeyName);
    _to = GetKey(tree, ToKeyName);
    _back = tree.GetBool(BackKey);
    int sizes = tree.GetInt(SizesKey, 11);
    _belt.Fit(sizes / 10, sizes % 10);
    _sign = tree.GetInt(SignKey, 1);
    _connected = true;
  }

  private static void SetPos(ITreeAttribute tree, string name, BlockPos pos) {
    tree.SetInt(name + "X", pos.X);
    tree.SetInt(name + "Y", pos.Y);
    tree.SetInt(name + "Z", pos.Z);
  }

  private static BlockPos GetPos(ITreeAttribute tree, string name) =>
    new(
      tree.GetInt(name + "X"),
      tree.GetInt(name + "Y"),
      tree.GetInt(name + "Z")
    );

  private static void SetKey(ITreeAttribute tree, string name, NodeKey key) {
    SetPos(tree, name, key.Pos);
    tree.SetInt(name + "Slot", key.Slot);
  }

  private static NodeKey GetKey(ITreeAttribute tree, string name) =>
    new(GetPos(tree, name), (byte)tree.GetInt(name + "Slot"));
}
