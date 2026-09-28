using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ExpandedLib.Checks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ExpandedLib.Registries;

/// <summary>
/// Adds <c>/exmod verify [&lt;mod&gt;]</c>: runs every content check and every loaded check in
/// <see cref="ExlibChecks"/> against the live game state and reports it on demand.
/// </summary>
[SubCommandRegister(Side = EnumAppSide.Server)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class VerifySubCommand : IExSubCommand {
  public string ParentName => "exmod";

  public void Register(ICoreAPI api, Mod mod, IChatCommand parent) {
    parent
      .BeginSubCommand("verify")
      .WithDescription(Lang.Get("exlib:command-verify-desc"))
      .WithArgs(api.ChatCommands.Parsers.OptionalWord("domain"))
      .HandleWith(args => Dispatch(api, args[0] as string, args.LanguageCode))
      .EndSubCommand();
  }

  /// <summary>The command's logic with fluent arg parsing stripped, callable directly by a test;
  /// the reply is worded in <paramref name="languageCode"/>.</summary>
  internal static TextCommandResult Dispatch(
    ICoreAPI api,
    string? domain,
    string languageCode
  ) {
    domain = domain?.ToLowerInvariant();

    if (domain != null) {
      List<string> loaded =
      [
        .. api.ModLoader.Mods.Select(m => m.Info.ModID).OrderBy(d => d),
      ];
      if (!loaded.Contains(domain))
        return TextCommandResult.Error(
          Lang.GetL(
            languageCode,
            "exlib:command-verify-unknown",
            domain,
            string.Join(", ", loaded)
          )
        );
    }
    IReadOnlyList<CheckResult> results = ExlibChecks.Verify(
      new AssetCheckSource(api),
      domain
    );

    // Logged in full regardless of what the chat window can show.
    ExlibChecks.Log(api.Logger, results);
    return Report(results, languageCode);
  }

  /// <summary>The chat reply over <paramref name="results"/>: a summary line, the first ten errors
  /// and a count of the rest, worded in <paramref name="languageCode"/>. An error when any check
  /// found one.</summary>
  internal static TextCommandResult Report(
    IReadOnlyList<CheckResult> results,
    string languageCode
  ) {
    List<string> errors = [.. results.SelectMany(r => r.Errors)];
    var lines = new List<string>
    {
      Lang.GetL(
        languageCode,
        "exlib:command-verify-summary",
        results.Count,
        errors.Count
      ),
    };
    lines.AddRange(errors.Take(10));
    if (errors.Count > 10)
      lines.Add(
        Lang.GetL(languageCode, "exlib:command-verify-more", errors.Count - 10)
      );

    return errors.Count == 0
      ? TextCommandResult.Success(string.Join("\n", lines))
      : TextCommandResult.Error(string.Join("\n", lines));
  }
}
