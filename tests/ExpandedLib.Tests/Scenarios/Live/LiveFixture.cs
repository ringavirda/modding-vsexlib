using System.Collections.Generic;
using System.IO;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using ExpandedLib.Testing.Live;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>A plot at (100, 50, 100) in a <see cref="TestWorld"/> that knows <c>game:stone</c>, with a
/// clock the test moves and the lines the steps print. <see cref="WithSamples"/> adds the twin-tub
/// blower and the plated pipes.</summary>
internal sealed class LiveFixture {
  public static readonly BlockPos Origin = new(100, 50, 100);

  public TestWorld Test { get; } = new();

  public Block Stone { get; } =
    new() { BlockId = 5, Code = new AssetLocation("game:stone") };

  public double Clock { get; set; }

  public List<string> Lines { get; } = [];

  public ScenarioErrors Errors { get; }

  public ScenarioContext Context { get; }

  public static LiveFixture WithSamples(params string[] allowed) =>
    new(true, allowed);

  public LiveFixture(params string[] allowed)
    : this(false, allowed) { }

  private LiveFixture(bool samples, string[] allowed) {
    if (samples) {
      Test.LoadAssets([
        Path.Combine(RepoPaths.Root, "samples", "TwinTubBlower"),
        Path.Combine(RepoPaths.Root, "samples", "PlatedPipes"),
      ]);
      Test.StartModSystem<MechanicalPowerMod>();
    }
    Test.Register(Stone);
    Errors = new ScenarioErrors([
      .. System.Linq.Enumerable.Select(
        allowed,
        p => new System.Text.RegularExpressions.Regex(p)
      ),
    ]);
    Context = new ScenarioContext(
      Test.World,
      new ScenarioWorld(Test.World, Origin.Copy(), Test.Networks),
      () => Clock,
      Errors,
      Lines.Add,
      "case.json"
    );
  }

  /// <summary>A scenario file text whose steps are <paramref name="steps"/> (a JSON array),
  /// with root keys added by <paramref name="extra"/>.</summary>
  public static string Text(string steps, string extra = "{}") {
    var root = JObject.Parse(
      $$"""
      { setup: "case", description: "a case", mods: [], seconds: 6, record: {}, steps: {{steps}} }
      """
    );
    root.Merge(JObject.Parse(extra));
    return root.ToString();
  }

  public static ScenarioCase Case(string steps, string extra = "{}") =>
    ScenarioCase.Load("exlib/case", Text(steps, extra), "case.json");

  /// <summary>Runs <paramref name="steps"/> to the end, moving the clock by
  /// <paramref name="stepSeconds"/> per tick; returns how many ticks it yielded.</summary>
  public int Run(IReadOnlyList<SetupStep> steps, double stepSeconds = 0.5) {
    int ticks = 0;
    foreach (bool _ in ScenarioSteps.Run(Context, steps, "")) {
      ticks++;
      Clock += stepSeconds;
    }
    return ticks;
  }
}
