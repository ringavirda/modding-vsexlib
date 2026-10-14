#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ExpandedLib.Scenarios;
using ExpandedLib.Structures;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

public sealed partial class SetupScene
{
  private const int DefaultRepeatMax = 20;

  private void ExpectStep(SetupStep step)
  {
    if (step.Body["footprint"] != null)
      FootprintExpect(step);
    else if (step.Body["info"] != null)
      InfoExpect(step);
    else if (step.Body["same"] != null)
      SameExpect(step);
    else
    {
      var reading = new ReadingContext(View);
      foreach (ReadingTest test in step.Tests)
        if (!Holds(test, reading, step.Path))
          throw Fail(
            step.Path,
            $"read {test.Describe(reading)}, expected {step.Body.ToString(Formatting.None)}."
          );
    }
  }

  private bool Holds(ReadingTest test, ReadingContext reading, string where)
  {
    try
    {
      return test.Holds(reading);
    }
    catch (SetupReadingException e)
    {
      throw Fail(where, e.Message);
    }
  }

  private IReadOnlyList<ReadingTest> TestsOf(SetupStep step, string key) =>
    step.Body is not JObject || step.Body[key] == null
      ? []
      : ReadingTests.ParseList(
        step.Body[key],
        new SetupJson.Where(_file.Name, step.Path).In(key),
        new ReadingScope(null, false, false, new SetupReferences())
      );

  private static string Read(
    IReadOnlyList<ReadingTest> tests,
    ReadingContext reading
  ) => string.Join("; ", tests.Select(t => t.Describe(reading)));

  private void InfoExpect(SetupStep step)
  {
    BlockEntity be = EntityAt(step, out BlockPos at);
    Regex pattern;
    try
    {
      pattern = new Regex(
        (string)step.Body["info"]!,
        RegexOptions.CultureInvariant
      );
    }
    catch (ArgumentException e)
    {
      throw Fail(step.Path, $"info is not a regex: {e.Message}");
    }
    var text = new StringBuilder();
    if (step.Body["control"] == null)
    {
      Player.LookAway();
      be.GetBlockInfo(Player.Player, text);
    }
    else
    {
      try
      {
        Player.LookAt(at, (string)step.Body["control"]!);
      }
      catch (ArgumentException e)
      {
        throw Fail(step.Path, e.Message);
      }
      text.Append(
        World.GetBlock(at).GetPlacedBlockInfo(World.World, at, Player.Player)
      );
    }
    string[] lines = text.ToString()
      .Split(
        '\n',
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
      );
    if (!lines.Any(pattern.IsMatch))
      throw Fail(
        step.Path,
        $"no info line of {be.GetType().Name} at {Describe(at)} matches \"{pattern}\"; it reads: {string.Join(" | ", lines)}."
      );
  }

  private void SameExpect(SetupStep step)
  {
    string type =
      (string?)step.Body["network"]
      ?? throw Fail(step.Path, "\"same\" takes \"network\".");
    BlockPos at = At(step, "at");
    BlockPos other = At(step, "same");
    object? first = View.NetworkAt(at, type);
    if (first == null || !ReferenceEquals(first, View.NetworkAt(other, type)))
      throw Fail(
        step.Path,
        $"{Describe(at)} and {Describe(other)} are not in one {type} network."
      );
  }

  private void FootprintExpect(SetupStep step)
  {
    string mode = (string)step.Body["footprint"]!;
    string id =
      (string?)step.Body["of"]
      ?? throw Fail(step.Path, "\"footprint\" takes \"of\".");
    SetupFrame frame =
      (
        View.Placed(id)
        ?? throw Fail(step.Path, $"\"{id}\" has placed nothing.")
      ).Frame
      ?? throw Fail(step.Path, $"\"{id}\" is no exlib structure.");
    if (mode is not ("filled" or "air"))
      throw Fail(
        step.Path,
        $"footprint is \"filled\" or \"air\", not \"{mode}\"."
      );
    Block principal = World.GetBlock(frame.Principal);
    foreach (
      BlockPos cell in new[] { frame.Principal }
        .Concat(frame.Footprint.Select(c => c.Pos))
        .Distinct()
    )
    {
      bool holds =
        mode == "air"
          ? World.GetBlock(cell).Id == 0
          : World.GetBlock(cell).Id == principal.Id
            || (
              World.GetBlockEntity(cell)
                is BlockEntityStructureFiller { Principal: { } p }
              && p.Equals(frame.Principal)
            );
      if (!holds)
        throw Fail(
          step.Path,
          $"footprint cell {Describe(cell)} holds {World.GetBlock(cell).Code}, expected {mode}."
        );
    }
  }

  private void WaitStep(SetupStep step)
  {
    IReadOnlyList<ReadingTest> until = TestsOf(step, "until");
    IReadOnlyList<ReadingTest> hold = TestsOf(step, "hold");
    double seconds = step.Body is JValue ? (double)step.Body : 0;
    if (step.Body is JObject o)
    {
      if (o["until"] != null && o["max"] == null)
        throw Fail(step.Path, "\"until\" takes \"max\".");
      if (o["until"] == null && o["seconds"] == null)
        throw Fail(step.Path, "wait takes seconds, or until with max.");
      seconds = (double)(o["until"] != null ? o["max"]! : o["seconds"]!);
    }
    var reading = new ReadingContext(View);
    for (double elapsed = 0; ; elapsed++)
    {
      foreach (ReadingTest test in hold)
        if (!Holds(test, reading, step.Path))
          throw Fail(
            step.Path,
            $"hold broke after {Seconds(elapsed)} s: {test.Describe(reading)}."
          );
      if (until.Count > 0 && until.All(t => Holds(t, reading, step.Path)))
        return;
      if (elapsed >= seconds)
      {
        if (until.Count > 0)
          throw Fail(
            step.Path,
            $"not met in {Seconds(seconds)} s; last read {Read(until, reading)}."
          );
        return;
      }
      TickSecond();
    }
  }

  private static string Seconds(double value) =>
    value.ToString("F1", CultureInfo.InvariantCulture);

  private void RepeatStep(SetupStep step)
  {
    int? times = (int?)step.Body["times"];
    int max = (int?)step.Body["max"] ?? DefaultRepeatMax;
    IReadOnlyList<ReadingTest> until = TestsOf(step, "until");
    if (times == null && until.Count == 0)
      throw Fail(step.Path, "repeat takes \"times\" or \"until\".");
    var reading = new ReadingContext(View);
    for (int round = 1; ; round++)
    {
      RunSteps(step.Steps);
      if (until.Count > 0 && until.All(t => Holds(t, reading, step.Path)))
        return;
      if (round >= (times ?? max))
      {
        if (times == null)
          throw Fail(
            step.Path,
            $"until not met after {max} round(s); last read {Read(until, reading)}."
          );
        return;
      }
    }
  }
}
#endif
