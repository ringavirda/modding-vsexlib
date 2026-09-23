using System;

namespace ExpandedLib.Registries;

/// <summary>Marks an assembly whose bare block-entity keys (<c>{ShortId}</c>, <c>{shortid}</c>) are
/// in published saves. <see cref="EntityRegistry.RegisterAll"/> gives a bare key claimed by a marked
/// and an unmarked assembly to the marked one, whichever registers first; the unmarked claimant is
/// not registered under it and one Notification names both types.</summary>
/// <example><code>
/// [assembly: ExPublishedSaveKeys]
/// </code></example>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ExPublishedSaveKeysAttribute : Attribute;
