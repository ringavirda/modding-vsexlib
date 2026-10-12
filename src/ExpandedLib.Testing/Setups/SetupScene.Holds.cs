#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Scenarios;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ExpandedLib.Testing;

public sealed partial class SetupScene
{
  private void InteractStep(SetupStep step)
  {
    BlockPos at = At(step, "at");
    string[] keys =
      (step.Body["keys"] as JArray)?.Select(k => (string)k!).ToArray() ?? [];
    foreach (string key in keys)
      if (key is not ("sneak" or "ctrl" or "sprint"))
        throw Fail(
          step.Path,
          $"\"{key}\" is not a key the hold holds: sneak, ctrl or sprint."
        );
    float seconds = (float?)step.Body["seconds"] ?? 0f;
    if (!(seconds >= 0f))
      throw Fail(step.Path, $"seconds {seconds} is negative.");
    BlockFacing? face = null;
    if (step.Body["face"] is { } letter)
      face =
        SetupLayout.FaceOf((string)letter!)
        ?? throw Fail(
          step.Path,
          $"\"{(string)letter}\" is not one of n, e, s, w, u, d."
        );
    Player.Hold(
      step.Body["with"] is { } held ? StackOf(held, step.Path) : null
    );
    bool answer = Player.Interact(
      at,
      null,
      keys.Contains("sneak"),
      keys.Contains("ctrl"),
      keys.Contains("sprint"),
      seconds,
      face,
      TickSecond
    );
    if (step.Body["returns"] is { } expected && (bool)expected != answer)
      throw Fail(
        step.Path,
        $"interact answered {answer}, not {(bool)expected}."
      );
    Player.Hold(null);
  }

  private void IgniteStep(SetupStep step)
  {
    BlockPos at = At(step, "at");
    Block block = World.GetBlock(at);
    if (block is not IIgnitable ignitable)
      throw Fail(
        step.Path,
        $"{block.Code} at {Describe(at)} is not ignitable."
      );
    float seconds = (float?)step.Body["seconds"] ?? 3f;
    EnumIgniteState? answer = null;
    float held = TestPlayer.HoldSteps(
      seconds,
      used =>
        (answer = ignitable.OnTryIgniteBlock(Player.Entity, at, used))
        == EnumIgniteState.Ignitable,
      TickSecond
    );
    if (answer == EnumIgniteState.IgniteNow)
    {
      EnumHandling handling = EnumHandling.PassThrough;
      ignitable.OnTryIgniteBlockOver(Player.Entity, at, held, ref handling);
      return;
    }
    throw Fail(
      step.Path,
      answer
        is EnumIgniteState.NotIgnitable
          or EnumIgniteState.NotIgnitablePreventDefault
        ? $"{block.Code} at {Describe(at)} is not ignitable."
        : $"{block.Code} at {Describe(at)} is not lit within {seconds} seconds."
    );
  }

  private void WindowStep(SetupStep step)
  {
    BlockEntity entity = EntityAt(step, out _);
    TestWindow window = Player.Window(entity);
    void Verb(Action verb)
    {
      try
      {
        verb();
      }
      catch (InvalidOperationException e)
      {
        throw Fail(step.Path, e.Message);
      }
    }

    if ((bool?)step.Body["open"] == true)
      Verb(window.Open);
    if (step.Body["put"] is JObject put)
      foreach (
        int slot in SlotsOf(
          put["slot"] ?? throw Fail(step.Path, "put needs a slot."),
          step.Path
        )
      )
      {
        Player.Hold(StackOf(put, step.Path));
        Verb(() => window.Put(slot));
      }
    if (step.Body["take"] is JObject take)
    {
      if (!Player.ActiveSlot.Empty)
      {
        ItemStack held = Player.ActiveSlot.Itemstack!;
        throw Fail(
          step.Path,
          $"the hand holds {held.StackSize} {held.Collectible.Code}; a take needs it empty."
        );
      }
      Verb(() => window.Take((int)take["slot"]!));
    }
    if (step.Body["press"] is JObject press)
    {
      int id = (int)press["id"]!;
      if (id <= TestWindow.ClosePacketId)
        throw Fail(
          step.Path,
          $"press id {id} is a slot, open or close packet; a button's id is above {TestWindow.ClosePacketId}."
        );
      byte[]? data = press["data"] is JArray bytes
        ? BytesOf(bytes, step.Path)
        : null;
      Verb(() => window.Press(id, data));
    }
    if ((bool?)step.Body["close"] == true)
      Verb(window.Close);
  }

  private void TickSecond()
  {
    World.FireBlockEntityTicks();
    World.Tick(1);
  }

  private List<int> SlotsOf(JToken slot, string where)
  {
    if (slot is not JArray range)
      return [(int)slot];
    int first = (int)range[0];
    int last = (int)range[1];
    return last < first
      ? throw Fail(where, $"slot range [{first}, {last}] runs backwards.")
      : Enumerable.Range(first, last - first + 1).ToList();
  }

  private byte[] BytesOf(JArray values, string where) =>
    values
      .Select(v => (int)v)
      .Select(n =>
        n is >= 0 and <= 255
          ? (byte)n
          : throw Fail(where, $"{n} is not a byte, 0 to 255.")
      )
      .ToArray();
}
#endif
