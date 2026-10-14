#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Scenarios;
using Newtonsoft.Json;
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
    ItemStack? with = step.Body["with"] is { } held
      ? StackOf(held, step.Path)
      : null;
    if (with != null)
      Draw(step.Path, with);
    Player.Hold(with);
    string? control = (string?)step.Body["control"];
    bool answer;
    try
    {
      answer =
        control == null
          ? Player.Interact(
            at,
            null,
            keys.Contains("sneak"),
            keys.Contains("ctrl"),
            keys.Contains("sprint"),
            seconds,
            face,
            TickSecond
          )
          : Player.UseControl(
            at,
            control,
            null,
            keys.Contains("sneak"),
            keys.Contains("ctrl"),
            keys.Contains("sprint"),
            seconds,
            face,
            TickSecond
          );
    }
    catch (ArgumentException e)
    {
      throw Fail(step.Path, e.Message);
    }
    Give(Player.ActiveSlot.Itemstack);
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
    if (
      SetupPlacers.For(block.Code.ToString()) is { } placer
      && placer.Ignite(this, at)
    )
      return;
    IIgnitable? ignitable = block.GetInterface<IIgnitable>(World.World, at);
    if (ignitable == null)
      throw Fail(
        step.Path,
        $"{block.Code} at {Describe(at)} is not ignitable."
      );
    float seconds = (float?)step.Body["seconds"] ?? 5f;
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
    void Within(int slot)
    {
      if (
        entity is IBlockEntityContainer { Inventory: { } inventory }
        && slot >= inventory.Count
      )
        throw Fail(
          step.Path,
          $"slot {slot} is past the inventory's {inventory.Count}."
        );
    }
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

    if (FlagOf(step, "open"))
      Verb(window.Open);
    if (step.Body["put"] is JObject put)
    {
      List<int> slots = SlotsOf(
        put["slot"] ?? throw Fail(step.Path, "put needs a slot."),
        step.Path
      );
      slots.ForEach(Within);
      foreach (int slot in slots)
      {
        ItemStack laid = StackOf(put, step.Path);
        Draw(step.Path, laid);
        Player.Hold(laid);
        Verb(() => window.Put(slot));
      }
    }
    if (step.Body["take"] is JObject take)
    {
      JToken takeSlot =
        take["slot"] ?? throw Fail(step.Path, "take needs a slot.");
      if (takeSlot is JArray)
        throw Fail(
          step.Path,
          "take takes one slot; the [first, last] range is for put."
        );
      int index = SlotOf(takeSlot, step.Path);
      Within(index);
      if (!Player.ActiveSlot.Empty)
      {
        ItemStack held = Player.ActiveSlot.Itemstack!;
        throw Fail(
          step.Path,
          $"the hand holds {held.StackSize} {held.Collectible.Code}; a take needs it empty."
        );
      }
      Verb(() => window.Take(index));
      Give(Player.ActiveSlot.Itemstack);
    }
    if (step.Body["press"] is JObject press)
    {
      int id = WholeOf(
        press["id"] ?? throw Fail(step.Path, "press needs an id."),
        step.Path
      );
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
    if (FlagOf(step, "close"))
      Verb(window.Close);
  }

  private bool FlagOf(SetupStep step, string key) =>
    step.Body[key] switch
    {
      null => false,
      { Type: JTokenType.Boolean } flag => (bool)flag,
      { } other => throw Fail(
        step.Path,
        $"{key} is {other.ToString(Formatting.None)}, not true or false."
      ),
    };

  private void TickSecond()
  {
    if (_ordered)
      World.FireBlockEntityTicks(
        1f,
        _lineListeners,
        NetworkTick,
        _networkBefore
      );
    else
    {
      World.FireBlockEntityTicks(1f, _lineListeners);
      NetworkTick();
    }
    World.StepListeners(1000, _lineListeners);
  }

  private List<int> SlotsOf(JToken slot, string where)
  {
    if (slot is not JArray range)
      return [SlotOf(slot, where)];
    if (range.Count != 2)
      throw Fail(
        where,
        $"slot range needs [first, last], not {range.Count} entries."
      );
    int first = SlotOf(range[0], where);
    int last = SlotOf(range[1], where);
    return last < first
      ? throw Fail(where, $"slot range [{first}, {last}] runs backwards.")
      : Enumerable.Range(first, last - first + 1).ToList();
  }

  private int SlotOf(JToken slot, string where)
  {
    int index = WholeOf(slot, where);
    return index < 0 ? throw Fail(where, $"slot {index} is negative.") : index;
  }

  private int WholeOf(JToken value, string where)
  {
    if (value.Type != JTokenType.Integer)
      throw Fail(
        where,
        $"{value.ToString(Formatting.None)} is not a whole number."
      );
    return
      value is JValue { Value: int or long }
      && (long)value is >= int.MinValue and <= int.MaxValue
      ? (int)value
      : throw Fail(
        where,
        $"{value.ToString(Formatting.None)} is out of range."
      );
  }

  private byte[] BytesOf(JArray values, string where) =>
    values
      .Select(v => WholeOf(v, where))
      .Select(n =>
        n is >= 0 and <= 255
          ? (byte)n
          : throw Fail(where, $"{n} is not a byte, 0 to 255.")
      )
      .ToArray();
}
#endif
