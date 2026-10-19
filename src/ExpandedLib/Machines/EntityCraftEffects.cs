using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace ExpandedLib.Machines;

/// <summary>The effects of a held step played on an entity: its animation manager and a sound at its
/// position, heard by every nearby player.</summary>
/// <param name="holder">The entity holding the tool.</param>
public sealed class EntityCraftEffects(Entity holder) : ICraftEffects
{
  /// <inheritdoc />
  public void StartAnimation(string animation) =>
    holder.AnimManager?.StartAnimation(animation);

  /// <inheritdoc />
  public void StopAnimation(string animation) =>
    holder.AnimManager?.StopAnimation(animation);

  /// <inheritdoc />
  public void PlaySound(string sound) =>
    holder.World.PlaySoundAt(
      new AssetLocation(sound),
      holder,
      null,
      0.9f + ((float)holder.World.Rand.NextDouble() * 0.2f),
      16,
      0.35f
    );
}
