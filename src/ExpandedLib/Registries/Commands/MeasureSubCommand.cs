using System.ComponentModel;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Registries;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ExpandedLib.Registries;

/// <summary>
/// Attaches <c>.exmod measure [metric|imperial]</c> to the shared <c>.exmod</c> root: shows or sets
/// the per-player display unit system, persisted through <see cref="ExPreferences"/>. Client-only.
/// </summary>
[SubCommandRegister(Side = EnumAppSide.Client)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class MeasureSubCommand : IExSubCommand {
  public string ParentName => "exmod";

  public void Register(ICoreAPI api, Mod mod, IChatCommand parent) {
    var capi = (ICoreClientAPI)api;
    var pref = ExPreferences.Find("measure") ?? new MeasurePreference();
    string domain = mod.Info.ModID;

    parent
      .BeginSubCommand(pref.Key)
      .WithDescription(Lang.Get(domain + ":command-" + pref.Key + "-desc"))
      .WithArgs(capi.ChatCommands.Parsers.OptionalWord("value"))
      .HandleWith(args => Dispatch(capi, domain, pref, args[0] as string))
      .EndSubCommand();
  }

  /// <summary>The command's logic with fluent arg parsing stripped, callable directly by a test.</summary>
  internal static TextCommandResult Dispatch(
    ICoreClientAPI api,
    string domain,
    IExPreference pref,
    string? rawWord
  ) {
    string uid = api.World.Player.PlayerUID;
    string? word = rawWord?.ToLowerInvariant();
    string label = Lang.Get(domain + ":pref-" + pref.Key + "-label");

    // No argument: report the current setting.
    if (string.IsNullOrEmpty(word))
      return TextCommandResult.Success(
        Lang.Get(
          "exlib:command-pref-current",
          label,
          ValueLabel(domain, pref, ExPreferences.GetForPlayer(uid, pref.Key))
        )
      );

    if (!pref.Options.Contains(word))
      return TextCommandResult.Error(
        Lang.Get(
          "exlib:command-pref-invalid",
          word,
          label,
          string.Join(", ", pref.Options)
        )
      );

    string previous = ExPreferences.GetForPlayer(uid, pref.Key);
    ExPreferences.SetForPlayer(uid, pref.Key, word);

    // Handbook prose is unit-converted only when its pages are built; a switch needs a rebuild.
    if (previous != word)
      HandbookUnitPatch.Rebuild(api);

    return TextCommandResult.Success(
      Lang.Get("exlib:command-pref-set", label, ValueLabel(domain, pref, word))
    );
  }

  private static string ValueLabel(
    string domain,
    IExPreference pref,
    string value
  ) => Lang.Get(domain + ":pref-" + pref.Key + "-" + value);
}
