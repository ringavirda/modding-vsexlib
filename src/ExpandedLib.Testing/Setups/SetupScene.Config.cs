#if GAME_GE_1_22
using System.Globalization;
using ExpandedLib.Config;
using ExpandedLib.Scenarios;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Testing;

public sealed partial class SetupScene
{
  /// <summary>Writes the case's <c>config</c> through the registers in
  /// <see cref="ExConfigProfiles"/>; <see cref="Dispose"/> sets each value back.</summary>
  private void ApplyConfig()
  {
    foreach (SetupConfigValue value in Model.Config)
    {
      string where = $"config.{value.Mod}.{value.Key}";
      if (!ExConfigProfiles.TryGet(value.Mod, out IExConfigAccess access))
        throw Fail(where, $"\"{value.Mod}\" has no config register.");
      if (!access.TryGet(value.Key, out string name, out string old))
        throw Fail(where, $"the register holds no value \"{value.Key}\".");
      ExConfigEditResult result = access.Set(name, Raw(value.Value));
      if (result.Status != ExConfigEditStatus.Ok)
        throw Fail(where, $"the register refuses it: {result.Status}.");
      _config.Add((access, name, old));
    }
  }

  private static string Raw(JValue value) =>
    value.Type == JTokenType.Boolean
      ? ((bool)value ? "true" : "false")
      : System.Convert.ToString(value.Value, CultureInfo.InvariantCulture)
        ?? "";
}
#endif
