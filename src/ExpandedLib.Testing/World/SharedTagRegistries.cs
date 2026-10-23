#if GAME_GE_1_22
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.Common.Datastructures;
#endif

namespace ExpandedLib.Testing;

/// <summary>The tag registries several worlds standing side by side bind to, so that a tag read
/// through the converters' process-wide statics gets the same handle in every one of them. Empty
/// before 1.22, which has no tag registries.</summary>
internal sealed class SharedTagRegistries {
#if GAME_GE_1_22
  private readonly RecordingLogger _log = new();

  internal ConcurrentTagRegistry Collectible { get; }

  internal ConcurrentTagRegistryFast Entity { get; }

  internal SharedTagRegistries()
  {
    Collectible = new ConcurrentTagRegistry(_log, "collectible");
    Entity = new ConcurrentTagRegistryFast(_log, "entity");
  }

  /// <summary>Points the converters' process-wide statics at these registries.</summary>
  internal void Bind()
  {
    GenericComplexConditionConverter.StaticInit(Collectible, Entity);
    CollectibleTagSetConverter.StaticInit(Collectible);
    EntityTagSetConverter.StaticInit(Entity);
  }

  /// <summary>Whether <paramref name="api"/> hands out these registries.</summary>
  internal bool IsBoundTo(ICoreAPI api) =>
    ReferenceEquals(api.CollectibleTagRegistry, Collectible)
    && ReferenceEquals(api.EntityTagRegistry, Entity);
#endif
}
