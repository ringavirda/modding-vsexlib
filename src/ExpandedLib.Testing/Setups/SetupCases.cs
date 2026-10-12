#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Scenarios;
using Vintagestory.API.Config;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace ExpandedLib.Testing;

/// <summary>
/// One harness case of a setup file, as an xunit row: the main case, a variant, or either in one
/// tick order. <see cref="SetupCases.Run"/> runs it.
/// </summary>
public sealed class SetupCaseRow : IXunitSerializable
{
  /// <summary>Reads a row back from xunit's discovery cache; use <see cref="SetupCases.In"/>.</summary>
  public SetupCaseRow() { }

  internal SetupCaseRow(
    string file,
    string setup,
    string? variant,
    string? order,
    string? skip
  )
  {
    File = file;
    Setup = setup;
    Variant = variant;
    Order = order;
    Skip = skip;
  }

  /// <summary>The path of the setup file.</summary>
  public string File { get; private set; } = "";

  /// <summary>The file's <c>setup</c> id.</summary>
  public string Setup { get; private set; } = "";

  /// <summary>The variant the row runs, or null for the main case.</summary>
  public string? Variant { get; private set; }

  /// <summary>The tick order the row runs, its ids and <c>network</c> joined by commas, or null
  /// for the build's order.</summary>
  public string? Order { get; private set; }

  /// <summary>Why the row is skipped, or null when it runs.</summary>
  public string? Skip { get; private set; }

  /// <summary>The case's name: <c>&lt;setup&gt;</c>, then <c>/&lt;variant&gt;</c> and
  /// <c>@&lt;order&gt;</c> when it has them.</summary>
  public override string ToString() =>
    Setup
    + (Variant == null ? "" : "/" + Variant)
    + (Order == null ? "" : "@" + Order);

  /// <inheritdoc/>
  public void Serialize(IXunitSerializationInfo info)
  {
    info.AddValue("file", File);
    info.AddValue("setup", Setup);
    info.AddValue("variant", Variant);
    info.AddValue("order", Order);
    info.AddValue("skip", Skip);
  }

  /// <inheritdoc/>
  public void Deserialize(IXunitSerializationInfo info)
  {
    File = info.GetValue<string>("file");
    Setup = info.GetValue<string>("setup");
    Variant = info.GetValue<string?>("variant");
    Order = info.GetValue<string?>("order");
    Skip = info.GetValue<string?>("skip");
  }
}

/// <summary>What <see cref="SetupCases.Run"/> loads a case with and where it writes its recording.</summary>
public sealed class SetupRunOptions
{
  /// <summary>The folder of the mod with the given id, holding its <c>modinfo.json</c> or a
  /// <c>src</c> folder that does.</summary>
  public required Func<string, string> ModFolder { get; init; }

  /// <summary>The <c>Recordings</c> folder; a recording goes under the game series that ran it,
  /// <c>Recordings/1.22/&lt;setup&gt;.json</c>.</summary>
  public required string Recordings { get; init; }

  /// <summary>The exlib version a recording names when no loaded mod's <c>modinfo.json</c> requires one.</summary>
  public required string ExlibVersion { get; init; }

  /// <summary>Runs after the mods load and before the file's <c>config</c> applies, as
  /// <see cref="SetupScene.Build"/> takes it.</summary>
  public Action<TestWorld>? AfterLoad { get; init; }

  /// <summary>Runs before the mods load, as <see cref="SetupScene.Build"/> takes it.</summary>
  public Action<TestWorld>? BeforeLoad { get; init; }

  /// <summary>The environment variable that is <c>1</c> or names the setups to write; default
  /// <c>EX_WRITE_SETUPS</c>.</summary>
  public string WriteVariable { get; init; } = SetupScene.DefaultWriteVariable;

  /// <summary>The environment variable that names the folder <see cref="SetupCases.Run"/> writes
  /// each row's scene dump to, as <c>&lt;setup&gt;.scene.json</c> or <c>&lt;setup&gt;@&lt;variant&gt;.scene.json</c>;
  /// unset or empty writes none. Default <c>EX_SETUP_SCENES</c>.</summary>
  public string ScenesVariable { get; init; } =
    SetupScene.DefaultScenesVariable;

  /// <summary>Receives each <c>stand-in</c> line the case's placers print, or null to drop them.</summary>
  public Action<string>? Output { get; init; }
}

/// <summary>
/// The harness cases of the setup files under a folder, as xunit rows for a
/// <see cref="SetupTheoryAttribute"/> test (setups.md, Where it lives).
/// </summary>
public static class SetupCases
{
  /// <summary>Every case of every <c>*.json</c> file directly in <paramref name="folder"/>, in file
  /// name order: the main case, each variant, then, when the file names <c>tickOrders</c>, the main
  /// case and each variant that sets <c>tickOrders</c> in each tick order. A row for a file whose
  /// <c>game</c> is above the running game carries the reason in <see cref="SetupCaseRow.Skip"/>.</summary>
  /// <param name="folder">A folder of setup files; recordings in a folder below it are not read.</param>
  /// <returns>One argument array per row, holding the <see cref="SetupCaseRow"/>.</returns>
  /// <exception cref="InvalidOperationException">A file is malformed or breaks a rule of setups.md.</exception>
  /// <exception cref="DirectoryNotFoundException"><paramref name="folder"/> does not exist.</exception>
  public static IEnumerable<object[]> In(string folder)
  {
    foreach (
      string path in Directory
        .GetFiles(folder, "*.json")
        .OrderBy(p => p, StringComparer.Ordinal)
    )
    {
      SetupFile file = SetupFile.Load(path);
      string? skip = GameVersion.IsAtLeastVersion(
        GameVersion.ShortGameVersion,
        file.Game
      )
        ? null
        : $"{file.Name} needs game {file.Game}, this is {GameVersion.ShortGameVersion}.";
      SetupCaseRow Row(string? variant, string[]? order = null) =>
        new(
          path,
          file.Setup,
          variant,
          order == null ? null : SetupTickOrders.Name(order),
          skip
        );
      yield return [Row(null)];
      foreach (string variant in file.VariantNames)
        yield return [Row(variant)];
      if (file.TickOrderIds.Count == 0)
        continue;
      IReadOnlyList<string[]> orders = SetupTickOrders.Of(file.TickOrderIds);
      foreach (string[] order in orders)
        yield return [Row(null, order)];
      foreach (string variant in file.OrderedVariantNames)
      foreach (string[] order in orders)
        yield return [Row(variant, order)];
    }
  }

  /// <summary>The mod's <c>modinfo.json</c>, at the folder root or under <c>src</c>.</summary>
  private static string ModInfoPath(string modFolder)
  {
    string root = Path.Combine(modFolder, "modinfo.json");
    return System.IO.File.Exists(root)
      ? root
      : Path.Combine(modFolder, "src", "modinfo.json");
  }

  /// <summary>Builds and runs the case <paramref name="row"/> names, judges its checks, and for a
  /// case that records writes or compares its recording (<c>EX_WRITE_SETUPS</c> names the setups to
  /// write). A case with no tick order also writes its built scene to <c>&lt;setup&gt;[@&lt;variant&gt;].scene.json</c> in the
  /// folder named by the environment variable <see cref="SetupRunOptions.ScenesVariable"/>, when that is set. A tick-order case judges
  /// only the checks that hold in every order and records nothing.</summary>
  /// <param name="row">A row from <see cref="In"/>.</param>
  /// <param name="options">The mod folders, recordings folder and hooks the case runs with.</param>
  /// <exception cref="InvalidOperationException">A check fails, the message naming the case (so an
  /// order case names its order) and the check; or the file cannot be built; or a tick-order
  /// machine registered no listener.</exception>
  /// <exception cref="SetupRecordingMismatchException">The recording differs from the committed file.</exception>
  /// <exception cref="IOException">The scenes folder cannot be created or the scene file written.</exception>
  /// <exception cref="UnauthorizedAccessException">The scenes folder or file is not writable.</exception>
  public static void Run(SetupCaseRow row, SetupRunOptions options)
  {
    ArgumentNullException.ThrowIfNull(row);
    ArgumentNullException.ThrowIfNull(options);
    SetupFile file = SetupFile.Load(row.File);
    IReadOnlyList<(string Mod, string Version)> mods = SetupMods.Map(
      file.Mods,
      id =>
        SetupMods.FromModInfo(
          System.IO.File.ReadAllText(ModInfoPath(options.ModFolder(id)))
        ),
      options.ExlibVersion
    );
    using SetupScene scene = SetupScene.Build(
      row.Variant == null ? file : file.ForVariant(row.Variant),
      [.. file.Mods.Select(options.ModFolder)],
      options.AfterLoad,
      options.BeforeLoad
    );
    foreach (string line in scene.StandIns)
      options.Output?.Invoke(line);
    if (
      row.Order == null
      && Environment.GetEnvironmentVariable(options.ScenesVariable)
        is { Length: > 0 } scenes
    )
    {
      Directory.CreateDirectory(scenes);
      File.WriteAllText(
        Path.Combine(scenes, SceneFile(row)),
        SetupSceneDump.Of(scene, row.Setup, row.Variant)
      );
    }
    if (row.Order != null)
      scene.Order(row.Order.Split(','));
    SetupRecording recording = scene.Run(mods, Series());
    if (scene.Judge(recording, row.Order != null) is { } verdict)
      throw new InvalidOperationException($"{row}: {verdict}");
    if (row.Order == null && scene.RecordsOwn)
      scene.Record(
        recording,
        Path.Combine(options.Recordings, Series()),
        options.WriteVariable
      );
  }

  private static string SceneFile(SetupCaseRow row) =>
    row.Setup + (row.Variant == null ? "" : "@" + row.Variant) + ".scene.json";

  private static string Series() =>
    string.Join('.', GameVersion.ShortGameVersion.Split('.').Take(2));
}

/// <summary>Marks a test method that takes a <see cref="SetupCaseRow"/> from
/// <c>[MemberData]</c> over <see cref="SetupCases.In"/>; a row with a skip reason is reported
/// skipped.</summary>
[XunitTestCaseDiscoverer(
  "ExpandedLib.Testing.SetupTheoryDiscoverer",
  "ExpandedLib.Testing"
)]
public sealed class SetupTheoryAttribute : Xunit.TheoryAttribute { }

/// <summary>Finds the rows of a <see cref="SetupTheoryAttribute"/> test and skips those whose
/// <see cref="SetupCaseRow.Skip"/> names a reason.</summary>
/// <param name="diagnosticMessageSink">Receives xunit's diagnostics.</param>
public sealed class SetupTheoryDiscoverer(IMessageSink diagnosticMessageSink)
  : Xunit.Sdk.TheoryDiscoverer(diagnosticMessageSink)
{
  /// <inheritdoc/>
  protected override IEnumerable<IXunitTestCase> CreateTestCasesForDataRow(
    ITestFrameworkDiscoveryOptions discoveryOptions,
    ITestMethod testMethod,
    IAttributeInfo theoryAttribute,
    object[] dataRow
  ) =>
    dataRow.OfType<SetupCaseRow>().FirstOrDefault()?.Skip is { } reason
      ?
      [
        new XunitSkippedDataRowTestCase(
          DiagnosticMessageSink,
          discoveryOptions.MethodDisplayOrDefault(),
          discoveryOptions.MethodDisplayOptionsOrDefault(),
          testMethod,
          reason,
          dataRow
        ),
      ]
      : base.CreateTestCasesForDataRow(
        discoveryOptions,
        testMethod,
        theoryAttribute,
        dataRow
      );
}
#endif
