using System;
using System.Collections.Generic;
using System.Globalization;
using ExpandedLib.Config;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Scenarios;

/// <summary>A case's <c>config</c> written through the registers in
/// <see cref="ExConfigProfiles"/>, and set back when the case ends.</summary>
/// <param name="file">The setup file's name, which starts every failure message.</param>
internal sealed class SetupConfig(string file) {
  private readonly List<(IExConfigAccess Access, string Name, string Old)> _held =
    [];

  /// <summary>Writes each value in order, remembering what the register held.</summary>
  /// <param name="values">The case's <c>config</c> values.</param>
  /// <exception cref="InvalidOperationException">A mod has no register, a register holds no
  /// such value or refuses it. The values written before it stay written until
  /// <see cref="Restore"/>.</exception>
  public void Apply(IReadOnlyList<SetupConfigValue> values) {
    foreach (SetupConfigValue value in values) {
      string where = $"config.{value.Mod}.{value.Key}";
      if (!ExConfigProfiles.TryGet(value.Mod, out IExConfigAccess access))
        throw Fail(where, $"\"{value.Mod}\" has no config register.");
      if (!access.TryGet(value.Key, out string name, out string old))
        throw Fail(where, $"the register holds no value \"{value.Key}\".");
      ExConfigEditResult result = access.Set(name, Raw(value.Value));
      if (result.Status != ExConfigEditStatus.Ok)
        throw Fail(where, $"the register refuses it: {result.Status}.");
      _held.Add((access, name, old));
    }
  }

  /// <summary>Sets every written value back, last first. Does nothing when none is held.</summary>
  public void Restore() {
    for (int i = _held.Count - 1; i >= 0; i--)
      _held[i].Access.Set(_held[i].Name, _held[i].Old);
    _held.Clear();
  }

  private InvalidOperationException Fail(string where, string message) =>
    new($"{file}: {where}: {message}");

  private static string Raw(JValue value) =>
    value.Type == JTokenType.Boolean
      ? ((bool)value ? "true" : "false")
      : Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? "";
}
