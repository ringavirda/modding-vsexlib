using System;

namespace ExpandedLib.Checks;

/// <summary>
/// Marks a class exposing <c>static CheckResult Run(ICheckSource, string)</c> for automatic
/// registration by <see cref="ExCheckRegistry.RegisterAll"/>, so a mod's own content invariant - every
/// machine's job table names a registered item, say - runs through the same <c>AssetsFinalize</c> log
/// line, <c>/exmod verify</c> and <see cref="ExlibChecks.All(ICheckSource)"/> call the eight shipped
/// checks do, with no wiring of its own.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ExCheckRegisterAttribute : Attribute { }
