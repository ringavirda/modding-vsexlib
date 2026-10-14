#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Scenarios;

namespace ExpandedLib.Testing;

public sealed partial class SetupScene
{
  internal const string DefaultWriteVariable = "EX_WRITE_SETUPS";
  internal const string DefaultScenesVariable = "EX_SETUP_SCENES";

  /// <summary>Runs the file's <c>seconds</c>: each second its due <c>during</c> actions, then its
  /// ticks, then the <c>record</c> sample of that second.</summary>
  /// <param name="mods">The header's <c>mods</c> map, in order.</param>
  /// <param name="game">The game series that runs the case, as the recording names it; null for
  /// the file's <c>game</c>.</param>
  internal SetupRecording Run(
    IReadOnlyList<(string Mod, string Version)> mods,
    string? game = null
  )
  {
    SetupRecording recording = SetupRecording.ForFile(
      Model,
      View,
      game ?? Model.Game,
      mods
    );
    var reading = new ReadingContext(View, recording);
    recording.Start(reading);
    SampledReading[] sampled = [.. Model.Checks.SelectMany(c => c.Sampled)];
    foreach (SampledReading kept in sampled)
      kept.Start(reading);
    for (int second = 0; second < Model.Seconds; second++)
    {
      foreach (SetupDuring action in Model.During)
        if (Due(action, second, reading))
          RunSteps(action.Steps);
      JoinLines();
      recording.Before(second, reading);
      TickSecond();
      recording.Sample(second, reading);
      foreach (SampledReading kept in sampled)
        kept.Sample(reading);
    }
    recording.Pack = CheckPack();
    return recording;
  }

  /// <summary>Whether the case writes and compares a recording of its own: the main case, and a
  /// variant that names one in <c>record</c>.</summary>
  internal bool RecordsOwn => Model.RecordsOwn;

  /// <summary>Writes the recording into <paramref name="folder"/> when the file is absent or
  /// <paramref name="writeVariable"/> names it, else compares it with the committed file.</summary>
  /// <param name="recording">What <see cref="Run"/> recorded.</param>
  /// <param name="folder">The folder holding the committed file.</param>
  /// <param name="writeVariable">The environment variable that is <c>1</c> or names the setups to write.</param>
  /// <exception cref="SetupRecordingMismatchException">The recording differs from the committed file.</exception>
  internal string Record(
    SetupRecording recording,
    string folder,
    string writeVariable = DefaultWriteVariable
  ) =>
    recording.Save(
      folder,
      SetupRecording.Writes(
        Environment.GetEnvironmentVariable(writeVariable),
        Model.RecordName
      )
    );

  /// <summary>Runs the case's checks in file order over <paramref name="recording"/>.</summary>
  /// <param name="recording">What <see cref="Run"/> recorded.</param>
  /// <param name="ordersOnly">Judges only the checks that hold in every tick order.</param>
  /// <returns>Null when the case passes: no check fails, or for a variant with <c>fails</c> the
  /// first failing check is that one. Else the message naming the check, its <c>says</c>, the
  /// second and the readings.</returns>
  internal string? Judge(SetupRecording recording, bool ordersOnly = false)
  {
    SetupCheckFailure? failure = SetupCheck.First(
      Model.Checks.Where(c => !ordersOnly || c.AllOrders),
      recording
    );
    if (Model.Fails is not { } expected)
      return failure?.ToString();
    if (failure == null)
      return $"check {expected} should fail first, and no check fails";
    return failure.Id == expected
      ? null
      : $"check {expected} should fail first, but {failure}";
  }

  private bool Due(SetupDuring action, int second, ReadingContext reading)
  {
    if (action.At is { } at)
      return at == second;
    if (second < action.From || (second - action.From) % action.Period != 0)
      return false;
    return action.When.All(t => Holds(t, reading, "during"));
  }
}
#endif
