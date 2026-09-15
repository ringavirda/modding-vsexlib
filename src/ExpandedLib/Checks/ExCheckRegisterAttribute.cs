using System;

namespace ExpandedLib.Checks;

/// <summary>Marks a class exposing <c>static CheckResult Run(ICheckSource, string)</c> for automatic
/// registration by <see cref="ExCheckRegistry.RegisterAll"/>.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ExCheckRegisterAttribute : Attribute { }
