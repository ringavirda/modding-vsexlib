using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Checks;

/// <summary>Runs every content check ExpandedLib ships against one <see cref="ICheckSource"/>, and
/// the loaded checks against an <see cref="ILoadedGame"/>. A mod exempts a finding it ships
/// knowingly with <see cref="Exempt(string, string, string[], string)"/> and declares what its
/// machines make with <see cref="Produces"/>.</summary>
/// <remarks><see cref="ExpandedLibModSystem.AssetsFinalize"/> calls <see cref="All(ICoreAPI)"/>;
/// <c>/exmod verify</c> calls <see cref="Verify"/>, which adds the loaded checks. A mod's own
/// <see cref="ExCheckRegisterAttribute"/>-decorated checks run after the content checks.</remarks>
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
    StageWildcardsCheck.Run,
    GridRecipeShapeCheck.Run,
    GridRecipeCollisionCheck.Run,
    GridOutputVariantCheck.Run,
  ];

  // The checks that read the loaded game; never run at load, only by Verify.
  private static readonly System.Func<
    ILoadedGame,
    string,
    CheckResult
  >[] _loaded =
  [
    ObtainabilityCheck.Run,
    VanillaGridCollisionCheck.Run,
    GameReferencesCheck.Run,
    LoadedStageWildcardsCheck.Run,
    CollectibleCollectionsCheck.Run,
  ];

  private static readonly List<Exemption> _exemptions = [];

  private static readonly List<Declaration> _produced = [];

  private sealed record Exemption(
    string Domain,
    string Rule,
    string[] Codes,
    string Reason
  ) {
    public bool Equals(Exemption? other) =>
      other is not null
      && Domain == other.Domain
      && Rule == other.Rule
      && Reason == other.Reason
      && Codes.SequenceEqual(other.Codes);

    public override int GetHashCode() => HashCode.Combine(Domain, Rule, Reason);

    public override string ToString() =>
      $"{Rule} exemption of {string.Join(" + ", Codes)}";
  }

  /// <summary>One code a mod declared with <see cref="Produces"/>.</summary>
  internal sealed record Declaration(string Domain, string Code, string Source);

  /// <summary>Runs every check against every domain <paramref name="source"/> covers.</summary>
  public static IReadOnlyList<CheckResult> All(ICheckSource source) =>
    [.. source.Domains.SelectMany(domain => For(source, domain))];

  /// <summary>Runs every check against <paramref name="domain"/>, regardless of whether <paramref
  /// name="source"/> covers it.</summary>
  /// <returns>One result per check, then one named <c>Exempt</c> listing each exemption of
  /// <paramref name="domain"/> whose rule ran and matched no finding, when there is one.</returns>
  public static IReadOnlyList<CheckResult> For(
    ICheckSource source,
    string domain
  ) => Apply(domain, Content(source, domain), everyRule: false);

  /// <summary>Runs every loaded check against every domain <paramref name="game"/> covers.</summary>
  public static IReadOnlyList<CheckResult> Loaded(ILoadedGame game) =>
    [.. game.Domains.SelectMany(domain => LoadedFor(game, domain))];

  /// <summary>Runs every loaded check against <paramref name="domain"/>: obtainability, vanilla
  /// grid collisions, <c>game:</c> references, construction wildcards over vanilla codes and the
  /// collections vanilla dereferences.</summary>
  /// <returns>One result per loaded check, then one named <c>Exempt</c> listing each exemption of
  /// <paramref name="domain"/> whose rule ran and matched no finding, when there is one.</returns>
  public static IReadOnlyList<CheckResult> LoadedFor(
    ILoadedGame game,
    string domain
  ) => Apply(domain, LoadedOnly(game, domain), everyRule: false);

  /// <summary>Runs the content checks and the loaded checks against <paramref name="domain"/>,
  /// or against every domain <paramref name="game"/> covers when it is null.</summary>
  /// <returns>One result per check and domain, then per domain one named <c>Exempt</c> listing
  /// each of its exemptions that matched no finding, the rule it names unknown or not.</returns>
  public static IReadOnlyList<CheckResult> Verify(
    ILoadedGame game,
    string? domain
  ) =>
    [
      .. (domain == null ? game.Domains : [domain]).SelectMany(d =>
        Apply(d, [.. Content(game, d), .. LoadedOnly(game, d)], everyRule: true)
      ),
    ];

  /// <summary>Runs every check against the live game state, over a fresh <see cref="AssetCheckSource"/>.</summary>
  public static IReadOnlyList<CheckResult> All(ICoreAPI api) =>
    All(new AssetCheckSource(api));

  /// <summary>Takes every finding of <paramref name="rule"/> in <paramref name="domain"/> that
  /// names <paramref name="code"/> out of the errors, for a defect a mod ships knowingly. Call it
  /// from a mod's <c>Start</c>; a world starting to load drops every exemption.</summary>
  /// <param name="domain">The domain whose run the finding is reported in.</param>
  /// <param name="rule">The check's name as its result carries it, e.g.
  /// <c>"GridRecipeShape"</c>.</param>
  /// <param name="code">What the finding names: a code, or a recipe's <c>file#position</c>.
  /// Matched as <see cref="Exempt(string, string, string[], string)"/> matches each of its
  /// codes.</param>
  /// <param name="reason">Why the finding stands, logged beside it.</param>
  /// <exception cref="ArgumentException">An argument is null or empty.</exception>
  /// <remarks>A finding about two things, such as a collision of two recipes, is taken only by an
  /// exemption naming both, through the other overload.</remarks>
  public static void Exempt(
    string domain,
    string rule,
    string code,
    string reason
  ) => Exempt(domain, rule, [code], reason);

  /// <summary>Takes every finding of <paramref name="rule"/> in <paramref name="domain"/> that
  /// names each of <paramref name="codes"/> out of the errors, for a defect a mod ships knowingly.
  /// Call it from a mod's <c>Start</c>; a world starting to load drops every exemption.</summary>
  /// <param name="domain">The domain whose run the finding is reported in.</param>
  /// <param name="rule">The check's name as its result carries it, e.g.
  /// <c>"GridRecipeCollision"</c>.</param>
  /// <param name="codes">What the finding names: codes, recipes' <c>file#position</c> or words of
  /// the defect (<c>"key G"</c>), each a whole word, so <c>a.json#1</c> does not take
  /// <c>a.json#10</c>; a finding's <see cref="CheckResult.Subjects"/> must all be among them.</param>
  /// <param name="reason">Why the finding stands, logged beside it.</param>
  /// <exception cref="ArgumentException">An argument or one of <paramref name="codes"/> is null or
  /// empty, or <paramref name="codes"/> holds none.</exception>
  /// <remarks>An exemption matching no finding of a run of its rule is reported in that run, one
  /// whose rule never ran by <see cref="Verify"/>, and one taking only what an earlier one took as
  /// its duplicate. The same exemption given twice is kept once.</remarks>
  public static void Exempt(
    string domain,
    string rule,
    string[] codes,
    string reason
  ) {
    if (codes is not { Length: > 0 })
      throw new ArgumentException("an exemption names a code", nameof(codes));
    Require(
      "an exemption names all four",
      [
        (domain, nameof(domain)),
        (rule, nameof(rule)),
        (reason, nameof(reason)),
        .. codes.Select(c => (c, nameof(codes))),
      ]
    );
    var exemption = new Exemption(domain, rule, [.. codes], reason);
    if (!_exemptions.Contains(exemption))
      _exemptions.Add(exemption);
  }

  /// <summary>Declares that a machine makes <paramref name="code"/>, a code no recipe registry
  /// or exlib process catalogue names as an output: a machine's product, or a block cast or poured
  /// in place. <see cref="ObtainabilityCheck"/> counts every loaded block and item it matches as
  /// made, in every domain's run. Call it from a mod's <c>Start</c>; a world starting to load
  /// drops every declaration.</summary>
  /// <param name="domain">The declaring mod's domain, whose obtainability run reports a
  /// declaration that matches no loaded block or item.</param>
  /// <param name="code">A domain-qualified code or wildcard, e.g. <c>"iiex:diagram-*"</c>.</param>
  /// <param name="source">The machine that makes it, named when the declaration is
  /// reported.</param>
  /// <exception cref="ArgumentException">An argument is null or empty.</exception>
  /// <remarks>The same declaration given twice is kept once.</remarks>
  public static void Produces(string domain, string code, string source) {
    Require(
      "a declaration names all three",
      (domain, nameof(domain)),
      (code, nameof(code)),
      (source, nameof(source))
    );
    var declaration = new Declaration(domain, code, source);
    if (!_produced.Contains(declaration))
      _produced.Add(declaration);
  }

  /// <summary>Every <see cref="Produces"/> declaration of the current world, in call
  /// order.</summary>
  internal static IReadOnlyList<Declaration> Produced() => _produced;

  /// <summary>Drops every exemption and every <see cref="Produces"/> declaration; run when a
  /// world starts loading.</summary>
  internal static void ClearDeclarations() {
    _exemptions.Clear();
    _produced.Clear();
  }

  private static void Require(
    string message,
    params (string Value, string Name)[] arguments
  ) {
    foreach ((string value, string name) in arguments)
      if (string.IsNullOrEmpty(value))
        throw new ArgumentException(message, name);
  }

  private static List<CheckResult> Content(
    ICheckSource source,
    string domain
  ) =>
    [
      .. _checks.Select(run => run(source, domain)),
      .. ExCheckRegistry.Registered.Select(check =>
        RunIsolated(check, source, domain)
      ),
    ];

  private static List<CheckResult> LoadedOnly(
    ILoadedGame game,
    string domain
  ) => [.. _loaded.Select(run => run(game, domain))];

  // Moves each exempted finding out of its result's errors, then reports every exemption of the
  // domain that took none: of a rule that ran, or of any rule when everyRule.
  private static List<CheckResult> Apply(
    string domain,
    List<CheckResult> results,
    bool everyRule
  ) {
    Exemption[] own = [.. _exemptions.Where(e => e.Domain == domain)];
    var used = new HashSet<Exemption>();
    var duplicates = new Dictionary<Exemption, Exemption>();
    var applied = new List<CheckResult>();
    foreach (CheckResult result in results) {
      Exemption[] rule = [.. own.Where(e => e.Rule == result.Check)];
      var errors = new List<string>();
      var exempted = new List<string>(result.Exempted);
      foreach (string error in result.Errors) {
        IReadOnlyList<string> subjects = result.Subjects.GetValueOrDefault(
          error,
          []
        );
        Exemption[] taking =
        [
          .. rule.Where(e =>
            e.Codes.All(c => Names(error, c))
            && subjects.All(s => e.Codes.Contains(s))
          ),
        ];
        if (taking.Length == 0) {
          errors.Add(error);
          continue;
        }
        used.Add(taking[0]);
        foreach (Exemption again in taking.Skip(1))
          duplicates.TryAdd(again, taking[0]);
        exempted.Add($"{error} (exempt: {taking[0].Reason})");
      }
      applied.Add(result with { Errors = errors, Exempted = exempted });
    }
    HashSet<string> ran = [.. results.Select(r => r.Check)];
    string[] unused =
    [
      .. own.Where(e =>
          !used.Contains(e) && (everyRule || ran.Contains(e.Rule))
        )
        .Select(e =>
          duplicates.TryGetValue(e, out Exemption? first)
            ? $"{e} duplicates the {first} ({e.Reason})"
            : $"{e} matches no finding ({e.Reason})"
        ),
    ];
    if (unused.Length > 0)
      applied.Add(new CheckResult("Exempt", domain, unused));
    return applied;
  }

  /// <summary>Whether <paramref name="text"/> holds <paramref name="code"/> as a whole word:
  /// starting at the text's start or after white space or punctuation other than <c>:</c> and
  /// <c>.</c>, and ending at the text's end, before white space or such punctuation, or before a
  /// <c>:</c> or <c>.</c> that ends the text or precedes white space.</summary>
  internal static bool Names(string text, string code) {
    for (
      int at = text.IndexOf(code, StringComparison.Ordinal);
      at >= 0;
      at = text.IndexOf(code, at + 1, StringComparison.Ordinal)
    ) {
      int end = at + code.Length;
      bool before = at == 0 || Bounds(text[at - 1]);
      bool after =
        end == text.Length
        || Bounds(text[end])
        || (
          text[end] is '.' or ':'
          && (end + 1 == text.Length || char.IsWhiteSpace(text[end + 1]))
        );
      if (before && after)
        return true;
    }
    return false;
  }

  private static bool Bounds(char c) =>
    char.IsWhiteSpace(c)
    || c is ',' or ';' or '(' or ')' or '[' or ']' or '\'' or '"';

  // Catches a thrown exception and reports it as one error naming the check.
  private static CheckResult RunIsolated(
    (Type Type, System.Func<ICheckSource, string, CheckResult> Run) check,
    ICheckSource source,
    string domain
  ) {
    try {
      return check.Run(source, domain);
    } catch (Exception e) {
      return new CheckResult(
        check.Type.Name,
        domain,
        [$"{check.Type.FullName} threw: {e}"]
      );
    }
  }

  /// <summary>Logs <paramref name="results"/>: one Notification per check naming its domain and error
  /// count, then each error on its own Error line and each exempted finding on its own
  /// Notification line.</summary>
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
      foreach (string exempted in result.Exempted)
        logger.Notification("[exlib]   {0}", exempted);
    }
  }
}
