using System;

namespace ExpandedLib.Registries;

/// <summary>Base for the kind-specific registration attributes; a class carries exactly one.
/// The key defaults to <c>{modid}.{ClassName}</c>; <see cref="Code"/> and
/// <see cref="PrefixModId"/> override it.</summary>
public abstract class RegisterAttribute(string? code = null) : Attribute {
  /// <summary>Explicit registry key, or the class name when null.</summary>
  public string? Code { get; } = code;

  /// <summary>Whether the key is prefixed with <c>{modid}.</c>.</summary>
  public bool PrefixModId { get; init; } = true;
}

/// <summary>Registers a <see cref="Vintagestory.API.Common.Block"/> class.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BlockRegisterAttribute(string? code = null)
  : RegisterAttribute(code);

/// <summary>Registers an <see cref="Vintagestory.API.Common.Item"/> class.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ItemRegisterAttribute(string? code = null)
  : RegisterAttribute(code);

/// <summary>Registers a <see cref="Vintagestory.API.Common.BlockEntity"/> class.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BlockEntityRegisterAttribute(string? code = null)
  : RegisterAttribute(code);

/// <summary>Registers a <see cref="Vintagestory.API.Common.BlockBehavior"/> class.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BlockBehaviorRegisterAttribute(string? code = null)
  : RegisterAttribute(code);

/// <summary>Registers a <see cref="Vintagestory.API.Common.BlockEntityBehavior"/> class.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BlockEntityBehaviorRegisterAttribute(string? code = null)
  : RegisterAttribute(code);

/// <summary>Registers a <see cref="Vintagestory.API.Common.CollectibleBehavior"/> class.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CollectibleBehaviorRegisterAttribute(string? code = null)
  : RegisterAttribute(code);

/// <summary>Registers a <see cref="Vintagestory.API.Common.Entities.Entity"/> class.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EntityRegisterAttribute(string? code = null)
  : RegisterAttribute(code);

/// <summary>Registers a <see cref="Vintagestory.API.Common.Entities.EntityBehavior"/> class.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EntityBehaviorRegisterAttribute(string? code = null)
  : RegisterAttribute(code);

/// <summary>Registers a <see cref="Vintagestory.API.Common.CropBehavior"/> class.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CropBehaviorRegisterAttribute(string? code = null)
  : RegisterAttribute(code);
