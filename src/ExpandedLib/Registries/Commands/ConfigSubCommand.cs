using System.ComponentModel;
using System.Linq;
using ExpandedLib.Config;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ExpandedLib.Registries;

/// <summary>
/// Adds <c>/exmod config [&lt;mod&gt; [&lt;value&gt; [&lt;new&gt;]]]</c>: the mod-agnostic switch for
/// gameplay tunables exposed through <see cref="ExConfigProfiles"/>. Server-side; the <c>/exmod</c>
/// root requires <c>controlserver</c>.
/// </summary>
[SubCommandRegister(Side = EnumAppSide.Server)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ConfigSubCommand : RegistrySubCommand<IExConfigAccess> {
  public ConfigSubCommand()
    : base(
      "config",
      "exlib:command-config-desc",
      () => ExConfigProfiles.Codes,
      code => ExConfigProfiles.TryGet(code, out var config) ? config : null
    ) { }

  protected override string NoneRegisteredKey => "exlib:command-config-none";
  protected override string ListHeaderKey => "exlib:command-config-list";
  protected override string UnknownCodeKey => "exlib:command-config-unknown";

  protected override string Describe(IExConfigAccess config) =>
    $"{config.ModId} ({config.FileName})";

  // Keep '<' and '>' out of the result strings; both break the client's rendering.
  protected override TextCommandResult Set(
    IExConfigAccess config,
    string[] args
  ) {
    string languageCode = CallerLanguage;
    if (args.Length == 0)
      return TextCommandResult.Success(ListValues(config, languageCode));

    string name = args[0];

    // Read: print the current value.
    if (args.Length < 2) {
      if (!config.TryGet(name, out var canonical, out var value))
        return TextCommandResult.Error(
          Lang.GetL(
            languageCode,
            "exlib:command-config-novalue",
            name,
            config.ModId
          )
        );
      return TextCommandResult.Success(
        Lang.GetL(
          languageCode,
          "exlib:command-config-current",
          canonical,
          value
        )
      );
    }

    // Write: parse, validate, set and persist (applied immediately).
    string raw = args[1];
    var result = config.Set(name, raw);
    if (result.Status == ExConfigEditStatus.Ok)
      // Pushes the change to connected players without requiring a reconnect.
      Api.ModLoader.GetModSystem<ExConfigSyncModSystem>()
        ?.BroadcastSection(config);

    return result.Status switch {
      ExConfigEditStatus.Ok => TextCommandResult.Success(
        Lang.GetL(
          languageCode,
          "exlib:command-config-set",
          result.Name,
          result.OldValue,
          result.NewValue
        )
      ),
      ExConfigEditStatus.ParseFailed => TextCommandResult.Error(
        Lang.GetL(
          languageCode,
          "exlib:command-config-parsefail",
          raw,
          result.Name,
          result.Expected
        )
      ),
      ExConfigEditStatus.OutOfRange => TextCommandResult.Error(
        Lang.GetL(
          languageCode,
          "exlib:command-config-range",
          result.Name,
          result.Range
        )
      ),
      _ => TextCommandResult.Error(
        Lang.GetL(
          languageCode,
          "exlib:command-config-novalue",
          name,
          config.ModId
        )
      ),
    };
  }

  private static string ListValues(IExConfigAccess config, string languageCode) {
    var lines = config.ValueNames.Select(n =>
      config.TryGet(n, out var canonical, out var value)
        ? $"  {canonical} = {value}"
        : n
    );
    return Lang.GetL(languageCode, "exlib:command-config-values", config.ModId)
      + "\n"
      + string.Join("\n", lines);
  }
}
