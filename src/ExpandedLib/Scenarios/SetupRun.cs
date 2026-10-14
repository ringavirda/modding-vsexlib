using System;
using System.Collections.Generic;
using System.Linq;

namespace ExpandedLib.Scenarios;

/// <summary>What one check came to over a recording: <paramref name="Index"/> its place in the
/// file, <paramref name="Failure"/> null when it holds or was skipped.</summary>
internal sealed record SetupVerdict(
  int Index,
  SetupCheck Check,
  SetupCheckFailure? Failure,
  bool Skipped
);

/// <summary>The seconds of a setup file's run, over any <see cref="ISetupWorld"/>: which
/// <c>during</c> actions are due, the recording and the checks' sampled readings, and the
/// verdict on the checks. The caller runs the actions' steps and the ticks.</summary>
internal sealed class SetupRun {
  private readonly SetupFileModel _model;
  private readonly ReadingContext _reading;
  private readonly SampledReading[] _sampled;

  /// <summary>A run of <paramref name="model"/> over <paramref name="world"/>.</summary>
  /// <param name="model">The validated file.</param>
  /// <param name="world">The world the run's readings are taken from.</param>
  /// <param name="game">The game series that runs the case, as the recording names it.</param>
  /// <param name="mods">The header's <c>mods</c> map, in order.</param>
  public SetupRun(
    SetupFileModel model,
    ISetupWorld world,
    string game,
    IReadOnlyList<(string Mod, string Version)> mods
  )
    : this(model, SetupRecording.ForFile(model, world, game, mods), world) { }

  /// <summary>A run that only judges a finished <paramref name="recording"/>: <see cref="Judge"/>
  /// and <see cref="Verdicts"/>.</summary>
  public SetupRun(SetupFileModel model, SetupRecording recording)
    : this(model, recording, null) { }

  private SetupRun(
    SetupFileModel model,
    SetupRecording recording,
    ISetupWorld? world
  ) {
    _model = model;
    Recording = recording;
    _reading = new ReadingContext(world, recording);
    _sampled = [.. model.Checks.SelectMany(c => c.Sampled)];
  }

  /// <summary>What the run records, second by second.</summary>
  public SetupRecording Recording { get; }

  /// <summary>Takes the readings that keep the finished build: the recording's and the checks'.</summary>
  public void Start() {
    Recording.Start(_reading);
    foreach (SampledReading kept in _sampled)
      kept.Start(_reading);
  }

  /// <summary>The <c>during</c> actions due in <paramref name="second"/>, in file order. Each is
  /// tested when the caller reaches it, so an earlier action's steps can make a later one's
  /// <c>when</c> hold in the same second.</summary>
  /// <param name="second">The second, from 0.</param>
  /// <exception cref="InvalidOperationException">A <c>when</c> test cannot be read; the message
  /// names the file and <c>during</c>.</exception>
  public IEnumerable<SetupDuring> Due(int second) {
    foreach (SetupDuring action in _model.During)
      if (IsDue(action, second))
        yield return action;
  }

  /// <summary>Takes the readings kept before <paramref name="second"/>'s ticks.</summary>
  public void Before(int second) => Recording.Before(second, _reading);

  /// <summary>Takes <paramref name="second"/>'s recording sample and the checks' sampled readings,
  /// after its ticks.</summary>
  public void Sample(int second) {
    Recording.Sample(second, _reading);
    foreach (SampledReading kept in _sampled)
      kept.Sample(_reading);
  }

  /// <summary>One verdict for each check in file order.</summary>
  /// <param name="ordersOnly">Skips the checks that hold in one tick order only.</param>
  public IReadOnlyList<SetupVerdict> Verdicts(bool ordersOnly = false) =>
    [
      .. _model.Checks.Select(
        (check, index) =>
          ordersOnly && !check.AllOrders
            ? new SetupVerdict(index, check, null, true)
            : new SetupVerdict(index, check, check.Run(Recording), false)
      ),
    ];

  /// <summary>Folds <see cref="Verdicts"/> into the case's result.</summary>
  /// <param name="ordersOnly">Judges only the checks that hold in every tick order.</param>
  /// <returns>Null when the case passes: no check fails, or for a variant with <c>fails</c> the
  /// first failing check is that one. Else the message naming the check, its <c>says</c>, the
  /// second and the readings.</returns>
  public string? Judge(bool ordersOnly = false) {
    SetupCheckFailure? failure = Verdicts(ordersOnly)
      .Select(v => v.Failure)
      .FirstOrDefault(f => f != null);
    if (_model.Fails is not { } expected)
      return failure?.ToString();
    if (failure == null)
      return $"check {expected} should fail first, and no check fails";
    return failure.Id == expected
      ? null
      : $"check {expected} should fail first, but {failure}";
  }

  private bool IsDue(SetupDuring action, int second) {
    if (action.At is { } at)
      return at == second;
    if (second < action.From || (second - action.From) % action.Period != 0)
      return false;
    try {
      return action.When.All(t => t.Holds(_reading));
    } catch (SetupReadingException e) {
      throw new InvalidOperationException(
        $"{_model.File}: during: {e.Message}"
      );
    }
  }
}
