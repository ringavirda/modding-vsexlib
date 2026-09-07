using Vintagestory.API.Common;

namespace ExpandedLib.Helpers;

/// <summary>
/// Thin wrapper over <see cref="IWorldChunk.GetModdata{T}"/>/<see cref="IWorldChunk.SetModdata{T}"/>
/// for per-chunk side-band data - a reinforcement, a sweeper's completion marker - keyed the same
/// domain-prefixed way as <see cref="ExWorldData"/>.
/// </summary>
public static class ExChunkData {
  /// <summary>Reads a value previously written with <see cref="Set{T}"/>, or
  /// <paramref name="defaultValue"/> if <paramref name="domain"/>/<paramref name="key"/> was never
  /// written on <paramref name="chunk"/>.</summary>
  public static T Get<T>(
    IWorldChunk chunk,
    string domain,
    string key,
    T defaultValue = default!
  ) => chunk.GetModdata($"{domain}:{key}", defaultValue);

  /// <summary>Writes <paramref name="value"/> under <paramref name="domain"/>/<paramref name="key"/>
  /// on <paramref name="chunk"/>, persisted with the chunk. Set server-side before the chunk is sent
  /// to reach the client too.</summary>
  public static void Set<T>(
    IWorldChunk chunk,
    string domain,
    string key,
    T value
  ) => chunk.SetModdata($"{domain}:{key}", value);
}
