using System;

namespace ExpandedLib.Registries;

/// <summary>Declares the asset domain an assembly's registered classes and code-first definitions
/// are keyed under.</summary>
/// <example><code>
/// [assembly: ExDomain("iiex")]
/// </code></example>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ExDomainAttribute(string domain) : Attribute {
  /// <summary>The asset domain every registrable type in this assembly is keyed under.</summary>
  public string Domain { get; } = domain;
}
