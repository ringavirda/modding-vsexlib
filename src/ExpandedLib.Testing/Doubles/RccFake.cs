using ExpandedLib.Blocks;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace ExpandedLib.Testing;

/// <summary>Makes a machine entity that gates on an <see cref="ExRightClickConstructable"/> (boiler,
/// engine) read as fully constructed, by planting a completed stage into the private <c>_rcc</c>
/// field. Re-apply after a real <c>Initialize</c>, which clears it.</summary>
public static class RccFake {
  public static void Complete(BlockEntity be) {
    // The single, already-completed construction state set into the behavior's "rcc" field: vanilla's
    // RightClickConstruction on 1.22, exlib's ExRightClickConstruction port on 1.20/1.21.
#if GAME_GE_1_22
    var construction = new RightClickConstruction
    {
      Stages = [new ConstructionStage()],
      CurrentCompletedStage = 0,
    };
#else
    var construction = new ExRightClickConstruction {
      Stages = [new ExConstructionStage()],
      CurrentCompletedStage = 0,
    };
#endif
    var rcc = new ExRightClickConstructable(be);
    ReflectionHelpers.SetField(rcc, "rcc", construction);

    // A machine that composes the ConstructedAnimator helper keeps _rcc on that helper; others hold
    // it directly. The helper may be null here; a bare one is created to carry the completed rcc.
    if (ReflectionHelpers.TryGetField(be, "_animator", out object? existing)) {
      object animator = existing ?? new ConstructedAnimator(be, () => "");
      ReflectionHelpers.SetField(be, "_animator", animator);
      ReflectionHelpers.SetField(animator, "_rcc", rcc);
    } else
      ReflectionHelpers.SetField(be, "_rcc", rcc);
  }
}
