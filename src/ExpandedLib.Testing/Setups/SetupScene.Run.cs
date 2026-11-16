#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using ExpandedLib.Scenarios;

namespace ExpandedLib.Testing;

public sealed partial class SetupScene
{
  internal const string DefaultWriteVariable = "EX_WRITE_SETUPS";
  internal const string DefaultGateVariable = "EX_GATE_RECORDINGS";
  internal const string DefaultScenesVariable = "EX_SETUP_SCENES";
  internal const string DefaultOpeningsCensusVariable = "EXLIB_OPENINGS_CENSUS";

  /// <summary>Runs the file's <c>seconds</c>: each second its due <c>during</c> actions, then the
  /// rest of its ticks, then the <c>record</c> sample of that second.</summary>
  /// <remarks>A <c>wait</c> met inside a second leaves that second running; its ticks then run only
  /// what is left of it, and none when the wait ran it to its end.</remarks>
  /// <param name="mods">The header's <c>mods</c> map, in order.</param>
  /// <param name="game">The game series that runs the case, as the recording names it; null for
  /// the file's <c>game</c>.</param>
  internal SetupRecording Run(
    IReadOnlyList<(string Mod, string Version)> mods,
    string? game = null
  )
  {
    var run = new SetupRun(Model, View, game ?? Model.Game, mods);
    run.Start();
    for (int second = 0; second < Model.Seconds; second++)
    {
      _waitClosedSecond = false;
      foreach (SetupDuring action in run.Due(second))
        RunSteps(action.Steps);
      JoinLines();
      run.Before(second);
      FinishSecond();
      GiveSpawns();
      run.Sample(second);
    }
    run.Recording.Pack = CheckPack();
    return run.Recording;
  }

  /// <summary>Whether the case writes and compares a recording of its own: the main case, and a
  /// variant that names one in <c>record</c>.</summary>
  internal bool RecordsOwn => Model.RecordsOwn;

  /// <summary>Writes the recording into <paramref name="folder"/> when the file is absent or
  /// <paramref name="writeVariable"/> names it, else compares it with the committed file. A
  /// difference writes the received and moved files and throws only when
  /// <paramref name="gateVariable"/> names the case.</summary>
  /// <param name="recording">What <see cref="Run"/> recorded.</param>
  /// <param name="folder">The folder holding the committed file.</param>
  /// <param name="writeVariable">The environment variable that is <c>1</c> or names the setups to write.</param>
  /// <param name="gateVariable">The environment variable that is <c>1</c> or names the setups whose difference fails.</param>
  /// <returns>The recording's text.</returns>
  /// <exception cref="SetupRecordingMismatchException">The recording differs from the committed file and the gate names the case.</exception>
  internal string Record(
    SetupRecording recording,
    string folder,
    string writeVariable = DefaultWriteVariable,
    string gateVariable = DefaultGateVariable
  ) =>
    recording.Save(
      folder,
      SetupRecording.Writes(
        Environment.GetEnvironmentVariable(writeVariable),
        Model.RecordName
      ),
      gate: SetupRecording.Writes(
        Environment.GetEnvironmentVariable(gateVariable),
        Model.RecordName
      )
    );

  /// <summary>Runs the case's checks in file order over <paramref name="recording"/>.</summary>
  /// <param name="recording">What <see cref="Run"/> recorded.</param>
  /// <param name="ordersOnly">Judges only the checks that hold in every tick order.</param>
  /// <returns>Null when the case passes: no check fails, or for a variant with <c>fails</c> the
  /// first failing check is that one. Else the message naming the check, its <c>says</c>, the
  /// second and the readings.</returns>
  internal string? Judge(SetupRecording recording, bool ordersOnly = false) =>
    new SetupRun(Model, recording).Judge(ordersOnly);
}
#endif
