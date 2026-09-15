using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Checks;

/// <summary>
/// Runs every content check ExpandedLib ships against one <see cref="ICheckSource"/>.
/// <see cref="ExpandedLibModSystem.AssetsFinalize"/> and <c>/exmod verify</c> call
/// <see cref="All(ICoreAPI)"/>; a mod's own <see cref="ExCheckRegisterAttribute"/>-decorated checks run after.
/// </summary>
public static class ExlibChecks {
  // One entry per check; order is the order results and log lines are emitted in.
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

  /// <summary>Runs every check against <paramref name="domain"/>, regardless of whether <paramref
  /// name="source"/> covers it.</summary>
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

  // Catches a thrown exception and reports it as one error naming the check.
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

  /// <summary>Logs <paramref name="results"/>: one Notification per check naming its domain and error
  /// count, then each error on its own Error line.</summary>
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
