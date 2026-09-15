using System;

namespace ExpandedLib.Config;

/// <summary>Stamped by <c>ExConfigGenerator</c> on every accessor class it emits for an
/// <see cref="ExConfigRegisterAttribute"/>-decorated config POCO, so <see cref="ExConfig.LoadAll"/>
/// can find it by reflection. Never applied by hand.</summary>
[AttributeUsage(
  AttributeTargets.Class,
  AllowMultiple = false,
  Inherited = false
)]
public sealed class ExConfigAccessorAttribute : Attribute {
  /// <param name="configType">The config POCO this accessor was generated for.</param>
  public ExConfigAccessorAttribute(Type configType) => ConfigType = configType;

  /// <summary>The config POCO this accessor was generated for.</summary>
  public Type ConfigType { get; }
}
