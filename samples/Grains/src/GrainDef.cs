namespace Grains;

/// <summary>
/// One entry under <c>config/grains/</c>: a grain item, the flour it grinds into, and how long
/// one piece takes. Read with <see cref="ExpandedLib.Catalogues.AssetCatalogueLoader.GetMany{T}"/>
/// the way Industry reads its metals, so any domain, not only <c>grains</c>, contributes a file.
/// One file is one entry.
/// </summary>
public sealed class GrainDef {
  /// <summary>Short key; codes the sack item <c>sack-&lt;code&gt;</c>.</summary>
  public string Code { get; set; } = "";

  /// <summary>Full item code of the grain, for example <c>game:grain-spelt</c>.</summary>
  public string Grain { get; set; } = "";

  /// <summary>Full item code of the flour, for example <c>game:flour-spelt</c>.</summary>
  public string Flour { get; set; } = "";

  /// <summary>Seconds of turning the mill needs per piece; 1 to 600.</summary>
  public int Seconds { get; set; } = 6;
}
