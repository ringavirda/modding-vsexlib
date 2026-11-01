using ExpandedLib.FastForwarding;
using Vintagestory.API.Common;

namespace ExpandedLib.Machines;

/// <summary>What <see cref="ProductionHost"/> reads from the block entity that owns it.</summary>
internal interface IProductionHostOwner {
  int ProductionTickMs { get; }

  bool AutoStartProduction { get; }

  int MaxAwayCatchupSteps { get; }

  float AwayCatchupStepSeconds { get; }

  FastForwardMode FastForwardMode { get; }

  double GameStepHours { get; }

  double MaxGameHoursPerTick { get; }

  void OnProductionTick(float dt);

  void OnIdleProductionTick(float dt);
}

/// <summary>The production process of a block entity that is itself the machine: reads its answers
/// from the owner, which is both the block entity and its <see cref="IProductionHostOwner"/>.
/// no away-catch-up: the owner round-trips the stamp and fast-forward state in its own tree.</summary>
internal sealed class ProductionHost(
  BlockEntity owner,
  IProductionHostOwner answers
) : BEBehaviorProductionMachine(owner) {
  protected override int ProductionTickMs => answers.ProductionTickMs;

  protected override bool AutoStartProduction => answers.AutoStartProduction;

#pragma warning disable CS0618, CS0672
  protected override int MaxAwayCatchupSteps => answers.MaxAwayCatchupSteps;

  protected override float AwayCatchupStepSeconds =>
    answers.AwayCatchupStepSeconds;
#pragma warning restore CS0618, CS0672

  protected override FastForwardMode FastForwardMode => answers.FastForwardMode;

  protected override double GameStepHours => answers.GameStepHours;

  protected override double MaxGameHoursPerTick => answers.MaxGameHoursPerTick;

  protected override void OnProductionTick(float dt) =>
    answers.OnProductionTick(dt);

  protected override void OnIdleProductionTick(float dt) =>
    answers.OnIdleProductionTick(dt);
}
