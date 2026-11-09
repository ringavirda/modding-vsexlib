#if GAME_GE_1_22
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Vintagestory.API.MathTools;
using Vintagestory.ServerMods;

namespace ExpandedLib.WorldGen;

/// <summary>What the base plane is anchored to.</summary>
internal enum SeamAnchor
{
  /// <summary>A fraction of the sea level height, drawn once per body.</summary>
  Sealevel,

  /// <summary>A depth below the terrain surface of each column, drawn once per body.</summary>
  Surface,
}

/// <summary>The rotated superellipse a body covers and how its thickness falls toward the edge.</summary>
[JsonObject(MemberSerialization.OptIn)]
internal sealed class SeamFootprintSpec
{
  /// <summary>Whole length along the dip, in blocks.</summary>
  [JsonProperty]
  public NatFloat Run { get; set; } = null!;

  /// <summary>Whole length along the strike, in blocks.</summary>
  [JsonProperty]
  public NatFloat Strike { get; set; } = null!;

  /// <summary>Superellipse exponent; 2 is an ellipse, larger is squarer.</summary>
  [JsonProperty]
  public float Exponent { get; set; } = 2;

  /// <summary>Normalised distance from the centre inside which the taper factor is 1.</summary>
  [JsonProperty]
  public float TaperFull { get; set; } = 0.4f;

  /// <summary>Normalised distance at which the taper factor reaches 0.</summary>
  [JsonProperty]
  public float TaperZero { get; set; } = 0.9f;
}

/// <summary>A slow wave over the ground plan, read as a smooth noise of the world position.</summary>
[JsonObject(MemberSerialization.OptIn)]
internal sealed class SeamWaveSpec
{
  /// <summary>Peak size of the wave: blocks for <see cref="SeamSpec.Roll"/>, a fraction 0..1 of
  /// the layer thickness for <see cref="SeamSpec.Swell"/>; 0 turns the wave off.</summary>
  [JsonProperty]
  public float Amplitude { get; set; }

  /// <summary>Distance in blocks between the noise lattice points, above 0.</summary>
  [JsonProperty]
  public float Wavelength { get; set; } = 100;
}

/// <summary>A world configuration value that scales how many bodies a chunk tries.</summary>
[JsonObject(MemberSerialization.OptIn)]
internal sealed class SeamWorldScale
{
  /// <summary>World configuration key read at world creation.</summary>
  [JsonProperty]
  public string Code { get; set; } = "";

  /// <summary>Multiplier by configuration value; an unlisted value counts as 1.</summary>
  [JsonProperty]
  public Dictionary<string, float> Values { get; set; } = [];
}

/// <summary>The attributes of an <c>exlib-seam</c> deposit file: a layered body inclined along
/// a dip direction drawn per body.</summary>
[JsonObject(MemberSerialization.OptIn)]
internal sealed class SeamSpec
{
  /// <summary>The host rock the layers replace.</summary>
  [JsonProperty]
  public DepositBlock InBlock { get; set; } = null!;

  /// <summary>Extent and edge taper of the body in plan.</summary>
  [JsonProperty]
  public SeamFootprintSpec Footprint { get; set; } = null!;

  /// <summary>Drop in blocks per block of run down the dip.</summary>
  [JsonProperty]
  public NatFloat Dip { get; set; } = NatFloat.Zero;

  /// <summary>Slow waves added to the base plane height, amplitude in blocks; null keeps the plane flat.</summary>
  [JsonProperty]
  public SeamWaveSpec? Roll { get; set; }

  /// <summary>Slow waves in the thickness of every layer, amplitude a fraction of the thickness
  /// (a layer of at least 1 block never drops under 1); null keeps each layer one thickness a body.</summary>
  [JsonProperty]
  public SeamWaveSpec? Swell { get; set; }

  /// <summary>What the body's base height is measured from.</summary>
  [JsonProperty]
  [JsonConverter(typeof(StringEnumConverter))]
  public SeamAnchor Anchor { get; set; } = SeamAnchor.Sealevel;

  /// <summary>Anchor <see cref="SeamAnchor.Sealevel"/>: the updip base height as a fraction of
  /// the sea level height.</summary>
  [JsonProperty]
  public NatFloat YCropRel { get; set; } = NatFloat.One;

  /// <summary>Anchor <see cref="SeamAnchor.Surface"/>: blocks below the terrain surface.</summary>
  [JsonProperty]
  public NatFloat Depth { get; set; } = NatFloat.Zero;

  /// <summary>Blocks of rock kept above every layer block; 0 lets the body crop out.</summary>
  [JsonProperty]
  public int MinCover { get; set; }

  /// <summary>Whether the lowest block of each column's stack goes through the block's world
  /// generation callback when the variant's own callback flag is off.</summary>
  [JsonProperty]
  public bool WithLastLayerBlockCallback { get; set; }

  /// <summary>Blocks below the pick position the prospecting reading looks through.</summary>
  [JsonProperty]
  public int PropickDepth { get; set; } = 48;

  /// <summary>Layers bottom first.</summary>
  [JsonProperty]
  public List<SeamLayer> Layers { get; set; } = [];

  /// <summary>World creation setting that multiplies the body's tries per chunk; null leaves them.</summary>
  [JsonProperty]
  public SeamWorldScale? WorldScale { get; set; }

  /// <summary>Keep chance 0..1 by geologic province code; null keeps every body.</summary>
  [JsonProperty]
  public Dictionary<string, float>? ProvinceChance { get; set; }
}
#endif
