using System.ComponentModel;
using System.Globalization;
using System.Linq;
using ExpandedLib.Registries;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ExpandedLib.Industry.Helpers;

/// <summary>
/// Attaches <c>.exmod sound [0-1]</c> to the shared <c>.exmod</c> root: shows or sets the player's
/// machine-sound volume, persisted through <see cref="ExPreferences"/>. Client-only.
/// </summary>
[SubCommandRegister(Side = EnumAppSide.Client)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SoundSubCommand : IExSubCommand {
  public string ParentName => "exmod";

  public void Register(ICoreAPI api, Mod mod, IChatCommand parent) {
    var capi = (ICoreClientAPI)api;
    var pref = ExPreferences.Find("sound") ?? new SoundVolumePreference();

    parent
      .BeginSubCommand(pref.Key)
      .WithDescription(Lang.Get("exlib:command-sound-desc"))
      .WithArgs(capi.ChatCommands.Parsers.OptionalWord("value"))
      .HandleWith(args =>
        Dispatch(capi.World.Player.PlayerUID, pref, args[0] as string)
      )
      .EndSubCommand();
  }

  /// <summary>The command's logic with fluent arg parsing stripped, callable directly by a test.</summary>
  internal static TextCommandResult Dispatch(
    string playerUid,
    IExPreference pref,
    string? word
  ) {
    string label = Lang.Get("exlib:pref-sound-label");
    if (string.IsNullOrEmpty(word))
      return TextCommandResult.Success(
        Lang.Get(
          "exlib:command-pref-current",
          label,
          Percent(ExPreferences.GetForPlayer(playerUid, pref.Key))
        )
      );

    if (
      !float.TryParse(
        word,
        NumberStyles.Float,
        CultureInfo.InvariantCulture,
        out float asked
      )
      || pref.Options.FirstOrDefault(o => Parse(o) == asked) is not { } option
    )
      return TextCommandResult.Error(
        Lang.Get(
          "exlib:command-pref-invalid",
          word,
          label,
          string.Join(", ", pref.Options)
        )
      );

    ExPreferences.SetForPlayer(playerUid, pref.Key, option);
    return TextCommandResult.Success(
      Lang.Get("exlib:command-pref-set", label, Percent(option))
    );
  }

  private static float Parse(string option) =>
    float.Parse(option, CultureInfo.InvariantCulture);

  private static string Percent(string option) =>
    ((int)System.Math.Round(Parse(option) * 100)).ToString(
      CultureInfo.InvariantCulture
    ) + "%";
}
