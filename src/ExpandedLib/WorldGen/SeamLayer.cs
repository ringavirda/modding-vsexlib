#if GAME_GE_1_22
using Newtonsoft.Json;
using Vintagestory.API.MathTools;

namespace ExpandedLib.WorldGen;

/// <summary>The weathered strip along the outcrop of a layer: its blocks are written as a
/// vanilla limonite-style ore in the host rock instead of the layer block.</summary>
[JsonObject(MemberSerialization.OptIn)]
internal sealed class SeamOutcrop
{
  /// <summary>Block code; <c>{grade}</c> and <c>{rock}</c> are filled from the body's grade and
  /// the replaced host.</summary>
  [JsonProperty]
  public string Block { get; set; } = "";

  /// <summary>Grades the body picks one from.</summary>
  [JsonProperty]
  public string[] Grades { get; set; } = [];

  /// <summary>Blocks down the layer the strip runs, drawn once per body and scaled by the
  /// climate at the body centre.</summary>
  [JsonProperty]
  public NatFloat Depth { get; set; } = NatFloat.Zero;
}

/// <summary>One bed of a seam body, laid upward from the layer below it.</summary>
[JsonObject(MemberSerialization.OptIn)]
internal sealed class SeamLayer
{
  /// <summary>Block code placed; <c>{rock}</c> is filled from the replaced host.</summary>
  [JsonProperty]
  public string Block { get; set; } = "";

  /// <summary>Host rock left between this layer's base and the top of the layer below, or the
  /// base plane for the first layer, in blocks across the bed.</summary>
  [JsonProperty]
  public float Offset { get; set; }

  /// <summary>Blocks across the bed, drawn once per body.</summary>
  [JsonProperty]
  public NatFloat Thickness { get; set; } = NatFloat.One;

  /// <summary>Loose sign block on the surface above the layer, or null.</summary>
  [JsonProperty]
  public string? SurfaceBlock { get; set; }

  /// <summary>Chance 0..1 scaling the surface sign roll.</summary>
  [JsonProperty]
  public float SurfaceBlockChance { get; set; }

  /// <summary>Whether the layer's blocks count in the prospecting pick's expected quantity.</summary>
  [JsonProperty]
  public bool Propick { get; set; } = true;

  /// <summary>The weathered strip, or null.</summary>
  [JsonProperty]
  public SeamOutcrop? Outcrop { get; set; }
}
#endif
