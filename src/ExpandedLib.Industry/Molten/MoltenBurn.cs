using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.Molten;

/// <summary>
/// The burn an entity takes from standing on a molten holder: fire damage while the hottest metal in
/// it is at or over <see cref="ExlibValues.MoltenBurnMinTemperature"/>, liquid or set, at most once a
/// second per entity.
/// </summary>
public static class MoltenBurn {
  /// <summary>Traversal cost a hot holder returns for a land creature; 10000 is costly enough that a creature goes round.</summary>
  public const float HotTraversalCost = 10000f;

  private const long IntervalMs = 1000;

  private sealed class Stamp {
    public bool Hit;
    public long Ms;
  }

  private static readonly ConditionalWeakTable<Entity, Stamp> LastHit = new();

  /// <summary>Temperature (deg C) of the hottest metal <paramref name="be"/> holds on its top face: its
  /// <see cref="IMoltenSurface"/> reading when it has one, otherwise the hottest of its
  /// <see cref="IMoltenCell"/> and hosted <see cref="BEBehaviorMoltenCell"/> cells that hold metal.
  /// Null when it holds none or is null.</summary>
  public static float? HottestTemperature(BlockEntity? be) {
    if (be is IMoltenSurface surface)
      return surface.SurfaceTemperature;
    float? hottest = null;
    if (be is IMoltenCell { CellAmount: > 0 } own)
      hottest = own.CellTemperature;
    foreach (BEBehaviorMoltenCell cell in be.MoltenCells())
      if (
        cell.CellAmount > 0
        && (hottest == null || cell.CellTemperature > hottest)
      )
        hottest = cell.CellTemperature;
    return hottest;
  }

  /// <summary>Whether metal at <paramref name="temperatureC"/> burns: at or over
  /// <see cref="ExlibValues.MoltenBurnMinTemperature"/>. Null is cold.</summary>
  public static bool IsHot(float? temperatureC) =>
    temperatureC >= ExlibValues.MoltenBurnMinTemperature;

  /// <summary>Burns <paramref name="entity"/> by <see cref="ExlibValues.MoltenBurnDamage"/> of Fire
  /// damage when it is alive, <paramref name="temperatureC"/> is hot and its last hit was a second or
  /// more ago on <c>entity.World</c>'s clock. Server side only: it deals damage unconditionally.</summary>
  /// <param name="entity">The entity standing on the holder; its <c>World</c> must be set.</param>
  /// <param name="temperatureC">Temperature of the hottest metal in the holder, deg C.</param>
  /// <param name="pos">The holder's block position, recorded as the damage source.</param>
  /// <returns>True when the entity took the hit.</returns>
  public static bool Apply(Entity entity, float temperatureC, BlockPos pos) {
    if (!entity.Alive || !IsHot(temperatureC))
      return false;
    long now = entity.World.ElapsedMilliseconds;
    Stamp stamp = LastHit.GetOrCreateValue(entity);
    if (stamp.Hit && now - stamp.Ms < IntervalMs)
      return false;
    stamp.Hit = true;
    stamp.Ms = now;
    entity.ReceiveDamage(
      new DamageSource {
        Source = EnumDamageSource.Block,
        Type = EnumDamageType.Fire,
        SourcePos = pos.ToVec3d(),
      },
      ExlibValues.MoltenBurnDamage
    );
    return true;
  }

  /// <summary>The traversal cost of a holder cell for <paramref name="creatureType"/>:
  /// <see cref="HotTraversalCost"/> for a land creature or humanoid while the holder is hot,
  /// otherwise <paramref name="baseCost"/>.</summary>
  public static float TraversalCost(
    BlockEntity? be,
    EnumAICreatureType creatureType,
    float baseCost
  ) =>
    creatureType
      is EnumAICreatureType.LandCreature
        or EnumAICreatureType.Humanoid
    && IsHot(HottestTemperature(be))
      ? HotTraversalCost
      : baseCost;
}
