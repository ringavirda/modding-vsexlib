using ExpandedLib.Networks;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>
/// An overrunning 1:1 coupling between two runs of an "mpshaft" network: it locks them while the driver side is not behind
/// the driven side, carries nothing otherwise, and slips at 0.8 of <c>MpOverrunCapNm</c> past the cap. Declared, it takes
/// ports 0 and 1 and the driving <c>sense</c> (1 or -1); the thrown flag is saved.
/// </summary>
[BlockEntityBehaviorRegister]
public class BEBehaviorMpOverrun(BlockEntity blockentity)
  : BlockEntityBehavior(blockentity),
    IMpEnergyLink {
  private const float SlipFraction = 0.8f;

  private NodeKey _from;
  private NodeKey _to;
  private int _sense = 1;
  private bool _connected;

  /// <summary>True while the coupling is thrown out and joins nothing.</summary>
  public bool Thrown { get; set; }

  /// <summary>The way the driver side must turn to drive: +1 or -1 in the run's signed speed.</summary>
  public int Sense => _sense;

  public NodeKey From => _from;

  public NodeKey To => _to;

  public float Ratio => 1f;

  public int Sign => 1;

  public float Efficiency => 1f;

  public bool Engaged => _connected && !Thrown;

  public float CapNm => ExlibValues.MpOverrunCapNm;

  public float SlipTorque(float relativeSpeed) =>
    _sense * relativeSpeed < 0f ? 0f : SlipFraction * CapNm;

  public bool Freewheels(int heading) => _sense * heading < 0;

  public override void Initialize(ICoreAPI api, JsonObject properties) {
    base.Initialize(api, properties);
    if (_connected)
      return;
    Connect(
      NodeKey.Port(Blockentity.Pos, 0),
      NodeKey.Port(Blockentity.Pos, 1),
      properties?["sense"].AsInt(1) ?? 1
    );
  }

  /// <summary>Fits the coupling between two sides of a shaft, engaged.</summary>
  /// <param name="from">Port key of the driver side.</param>
  /// <param name="to">Port key of the driven side.</param>
  /// <param name="sense">The way the driver side turns to drive; below zero for the mirrored coupling, otherwise 1.</param>
  public void Connect(NodeKey from, NodeKey to, int sense = 1) {
    _from = from;
    _to = to;
    _sense = sense < 0 ? -1 : 1;
    _connected = true;
  }

  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    tree.SetBool("mpOverrunThrown", Thrown);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldAccessForResolve
  ) {
    base.FromTreeAttributes(tree, worldAccessForResolve);
    Thrown = tree.GetBool("mpOverrunThrown");
  }
}
