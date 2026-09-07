using System;
using System.Collections.Generic;
using System.Reflection;
using ExpandedLib.Registries;
using Vintagestory.API.Common;

namespace ExpandedLib.Checks;

/// <summary>
/// Reflection-driven registration for a mod's own content checks, the checks-side counterpart to
/// <see cref="Registries.EntityRegistry"/>. Scans an assembly for
/// <see cref="ExCheckRegisterAttribute"/>-decorated classes exposing
/// <c>static CheckResult Run(ICheckSource, string)</c> and appends each to
/// <see cref="ExlibChecks"/>'s run list, after the eight shipped checks and in registration order. A
/// class carrying the attribute but missing that exact method is warned about and skipped; the same
/// type scanned twice (a rejoined world, a module and its host sharing an assembly) is registered once
/// - a later scan is silently ignored, since re-registering would double every error the check reports.
/// </summary>
public static class ExCheckRegistry {
  private static readonly HashSet<Type> _seen = [];
  private static readonly List<(
    Type Type,
    System.Func<ICheckSource, string, CheckResult> Run
  )> _registered = [];

  /// <summary>Every check registered so far, in registration order.</summary>
  internal static IReadOnlyList<(
    Type Type,
    System.Func<ICheckSource, string, CheckResult> Run
  )> Registered => _registered;

  /// <summary>Drops every registration. For tests only - a running game never calls this.</summary>
  internal static void Clear() {
    _seen.Clear();
    _registered.Clear();
  }

  /// <summary>
  /// Registers every <see cref="ExCheckRegisterAttribute"/>-decorated check in <paramref name="asm"/>
  /// (default: the calling assembly). Call once from <c>ModSystem.Start</c>, alongside
  /// <see cref="Registries.EntityRegistry.RegisterAll"/>.
  /// </summary>
  public static void RegisterAll(ICoreAPI api, Mod mod, Assembly? asm = null) {
    asm ??= Assembly.GetCallingAssembly();
    string modId = mod.Info.ModID;

    ReflectionScan.ForEachAttributed<ExCheckRegisterAttribute>(
      asm,
      (attr, type) => Register(api, modId, type)
    );
  }

  private static void Register(ICoreAPI api, string modId, Type type) {
    if (!_seen.Add(type))
      return;

    MethodInfo? run = type.GetMethod(
      "Run",
      BindingFlags.Public | BindingFlags.Static,
      binder: null,
      types: [typeof(ICheckSource), typeof(string)],
      modifiers: null
    );
    if (run == null || run.ReturnType != typeof(CheckResult)) {
      api.Logger.Warning(
        "[{0}] {1} is marked [ExCheckRegister] but has no `static CheckResult Run(ICheckSource, string)`; skipped.",
        modId,
        type.FullName
      );
      return;
    }

    _registered.Add(
      (
        type,
        (System.Func<ICheckSource, string, CheckResult>)
          run.CreateDelegate(
            typeof(System.Func<ICheckSource, string, CheckResult>)
          )
      )
    );
  }
}
