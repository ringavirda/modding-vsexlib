using System;

namespace ExpandedLib.Definitions;

/// <summary>Overrides the domain a definition provider's <c>Definitions(string)</c> factory is
/// handed, instead of the registering mod's own domain.</summary>
/// <example><code>
/// [ExDefDomain("iiex")]
/// public class CastPipeDefinitions : IExBlockDefProvider { }
/// </code></example>
[AttributeUsage(
  AttributeTargets.Class,
  AllowMultiple = false,
  Inherited = false
)]
public sealed class ExDefDomainAttribute(string domain) : Attribute {
  /// <summary>The domain this provider's definitions are created in.</summary>
  public string Domain { get; } = domain;
}
