using System;
using Vintagestory.API.Common;

namespace ExpandedLib.Registries;

/// <summary>Marks an <see cref="IExCommand"/> class for automatic registration by <see cref="CommandRegistry.RegisterAll"/>.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CommandRegisterAttribute : Attribute {
  /// <summary>Side(s) this command registers on; <see cref="EnumAppSide.Universal"/> (default) registers on both.</summary>
  public EnumAppSide Side { get; init; } = EnumAppSide.Universal;
}
