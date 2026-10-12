#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace ExpandedLib.Tests.Setups;

/// <summary>The rows <see cref="SetupCases"/> makes of a folder of setup files and what
/// <see cref="SetupCases.Run"/> does with one (setups.md, Where it lives).</summary>
[Collection("Setups")]
public class SetupCasesTests : IDisposable
{
  private readonly string _folder = Path.Combine(
    Path.GetTempPath(),
    "exlib-setup-cases-" + Guid.NewGuid().ToString("N")
  );

  private string Recordings => Path.Combine(_folder, "Recordings");

  public SetupCasesTests()
  {
    Environment.SetEnvironmentVariable("EX_WRITE_SETUPS", null);
    Directory.CreateDirectory(_folder);
  }

  public void Dispose()
  {
    SetupPlacers.Clear();
    if (Directory.Exists(_folder))
      Directory.Delete(_folder, recursive: true);
  }

  private static readonly string HoldPath = Path.Combine(
    RepoPaths.Root,
    "tests",
    "ExpandedLib.Tests",
    "Setups",
    "Fixtures",
    "SetupHold"
  );

  private void Write(string name, string text) =>
    File.WriteAllText(Path.Combine(_folder, name), text);

  private SetupRunOptions Options(Action<string>? output = null) =>
    new()
    {
      ModFolder = id => id == "setuphold" ? HoldPath : SetupCaseTests.BoxPath,
      Recordings = Recordings,
      ExlibVersion = "0.8.4",
      BeforeLoad = w =>
      {
        SetupCaseTests.Register(w);
        SetupTickOrderTests.Register(w, []);
      },
      Output = output,
    };

  private IReadOnlyList<SetupCaseRow> Rows() =>
    [.. SetupCases.In(_folder).Select(r => (SetupCaseRow)r[0])];

  private SetupCaseRow Row(string name) =>
    Rows().Single(r => r.ToString() == name);

  private static string Ticking(string checks) =>
    "{\"setup\": \"ticks\", \"description\": \"d\", \"mods\": [\"setuphold\"], \"tickOrders\": [\"a\", \"b\"], "
    + "\"steps\": [{\"place\": {\"id\": \"b\", \"code\": \"setuphold:ticker\", \"at\": [1, 0, 0]}}, "
    + "{\"place\": {\"id\": \"a\", \"code\": \"setuphold:ticker\", \"at\": [0, 0, 0]}}], "
    + "\"seconds\": 6, \"record\": {}, \"checks\": ["
    + checks
    + "], \"variants\": {}}";

  private const string InStep =
    "{\"id\": \"sync\", \"says\": \"a keeps level with b\", \"always\": {\"at\": [0, 0, 0], \"member\": \"Lag\", \"equals\": 0}";

  private sealed class Info : IXunitSerializationInfo
  {
    private readonly Dictionary<string, object?> _values = [];

    public void AddValue(string key, object? value, Type? type = null) =>
      _values[key] = value;

    public object? GetValue(string key, Type type) => _values[key];

    public T GetValue<T>(string key) => (T)_values[key]!;
  }

  private sealed class DiscoveryOptions : ITestFrameworkDiscoveryOptions
  {
    public TValue GetValue<TValue>(string name) => default!;

    public void SetValue<TValue>(string name, TValue value) { }
  }

  #region Rows

  // Fails when the files are not read in name order, or a variant row comes before its main case.
  [Fact]
  public void A_folder_yields_each_file_s_main_case_then_its_variants_in_file_name_order()
  {
    Write("b.json", SetupCaseTests.Cases("\"low\": {}, \"high\": {}"));
    Write("a.json", SetupCaseTests.Cases(""));

    var rows = Rows();

    Assert.Equal(
      ["a.json:", "b.json:", "b.json:low", "b.json:high"],
      rows.Select(r => Path.GetFileName(r.File) + ":" + r.Variant)
    );
    Assert.Equal(
      ["cases", "cases", "cases/low", "cases/high"],
      rows.Select(r => r.ToString())
    );
    Assert.All(rows, r => Assert.Null(r.Skip));
  }

  // Fails when a recording in a folder below the setups is read as a setup file.
  [Fact]
  public void A_folder_below_the_setups_is_not_read()
  {
    Write("a.json", SetupCaseTests.Cases(""));
    Directory.CreateDirectory(Recordings);
    File.WriteAllText(Path.Combine(Recordings, "ignored.json"), "not a setup");

    Assert.Single(Rows());
  }

  // Fails when a file whose game is above the running one yields a runnable row, or a file at the
  // running game a skipped one.
  [Fact]
  public void A_file_for_a_later_game_yields_rows_skipped_with_the_reason()
  {
    Write(
      "late.json",
      SetupCaseTests
        .Cases("\"low\": {}")
        .Replace(
          "\"setup\": \"cases\",",
          "\"setup\": \"cases\", \"game\": \"9.9\","
        )
    );
    Write("now.json", SetupCaseTests.Cases("\"low\": {}"));

    var rows = Rows();

    Assert.All(
      rows.Where(r => r.File.EndsWith("late.json")),
      r => Assert.Contains("needs game 9.9", r.Skip)
    );
    Assert.Equal(2, rows.Count(r => r.File.EndsWith("late.json")));
    Assert.All(
      rows.Where(r => r.File.EndsWith("now.json")),
      r => Assert.Null(r.Skip)
    );
  }

  // Fails when the orders miss the main case or the variants that ask for them, or reach a variant
  // that does not.
  [Fact]
  public void A_file_naming_tick_orders_yields_the_main_case_and_the_asking_variants_in_each()
  {
    Write(
      "t.json",
      SetupCaseTests
        .Cases("\"ordered\": {\"tickOrders\": true}, \"plain\": {}")
        .Replace("\"steps\":", "\"tickOrders\": [\"a\", \"b\"], \"steps\":")
    );

    var names = Rows().Select(r => r.ToString()).ToList();

    Assert.Equal(3 + 6 + 6, names.Count);
    Assert.Equal(["cases", "cases/ordered", "cases/plain"], names.Take(3));
    Assert.Contains("cases@a,b,network", names);
    Assert.Contains("cases@b,network,a", names);
    Assert.Contains("cases/ordered@network,b,a", names);
    Assert.DoesNotContain(names, n => n.StartsWith("cases/plain@"));
    Assert.Equal(6, Rows().Count(r => r.Variant == null && r.Order != null));
  }

  // Fails when a row loses a field passing through xunit's discovery cache.
  [Fact]
  public void A_row_survives_xunit_serialization()
  {
    Write(
      "t.json",
      SetupCaseTests
        .Cases("\"ordered\": {\"tickOrders\": true}")
        .Replace(
          "\"setup\": \"cases\",",
          "\"setup\": \"cases\", \"game\": \"9.9\","
        )
        .Replace("\"steps\":", "\"tickOrders\": [\"a\", \"b\"], \"steps\":")
    );
    SetupCaseRow row = Row("cases/ordered@b,a,network");

    var info = new Info();
    row.Serialize(info);
    var back = new SetupCaseRow();
    back.Deserialize(info);

    Assert.Equal(row.File, back.File);
    Assert.Equal("cases/ordered@b,a,network", back.ToString());
    Assert.Equal(row.Skip, back.Skip);
    Assert.Contains("needs game 9.9", back.Skip);
  }

  #endregion

  #region Discoverer

#pragma warning disable xUnit1000
  private sealed class Probe
  {
    public static IEnumerable<object[]> Rows =>
      [
        [new SetupCaseRow("a.json", "ok", null, null, null)],
        [new SetupCaseRow("b.json", "late", null, null, "needs game 9.9")],
      ];

    [SetupTheory]
    [MemberData(nameof(Rows))]
    public void Case(SetupCaseRow row) { }
  }
#pragma warning restore xUnit1000

  // Fails when a row with a skip reason is discovered as a test that runs, or a row without one as
  // skipped.
  [Fact]
  public void The_discoverer_skips_a_row_that_carries_a_reason_and_no_other()
  {
    var method = typeof(Probe).GetMethod(nameof(Probe.Case))!;
    var testMethod = new TestMethod(
      new TestClass(
        new TestCollection(
          new TestAssembly(Reflector.Wrap(typeof(Probe).Assembly)),
          null,
          "probe"
        ),
        Reflector.Wrap(typeof(Probe))
      ),
      Reflector.Wrap(method)
    );
    IAttributeInfo theory = Reflector.Wrap(
      method
        .GetCustomAttributesData()
        .Single(a => a.AttributeType == typeof(SetupTheoryAttribute))
    );

    var cases = new SetupTheoryDiscoverer(new NullMessageSink())
      .Discover(new DiscoveryOptions(), testMethod, theory)
      .ToList();

    Assert.Equal(2, cases.Count);
    Assert.Null(cases[0].SkipReason);
    Assert.Equal("needs game 9.9", cases[1].SkipReason);
  }

  #endregion

  #region Running

  private static string Pair() =>
    SetupCaseTests.Check(
      "hold",
      "always",
      SetupCaseTests.Series("a", "equals", 5)
    )
    + ", "
    + SetupCaseTests.Check(
      "zero",
      "always",
      SetupCaseTests.Series("b", "equals", 0)
    );

  private const string Late =
    "\"after\": {\"b\": [{\"set\": {\"at\": [0, 0, 0], \"member\": {\"Level\": 2}}}]}";

  // Fails when Run reads modinfo.json only at the mod folder's root, so a mod laid out under src/
  // throws FileNotFoundException.
  [Fact]
  public void A_mod_whose_modinfo_sits_under_src_runs()
  {
    string mod = Path.Combine(_folder, "srcmod");
    string src = Path.Combine(mod, "src");
    Directory.CreateDirectory(src);
    File.Copy(
      Path.Combine(SetupCaseTests.BoxPath, "modinfo.json"),
      Path.Combine(src, "modinfo.json")
    );
    string assets = Path.Combine(SetupCaseTests.BoxPath, "assets");
    foreach (
      string file in Directory.GetFiles(
        assets,
        "*",
        SearchOption.AllDirectories
      )
    )
    {
      string to = Path.Combine(
        mod,
        "assets",
        Path.GetRelativePath(assets, file)
      );
      Directory.CreateDirectory(Path.GetDirectoryName(to)!);
      File.Copy(file, to);
    }
    Write("c.json", SetupCaseTests.Cases(""));
    SetupRunOptions options = new()
    {
      ModFolder = _ => mod,
      Recordings = Recordings,
      ExlibVersion = "0.8.4",
      BeforeLoad = w =>
      {
        SetupCaseTests.Register(w);
        SetupTickOrderTests.Register(w, []);
      },
    };

    SetupCases.Run(Row("cases"), options);

    Assert.Single(
      Directory.GetFiles(Recordings, "cases.json", SearchOption.AllDirectories)
    );
  }

  // Fails when a passing main case writes no recording, or writes it outside the folder of the
  // series that ran it.
  [Fact]
  public void A_main_case_writes_its_recording_under_the_series_that_ran_it()
  {
    Write("c.json", SetupCaseTests.Cases(""));

    SetupCases.Run(Row("cases"), Options());

    string written = Assert.Single(
      Directory.GetFiles(Recordings, "cases.json", SearchOption.AllDirectories)
    );
    string series = Path.GetFileName(Path.GetDirectoryName(written))!;
    Assert.Matches(new Regex(@"^\d+\.\d+$"), series);
    Assert.StartsWith(
      series + ".",
      Vintagestory.API.Config.GameVersion.ShortGameVersion
    );
  }

  // Fails when a second run does not compare with the committed recording, or a difference writes
  // no received file.
  [Fact]
  public void A_main_case_whose_recording_differs_from_the_committed_one_fails_with_a_received_file()
  {
    Write("c.json", SetupCaseTests.Cases(""));
    SetupCases.Run(Row("cases"), Options());
    string committed = Directory
      .GetFiles(Recordings, "cases.json", SearchOption.AllDirectories)
      .Single();
    File.WriteAllText(
      committed,
      File.ReadAllText(committed).Replace("idle", "busy")
    );

    Assert.ThrowsAny<Exception>(() => SetupCases.Run(Row("cases"), Options()));

    Assert.True(
      File.Exists(
        Path.Combine(Path.GetDirectoryName(committed)!, "cases.received.json")
      )
    );
  }

  // Fails when a variant without a recording of its own writes one.
  [Fact]
  public void A_variant_with_fails_passes_and_records_nothing()
  {
    Write(
      "c.json",
      SetupCaseTests.Cases(
        "\"low\": {" + Late + ", \"fails\": \"hold\"}",
        Pair()
      )
    );

    SetupCases.Run(Row("cases/low"), Options());

    Assert.False(Directory.Exists(Recordings));
  }

  // Fails when a failing check does not fail the row, or its message does not name the case.
  [Fact]
  public void A_failing_check_fails_the_row_naming_the_case_and_the_check()
  {
    Write(
      "c.json",
      SetupCaseTests
        .Cases("\"low\": {}", Pair())
        .Replace("\"equals\": 0}", "\"equals\": 9}")
    );

    InvalidOperationException e = Assert.Throws<InvalidOperationException>(() =>
      SetupCases.Run(Row("cases/low"), Options())
    );

    Assert.StartsWith("cases/low: check zero (zero says) fails", e.Message);
  }

  private sealed class SlabPlacer : ISetupPlacer
  {
    public bool Takes(string code) => code == "setupbox:slab";

    public string? StandIn(string code) => "a plain slab";

    public void Place(SetupScene scene, Block block, BlockPos pos) =>
      scene.World.Accessor.SetBlock(block.BlockId, pos);
  }

  // Fails when the stand-ins a case's placers print are dropped.
  [Fact]
  public void The_stand_ins_the_case_prints_reach_the_output()
  {
    SetupPlacers.Register(new SlabPlacer());
    Write(
      "c.json",
      SetupCaseTests.Cases(
        "",
        steps: "[{\"place\": {\"id\": \"a\", \"code\": \"setupbox:box\", \"at\": [0, 0, 0]}}, "
          + "{\"set\": {\"at\": [0, 0, 0], \"member\": {\"Level\": 5}}}, "
          + "{\"place\": {\"id\": \"b\", \"code\": \"setupbox:box\", \"at\": [1, 0, 0]}}, "
          + "{\"place\": {\"code\": \"setupbox:slab\", \"at\": [2, 0, 0]}}]"
      )
    );
    var lines = new List<string>();

    SetupCases.Run(Row("cases"), Options(lines.Add));

    Assert.Equal(["stand-in setupbox:slab: a plain slab"], lines);
  }

  // Fails when a tick-order row ignores its order, or its failure does not name the order.
  [Fact]
  public void A_check_that_needs_one_tick_order_fails_in_the_orders_that_break_it_naming_the_order()
  {
    Write("t.json", Ticking(InStep + "}"));

    SetupCases.Run(Row("ticks"), Options());
    SetupCases.Run(Row("ticks@b,a,network"), Options());
    InvalidOperationException e = Assert.Throws<InvalidOperationException>(() =>
      SetupCases.Run(Row("ticks@a,b,network"), Options())
    );

    Assert.StartsWith(
      "ticks@a,b,network: check sync (a keeps level with b) fails",
      e.Message
    );
  }

  // Fails when a check marked for the drawn order only is judged in the other orders.
  [Fact]
  public void A_check_for_the_drawn_order_only_is_not_judged_in_the_other_orders()
  {
    Write("t.json", Ticking(InStep + ", \"orders\": \"drawn\"}"));

    SetupCases.Run(Row("ticks"), Options());
    SetupCases.Run(Row("ticks@a,b,network"), Options());
  }

  // Fails when a tick-order row writes a recording.
  [Fact]
  public void A_tick_order_row_records_nothing()
  {
    Write("t.json", Ticking(InStep + "}"));

    SetupCases.Run(Row("ticks@b,a,network"), Options());

    Assert.False(Directory.Exists(Recordings));
  }

  #endregion
}
#endif
