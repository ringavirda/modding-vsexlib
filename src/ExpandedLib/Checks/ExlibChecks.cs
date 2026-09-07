using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Checks;

/// <summary>
/// Runs every content check ExpandedLib ships against one <see cref="ICheckSource"/>, so a JSON-only
/// modder gets "your recipe names a code that does not exist" as a log line, without ever opening
/// xUnit. <see cref="ExpandedLibModSystem.AssetsFinalize"/> runs <see cref="All(ICoreAPI)"/> after the
/// catalogues load; <c>/exmod verify</c> runs it on demand. A mod's own <see cref="ExCheckRegisterAttribute"/>-decorated
/// checks (see <see cref="ExCheckRegistry"/>) run after the eight shipped here, in registration order.
/// </summary>
public static class ExlibChecks {
  // One entry per check. Order is the order results and log lines come out in - cheapest and most
  // load-bearing (does a def even resolve to a real block) first.
  private static readonly System.Func<
    ICheckSource,
    string,
    CheckResult
  >[] _checks =
  [
    DefinitionCatalogueCheck.Run,
    LateDefinitionCheck.Run,
    MultiblockCodesCheck.Run,
    RecipeCodesCheck.Run,
    LangCoverageCheck.Run,
    NetworkNodeContractCheck.Run,
    PinnedNetworkNodesCheck.Run,
    CodePrefixCollisionCheck.Run,
  ];

  /// <summary>Runs every check against every domain <paramref name="source"/> covers.</summary>
  public static IReadOnlyList<CheckResult> All(ICheckSource source) =>
    [.. source.Domains.SelectMany(domain => For(source, domain))];

  /// <summary>
  /// Runs every check against one <paramref name="domain"/>, regardless of whether
  /// <paramref name="source"/> covers it - <c>/exmod verify &lt;domain&gt;</c> uses this to let a
  /// modder point at a domain <see cref="ICheckSource.Domains"/> would not scope in on its own.
  /// </summary>
  public static IReadOnlyList<CheckResult> For(
    ICheckSource source,
    string domain
  ) =>
    [
      .. _checks.Select(run => run(source, domain)),
      .. ExCheckRegistry.Registered.Select(check =>
        RunIsolated(check, source, domain)
      ),
    ];

  // Wraps a registered check the way ExModuleHost.Isolate wraps a module phase: a throw is caught and
  // reported as one error naming the check, rather than taking down every shipped check's result.
  private static CheckResult RunIsolated(
    (
      System.Type Type,
      System.Func<ICheckSource, string, CheckResult> Run
    ) check,
    ICheckSource source,
    string domain
  ) {
    try {
      return check.Run(source, domain);
    } catch (System.Exception e) {
      return new CheckResult(
        check.Type.Name,
        domain,
        [$"{check.Type.FullName} threw: {e}"]
      );
    }
  }

  /// <summary>Runs every check against the live game state, over a fresh <see cref="AssetCheckSource"/>.</summary>
  public static IReadOnlyList<CheckResult> All(ICoreAPI api) =>
    All(new AssetCheckSource(api));

  /// <summary>
  /// Logs <paramref name="results"/>: one Notification per check naming its domain and error count,
  /// then each error on its own Error line. A modder scanning the log for "0 error(s)" on every
  /// line knows everything passed without reading further.
  /// </summary>
  public static void Log(ILogger logger, IReadOnlyList<CheckResult> results) {
    foreach (CheckResult result in results) {
      logger.Notification(
        "[exlib] check {0} ({1}): {2} error(s)",
        result.Check,
        result.Domain,
        result.Errors.Count
      );
      foreach (string error in result.Errors)
        logger.Error("[exlib]   {0}", error);
    }
  }
}
