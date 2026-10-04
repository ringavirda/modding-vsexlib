using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Scenarios;

/// <summary>How a setup is recorded: change lists, steady figures, the file written beside the
/// committed one in a scratch folder, and a committed recording read back.</summary>
public class SetupRecordingTests : IDisposable {
  private const string Name = "setup";
  private const int Seconds = 12;

  private readonly string _folder = Path.Combine(
    Path.GetTempPath(),
    "exlib-setup-" + Guid.NewGuid().ToString("N")
  );

  private string Committed => Path.Combine(_folder, Name + ".json");

  private string Received => Path.Combine(_folder, Name + ".received.json");

  public void Dispose() {
    if (Directory.Exists(_folder))
      Directory.Delete(_folder, recursive: true);
  }

  #region Fixtures

  /// <summary>Reads the scripted value of the context's second.</summary>
  private sealed class Script(object[] values) : Reading {
    public override object Read(ReadingContext ctx) => values[ctx.Second];
  }

  /// <summary>Reads whatever the test last set.</summary>
  private sealed class Counter : Reading {
    public object Value { get; set; } = 0f;

    public override object Read(ReadingContext ctx) => Value;
  }

  private sealed class PipeState {
    public string MediumType { get; set; } = "Steam";

    public float FlowRate { get; set; } = 1.234f;

    public float Pressure { get; set; } = 2f;

    public float Temperature { get; set; } = 150f;

    public float Volume { get; set; } = 3f;
  }

  private static SetupRecording Empty(int seconds = Seconds) =>
    new(Name, "1.22", [("exlib", "0.9.0"), ("iiex", "0.1.0")], seconds);

  /// <summary>A finished recording whose machine <c>m</c> records <c>v</c> in atm and the
  /// state word of each second, "on" when none is given.</summary>
  private static SetupRecording Scripted(float[] values, string[]? words = null) {
    SetupRecording recording = Empty();
    recording
      .Machine("m", "test:m-north")
      .State(new Script([.. words ?? Enumerable.Repeat("on", Seconds)]))
      .Value("v", "atm", new Script([.. values.Cast<object>()]));
    return Finished(recording, new ReadingContext(null));
  }

  private static SetupRecording Finished(SetupRecording recording, ReadingContext ctx) {
    recording.Start(ctx);
    for (int t = 0; t < recording.Seconds; t++)
      recording.Sample(t, ctx);
    return recording;
  }

  private static float[] Repeat(float value) => [.. Enumerable.Repeat(value, Seconds)];

  private static JsonNode Parse(string text) => JsonNode.Parse(text)!;

  private static string Fixture =>
    Path.Combine(
      RepoPaths.Root,
      "tests",
      "ExpandedLib.Tests",
      "Scenarios",
      "Fixtures",
      "starter-steam-power.json"
    );

  #endregion

  #region Series

  // Fails when a second is written although its rounded value equals the last one's, or when a
  // change is judged on the unrounded value.
  [Fact]
  public void A_series_writes_a_second_only_when_its_rounded_value_changes() {
    float[] values = Repeat(1f);
    values[1] = 1.0004f;
    values[5] = 2f;
    values[6] = 2f;

    string text = Scripted(values).Text();

    Assert.Equal(
      "[[0,1.000],[5,2.000],[7,1.000]]",
      Parse(text)["machines"]!["m"]!["series"]!["v"]!.ToJsonString()
    );
  }

  // Fails when the steady number is the last sample, the mean of the whole run, or an
  // unweighted mean of the change list's values.
  [Fact]
  public void The_steady_number_is_the_time_weighted_mean_of_the_last_third() {
    float[] values = Repeat(9f);
    values[8] = 1f;
    values[9] = 1f;
    values[10] = 1f;
    values[11] = 5f;

    Assert.Equal(2d, (double)Scripted(values).Steady()["m.v"], 6);
  }

  // Fails when the steady word is the last one, the first, or the one with most changes.
  [Fact]
  public void The_steady_word_is_the_one_held_longest_in_the_window() {
    string[] words = [.. Enumerable.Repeat("idle", 8), "run", "run", "run", "idle"];

    Assert.Equal("run", Scripted(Repeat(1f), words).Steady()["m.state"]);
  }

  // Fails when a tie in held time goes to the later word.
  [Fact]
  public void A_tied_steady_word_goes_to_the_one_held_first() {
    string[] words = [.. Enumerable.Repeat("idle", 8), "run", "run", "idle", "idle"];

    Assert.Equal("run", Scripted(Repeat(1f), words).Steady()["m.state"]);
  }

  #endregion

  #region Steadiness

  // Fails when steadiness is judged on the whole run instead of the two halves of the window.
  [Fact]
  public void A_number_still_climbing_in_the_window_is_unsteady() {
    float[] values = Repeat(10f);
    values[10] = 11f;
    values[11] = 11f;

    Assert.Contains("m.v", Scripted(values).Unsteady());
  }

  // Fails when the tolerance is under 5% of the larger half mean, or over it.
  [Theory]
  [InlineData(100f, 104f, true)]
  [InlineData(100f, 106f, false)]
  public void Halves_of_a_large_number_differ_by_at_most_five_percent(
    float first,
    float second,
    bool steady
  ) {
    float[] values = Repeat(first);
    for (int t = 10; t < Seconds; t++)
      values[t] = second;

    Assert.Equal(steady, !Scripted(values).Unsteady().Contains("m.v"));
  }

  // Fails when a number below 1 is judged by 5% of itself instead of by 0.05.
  [Theory]
  [InlineData(0.5f, 0.54f, true)]
  [InlineData(0.5f, 0.56f, false)]
  public void Halves_of_a_small_number_differ_by_at_most_five_hundredths(
    float first,
    float second,
    bool steady
  ) {
    float[] values = Repeat(first);
    for (int t = 10; t < Seconds; t++)
      values[t] = second;

    Assert.Equal(steady, !Scripted(values).Unsteady().Contains("m.v"));
  }

  // Fails when a word is judged steady although the halves hold different words longest.
  [Fact]
  public void A_word_that_changes_between_the_halves_is_unsteady() {
    string[] words = [.. Enumerable.Repeat("idle", 10), "run", "run"];

    Assert.Contains("m.state", Scripted(Repeat(1f), words).Unsteady());
  }

  // Fails when a flat recording reports an unsteady series.
  [Fact]
  public void A_flat_recording_is_steady() {
    Assert.Empty(Scripted(Repeat(3f)).Unsteady());
  }

  #endregion

  #region Sampling

  // Fails when Sample accepts a second out of order.
  [Fact]
  public void Sampling_out_of_order_throws() {
    SetupRecording recording = Empty();
    recording.Machine("m", "test:m-north").State(new Counter());
    var ctx = new ReadingContext(null);
    recording.Start(ctx);

    Assert.Throws<InvalidOperationException>(() => recording.Sample(1, ctx));
  }

  // Fails when Sample accepts a second past the recording's length.
  [Fact]
  public void Sampling_past_the_end_throws() {
    SetupRecording recording = Scripted(Repeat(1f));

    Assert.Throws<InvalidOperationException>(() =>
      recording.Sample(Seconds, new ReadingContext(null))
    );
  }

  // Fails when Sample runs before Start has read the baselines.
  [Fact]
  public void Sampling_before_start_throws() {
    SetupRecording recording = Empty();
    recording.Machine("m", "test:m-north").State(new Counter());

    Assert.Throws<InvalidOperationException>(() =>
      recording.Sample(0, new ReadingContext(null))
    );
  }

  // Fails when the steady figures are read before every second is sampled.
  [Fact]
  public void An_incomplete_recording_has_no_steady_figures() {
    SetupRecording recording = Empty();
    recording.Machine("m", "test:m-north").State(new Counter());
    var ctx = new ReadingContext(null);
    recording.Start(ctx);
    recording.Sample(0, ctx);

    Assert.Throws<InvalidOperationException>(() => recording.Steady());
  }

  // Fails when a machine with no state is recorded with an empty one.
  [Fact]
  public void A_machine_with_no_state_throws_when_sampled() {
    SetupRecording recording = Empty();
    recording.Machine("m", "test:m-north");
    var ctx = new ReadingContext(null);
    recording.Start(ctx);

    Assert.Throws<InvalidOperationException>(() => recording.Sample(0, ctx));
  }

  // Fails when a constant is read after the build changes it, or at a sample.
  [Fact]
  public void A_constant_is_read_once_after_the_build() {
    SetupRecording recording = Empty();
    var gate = new Counter { Value = 3.5f };
    recording.Machine("k", "test:k").State(new Counter()).Constant("gate", "atm", gate);
    var ctx = new ReadingContext(null);
    recording.Start(ctx);
    gate.Value = 9f;
    for (int t = 0; t < Seconds; t++)
      recording.Sample(t, ctx);

    Assert.Equal(
      "{\"gate\":[3.500,\"atm\"]}",
      Parse(recording.Text())["machines"]!["k"]!["constants"]!.ToJsonString()
    );
  }

  #endregion

  #region Runs

  // Fails when a run reads another network or another field of its state than the one it
  // names, or an empty cell records anything but an empty medium and zeros.
  [Fact]
  public void A_run_records_its_own_network_and_an_empty_one_records_zeros() {
    var world = new SetupTestWorld();
    world.Test.RegisterNetwork("pipe", s => new StubNetwork(s, "pipe"));
    world.Test.PlaceNode(new BlockPos(0, 0, 0), "pipe", "ns");
    world.Test.PlaceNode(new BlockPos(5, 0, 0), "pipe", "ns");
    world.Test.NetworkAt(new BlockPos(0, 0, 0))!.RestoreState(new PipeState());
    SetupRecording recording = Empty();
    recording.Run("fed", new BlockPos(0, 0, 0));
    recording.Run("dry", new BlockPos(5, 0, 0));

    var runs = Parse(Finished(recording, new ReadingContext(world)).Text())["runs"]!;

    Assert.Equal(
      "{\"medium\":\"Steam\",\"flow\":1.23,\"pressure\":2.000,\"temperature\":150.0,\"volume\":3.00}",
      runs["fed"]!["steady"]!.ToJsonString()
    );
    Assert.Equal(
      "{\"medium\":\"\",\"flow\":0.00,\"pressure\":0.000,\"temperature\":0.0,\"volume\":0.00}",
      runs["dry"]!["steady"]!.ToJsonString()
    );
    Assert.Equal("[5,0,0]", runs["dry"]!["at"]!.ToJsonString());
  }

  // Fails when a run whose cell is on no network of its type is recorded as empty.
  [Fact]
  public void A_run_on_no_network_fails_at_start() {
    SetupRecording recording = Empty();
    recording.Run("fed", new BlockPos(0, 0, 0));

    Assert.Throws<SetupReadingException>(() =>
      recording.Start(new ReadingContext(new SetupTestWorld()))
    );
  }

  // Fails when a molten run's flow is not the rise of entered from its value after the build,
  // or its medium, temperature and volume are read off other cells.
  [Fact]
  public void A_molten_run_records_the_rise_of_entered_and_its_standing_metal() {
    var world = new SetupTestWorld();
    world.Test.RegisterNetwork("molten", s => new StubNetwork(s, "molten"));
    world.Test.PlaceNode(new BlockPos(0, 0, 0), "molten", "ns");
    world.Test.PlaceNode(new BlockPos(0, 0, 1), "molten", "ns");
    world.Molten(new BlockPos(0, 0, 0), new MoltenCellReading(0, "", 0f));
    world.Molten(new BlockPos(0, 0, 1), new MoltenCellReading(40, "game:ingot-iron", 1450f));
    var entered = new Counter { Value = 10f };
    SetupRecording recording = new(Name, "1.22", [("exlib", "0.9.0")], 6);
    recording.MoltenRun("canal", new BlockPos(0, 0, 0), entered);
    var ctx = new ReadingContext(world);
    recording.Start(ctx);
    foreach (float now in new[] { 12f, 15f, 3f, 3f, 3f, 3f }) {
      entered.Value = now;
      recording.Sample(recording.Samples, ctx);
    }

    var canal = Parse(recording.Text())["runs"]!["canal"]!;

    Assert.Equal("[[0,2.00],[1,3.00],[3,0.00]]", canal["series"]!["flow"]!.ToJsonString());
    Assert.Equal("[[0,\"molten-iron\"]]", canal["series"]!["medium"]!.ToJsonString());
    Assert.Equal("[[0,1450.0]]", canal["series"]!["temperature"]!.ToJsonString());
    Assert.Equal("[[0,40.00]]", canal["series"]!["volume"]!.ToJsonString());
    Assert.Equal("u/s", canal["units"]!["flow"]!.GetValue<string>());
  }

  #endregion

  #region Save

  // Fails when the header names another schema, setup, game, mods order or window than given.
  [Fact]
  public void The_file_opens_with_its_schema_setup_game_mods_and_window() {
    string text = Scripted(Repeat(1f)).Text();

    Assert.StartsWith(
      "{\"schema\":1,\"setup\":\"setup\",\"game\":\"1.22\","
        + "\"mods\":{\"exlib\":\"0.9.0\",\"iiex\":\"0.1.0\"},\"seconds\":12,\"steady\":[8,12],\n",
      text
    );
  }

  // Fails when exlib's version is not the first naming mod's, or the mods lose their order.
  [Fact]
  public void The_mods_map_takes_exlib_from_the_first_mod_that_names_it() {
    SetupModVersions Versions(string mod) =>
      mod switch {
        "siex" => new("0.2.0", null),
        "iiex" => new("0.1.0", "0.9.1"),
        _ => new("0.3.0", "0.9.2"),
      };

    var map = SetupMods.Map(["siex", "iiex", "other"], Versions, "0.9.9");

    Assert.Equal(
      new[] { ("exlib", "0.9.1"), ("siex", "0.2.0"), ("iiex", "0.1.0"), ("other", "0.3.0") },
      map
    );
    Assert.Equal(("exlib", "0.9.9"), SetupMods.Map(["siex"], Versions, "0.9.9")[0]);
  }

  // Fails when a modinfo's version or exlib dependency is read case-sensitively or missed.
  [Fact]
  public void A_modinfo_names_its_version_and_the_exlib_it_requires() {
    Assert.Equal(
      new SetupModVersions("0.1.0", "0.9.1"),
      SetupMods.FromModInfo("""{ "Version": "0.1.0", "Dependencies": { "ExLib": "0.9.1" } }""")
    );
    Assert.Equal(new SetupModVersions("0.2.0", null), SetupMods.FromModInfo("""{ "version": "0.2.0" }"""));
  }

  // Fails when the same recording writes different bytes the second time.
  [Fact]
  public void The_same_recording_writes_the_same_text() {
    string first = Scripted(Repeat(1f)).Save(_folder, write: false);

    string second = Scripted(Repeat(1f)).Save(_folder, write: false);

    Assert.Equal(first, second);
    Assert.Equal(first, File.ReadAllText(Committed));
  }

  // Fails when an absent recording is not written as the committed file.
  [Fact]
  public void A_recording_with_no_committed_file_is_written_as_the_committed_file() {
    string text = Scripted(Repeat(1f)).Save(_folder, write: false);

    Assert.Equal(text, File.ReadAllText(Committed));
    Assert.False(File.Exists(Received));
  }

  // Fails when a differing steady figure passes, leaves no received file, or overwrites the
  // committed one.
  [Fact]
  public void Differing_steady_figures_write_a_received_file_and_fail() {
    string committed = Scripted(Repeat(1f)).Save(_folder, write: false);

    Assert.Throws<SetupRecordingMismatchException>(() =>
      Scripted(Repeat(2f)).Save(_folder, write: false)
    );

    Assert.Equal(committed, File.ReadAllText(Committed));
    Assert.Contains("2.000", File.ReadAllText(Received));
  }

  // Fails when a differing recording named for writing does not replace the committed file.
  [Fact]
  public void A_differing_recording_named_for_writing_replaces_the_committed_file() {
    Scripted(Repeat(1f)).Save(_folder, write: false);

    string text = Scripted(Repeat(2f)).Save(_folder, write: true);

    Assert.Equal(text, File.ReadAllText(Committed));
    Assert.False(File.Exists(Received));
  }

  // Fails when a matching recording leaves a stale received file.
  [Fact]
  public void A_matching_recording_deletes_a_stale_received_file() {
    Scripted(Repeat(1f)).Save(_folder, write: false);
    Assert.Throws<SetupRecordingMismatchException>(() =>
      Scripted(Repeat(2f)).Save(_folder, write: false)
    );

    Scripted(Repeat(1f)).Save(_folder, write: false);

    Assert.False(File.Exists(Received));
  }

  // Fails when the comparison reads only the steady figures and constants: a changed start-up
  // leaves both alone.
  [Fact]
  public void A_changed_start_with_the_same_steady_figures_fails() {
    string committed = Scripted(Repeat(1f)).Save(_folder, write: false);
    float[] values = Repeat(1f);
    values[2] = 7f;

    Assert.Throws<SetupRecordingMismatchException>(() =>
      Scripted(values).Save(_folder, write: false)
    );

    Assert.Equal(committed, File.ReadAllText(Committed));
    Assert.Contains("[2,7.000]", File.ReadAllText(Received));
  }

  // Fails when the comparison skips the header: a committed file naming an older iiex passes.
  [Fact]
  public void A_committed_file_with_a_stale_header_fails() {
    string text = Scripted(Repeat(1f)).Save(_folder, write: false);
    File.WriteAllText(Committed, text.Replace("\"iiex\":\"0.1.0\"", "\"iiex\":\"0.0.1\""));

    Assert.Throws<SetupRecordingMismatchException>(() =>
      Scripted(Repeat(1f)).Save(_folder, write: false)
    );

    Assert.Equal(text, File.ReadAllText(Received));
  }

  // Fails when a changed constant passes the comparison.
  [Fact]
  public void A_changed_constant_fails_the_comparison() {
    ConstantRecording(5f).Save(_folder, write: false);

    Assert.Throws<SetupRecordingMismatchException>(() =>
      ConstantRecording(6f).Save(_folder, write: false)
    );
  }

  private static SetupRecording ConstantRecording(float max) {
    SetupRecording recording = Empty();
    recording.Machine("k", "test:k").State(new Counter()).Constant("max", "atm", new Counter { Value = max });
    return Finished(recording, new ReadingContext(null));
  }

  // Fails when the write variable's value names another setup, or "1" does not name every one.
  [Theory]
  [InlineData("1", true)]
  [InlineData("starter-steam-power, setup", true)]
  [InlineData("starter-steam-power", false)]
  [InlineData(null, false)]
  public void The_write_variable_names_the_setups_it_rewrites(string? value, bool writes) {
    Assert.Equal(writes, SetupRecording.Writes(value, Name));
  }

  // Fails when the limit is doubled (the recording a kibibyte over it is written) or halved
  // (the one a kibibyte under it is refused).
  [Theory]
  [InlineData(1024, true)]
  [InlineData(-1024, false)]
  public void A_recording_over_a_mebibyte_is_refused(int beyond, bool refused) {
    SetupRecording recording = Empty();
    recording
      .Machine("m", "test:m-north")
      .State(new Counter { Value = new string('x', (SetupRecording.MaxBytes + beyond) / 2) });
    Finished(recording, new ReadingContext(null));

    if (refused)
      Assert.Throws<InvalidOperationException>(() => recording.Save(_folder, write: false));
    else
      recording.Save(_folder, write: false);

    Assert.Equal(!refused, File.Exists(Committed));
  }

  // Fails when a differing line is not named by its number and the text where it differs.
  [Fact]
  public void Differences_name_each_differing_line_at_its_first_differing_character() {
    Assert.Equal(
      new[] { "line 2: committed b1, recorded b2" },
      SetupRecording.Differences("a\nb1\nc", "a\nb2\nc")
    );
    Assert.Equal(new[] { "line 2: committed absent, recorded b" }, SetupRecording.Differences("a", "a\nb"));
  }

  #endregion

  #region Read back

  // Fails when reading a committed recording back loses or reorders a byte of what it writes:
  // a series, a steady figure, a constant's rounding, a unit or a line break.
  [Fact]
  public void The_starter_recording_reads_back_to_the_same_bytes() {
    string text = File.ReadAllText(Fixture);

    string again = SetupRecording.Parse(text).Text();

    Assert.Equal(text, again);
  }

  // Fails when a read-back recording answers another value than its change list holds.
  [Fact]
  public void A_read_back_recording_answers_its_series_and_steady_means() {
    SetupRecording recording = SetupRecording.Parse(File.ReadAllText(Fixture));

    Assert.Equal(3.59f, recording.ValueAt("steam", "flow", 181));
    Assert.Equal("Steam", recording.ValueAt("steam", "medium", 900));
    Assert.Equal(2.0d, recording.SteadyMean("steam-valve", "vent"), 6);
  }

  #endregion
}
