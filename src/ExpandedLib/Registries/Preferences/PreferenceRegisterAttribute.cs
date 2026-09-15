using System;

namespace ExpandedLib.Registries;

/// <summary>Marks an <see cref="IExPreference"/> class for automatic registration by
/// <see cref="PreferenceRegistry.RegisterAll"/>.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PreferenceRegisterAttribute : Attribute { }
