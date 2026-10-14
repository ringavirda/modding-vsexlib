using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ExpandedLib.Scenarios;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Config;

namespace ExpandedLib.Testing.Live;

/// <summary>A scenario file as the live runner runs it: the setup file model, plus the keys the
/// model leaves raw (<c>plot</c>, <c>settle</c>, <c>allowErrors</c>).</summary>
internal sealed class ScenarioCase(
  string name,
  SetupFileModel model,
  int[] plot,
  double settle,
  IReadOnlyList<Regex> allowErrors
) {
  public const double DefaultSettle = 3;

  private static readonly string[] Refused =
  [
    "checks",
    "climate",
    "variants",
    "tickOrders",
  ];

  /// <summary>The scenario's name, <c>&lt;modid&gt;/&lt;path&gt;</c>.</summary>
  public string Name => name;

  public SetupFileModel Model => model;

  /// <summary>The plot size in blocks: x, y, z.</summary>
  public int[] Plot => plot;

  /// <summary>Seconds the runner waits after the last step.</summary>
  public double Settle => settle;

  public IReadOnlyList<Regex> AllowErrors => allowErrors;

  /// <summary>The case timeout in seconds of server clock.</summary>
  public double Timeout => model.Timeout;

  /// <summary>Parses <paramref name="text"/> as the scenario <paramref name="name"/> read from
  /// <paramref name="file"/>, for a server running game series <paramref name="series"/>
  /// (<c>1.22</c>) with the mods <paramref name="loadedMods"/> loaded.</summary>
  /// <exception cref="SetupFileException">The file is malformed, uses a key or a step the
  /// live runner does not run yet, needs a newer series, or names a mod that is not loaded;
  /// the message names the file, where and the reason.</exception>
  public static ScenarioCase Load(
    string name,
    string text,
    string file,
    IReadOnlyCollection<string> loadedMods,
    string series
  ) {
    SetupFileModel model = SetupFileModel.Parse(text, file);
    JObject root = SetupJson.ParseObject(text, file);
    var top = new SetupJson.Where(file, "");
    foreach (SetupStep step in SetupStepWalk.Of(model))
      if (ScenarioSteps.Refusal(step) is { } reason)
        throw top.Fail(reason);
    foreach (string key in Refused)
      if (root[key] != null)
        throw top.In(key).Fail("is not read by the live runner yet.");
    if (!GameVersion.IsAtLeastVersion(series, model.Game))
      throw top.In("game")
        .Fail($"needs {model.Game}; the server runs {series}.");
    foreach (string mod in model.Mods)
      if (!loadedMods.Contains(mod, StringComparer.OrdinalIgnoreCase))
        throw top.In("mods").Fail($"\"{mod}\" is not loaded.");
    if (model.Pack is { After.Count: > 0 })
      throw top.In("pack")
        .In("after")
        .Fail("is not read by the live runner yet.");

    int[] plot = [16, 16, 16];
    if (root["plot"] != null) {
      JArray a = SetupJson.Array(root["plot"], top.In("plot"));
      if (a.Count != 3)
        throw top.In("plot").Fail("expected [x, y, z].");
      plot =
      [
        .. a.Select((t, i) => SetupJson.Integer(t, top.In("plot").At(i))),
      ];
      if (plot.Any(n => n < 1))
        throw top.In("plot").Fail("takes sizes of at least 1.");
    }
    double settle =
      root["settle"] == null
        ? DefaultSettle
        : SetupJson.Number(root["settle"], top.In("settle"));
    if (settle < 0)
      throw top.In("settle").Fail("is not negative.");
    IReadOnlyList<Regex> allow =
      root["allowErrors"] == null
        ? []
        :
        [
          .. SetupJson
            .Strings(root["allowErrors"], top.In("allowErrors"))
            .Select(p => new Regex(p)),
        ];
    return new ScenarioCase(name, model, plot, settle, allow);
  }
}
