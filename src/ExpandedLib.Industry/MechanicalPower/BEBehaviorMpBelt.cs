using System;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>
/// A belt between two pulleys of an "mpshaft" run, its law in <see cref="MpBeltWear"/>. Declared, it takes ports 0 and
/// 1 of its cell, <c>fromPulley</c> and <c>toPulley</c> (size 1 or 2, 1 when absent) and <c>crossed</c>.
/// </summary>
[BlockEntityBehaviorRegister]
public class BEBehaviorMpBelt(BlockEntity blockentity)
  : BlockEntityBehavior(blockentity),
    IMpEnergyLink {
  private readonly MpBeltWear _belt = new();
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

  public bool Engaged => _connected && !Broken;

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

  /// <summary>Fits the belt between two pulleys.</summary>
  /// <param name="from">Port key of the driving pulley's side.</param>
  /// <param name="to">Port key of the driven pulley's side.</param>
  /// <param name="fromSize">Size of the pulley at <paramref name="from"/>, 1 or 2.</param>
  /// <param name="toSize">Size of the pulley at <paramref name="to"/>, 1 or 2.</param>
  /// <param name="crossed">True when the belt is crossed, so the driven pulley turns against the driving one.</param>
  /// <exception cref="ArgumentException">A pulley size is not 1 or 2.</exception>
  public void Connect(
    NodeKey from,
    NodeKey to,
    int fromSize,
    int toSize,
    bool crossed = false
  ) {
    _belt.Fit(fromSize, toSize);
    _from = from;
    _to = to;
    _sign = crossed ? -1 : 1;
    _connected = true;
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
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldAccessForResolve
  ) {
    base.FromTreeAttributes(tree, worldAccessForResolve);
    _belt.Load(tree);
  }
}
