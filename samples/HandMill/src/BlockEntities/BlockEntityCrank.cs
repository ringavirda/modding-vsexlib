using System;
using ExpandedLib;
using ExpandedLib.Blocks;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using Vintagestory.API.Common;

namespace HandMill.BlockEntities;

/// <summary>
/// Block entity for a <see cref="Blocks.BlockCrank"/>: the mpenergy run's producer, driving it with
/// a burst of torque per click, easing off as the run spins up and unwinding over time when left
/// alone.
/// </summary>
[BlockEntityRegister]
public class BlockEntityCrank : BlockEntityNetworkNode, IMpEnergyProducer {
  /// <summary>Seconds of drive left from the last click; saved so a reload does not drop a wind.</summary>
  [Persist]
  private float _windSeconds;

  public override string NetworkType {
    get => "mpenergy";
    set { }
  }

  public override void Initialize(ICoreAPI api) {
    base.Initialize(api);
    if (api.Side == EnumAppSide.Server)
      RegisterGameTickListener(Unwind, 1000);
  }

  /// <summary>Adds <paramref name="seconds"/> of drive, capped at twice one wind.</summary>
  public void Wind(float seconds) {
    _windSeconds = Math.Min(_windSeconds + seconds, seconds * 2f);
    MarkDirty();
  }

  /// <summary>Full torque at rest, easing to zero at the run's burst speed, while wound.</summary>
  public float DriveTorque(float speed) =>
    _windSeconds <= 0f
      ? 0f
      : HandMillValues.CrankTorque * Math.Max(0f, 1f - speed / ExlibValues.MpMaxSpeed);

  private void Unwind(float dt) {
    if (_windSeconds <= 0f)
      return;
    _windSeconds = Math.Max(0f, _windSeconds - dt);
    MarkDirty();
  }
}
