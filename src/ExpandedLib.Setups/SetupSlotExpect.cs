using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Scenarios;

/// <summary>The <c>expect</c> forms that read the hotbar: <c>{ slot, code?, count?, attribute?: { key,
/// equals } }</c> for one slot, and <c>{ code, count? }</c> with no <c>slot</c> for an item anywhere in the
/// hotbar. Both layers read the stacks and test them here.</summary>
internal static class SetupSlotExpect {
  /// <summary>The number of hotbar slots a <c>slot</c> index may name.</summary>
  public const int Slots = 12;

  /// <summary>Whether the <c>expect</c> body is a slot reading or a hotbar-wide one.</summary>
  public static bool Is(JObject body) => IsSlot(body) || IsHotbar(body);

  /// <summary>Whether the <c>expect</c> body names one slot.</summary>
  public static bool IsSlot(JObject body) => body["slot"] != null;

  /// <summary>Whether the <c>expect</c> body has a string <c>code</c> and no <c>slot</c>.</summary>
  public static bool IsHotbar(JObject body) =>
    body["slot"] == null && body["code"] is { Type: JTokenType.String };

  /// <summary>Checks the keys of a slot reading or a hotbar-wide one.</summary>
  /// <exception cref="SetupFileException">The body has a key besides those or none of <c>code</c>,
  /// <c>count</c> and <c>attribute</c>, the slot is no hotbar index, <c>code</c> is neither a string nor
  /// null, <c>count</c> is no whole number, <c>attribute</c> lacks a <c>key</c> or an <c>equals</c>, or a
  /// hotbar-wide reading has an <c>attribute</c>.</exception>
  public static void Validate(JObject body, SetupJson.Where at) {
    if (IsHotbar(body)) {
      SetupJson.RefuseUnknown(body, at, "code", "count");
      if (body["count"] != null)
        SetupJson.Integer(body["count"], at.In("count"));
      return;
    }
    SetupJson.RefuseUnknown(body, at, "slot", "code", "count", "attribute");
    if (
      !body.ContainsKey("code")
      && !body.ContainsKey("count")
      && !body.ContainsKey("attribute")
    )
      throw at.Fail("a slot reading needs code, count or attribute.");
    int slot = SetupJson.Integer(body["slot"], at.In("slot"));
    if (slot < 0 || slot >= Slots)
      throw at.In("slot").Fail($"takes a hotbar index from 0 to {Slots - 1}.");
    if (body["code"] is { Type: not JTokenType.Null } code)
      SetupJson.String(code, at.In("code"));
    if (body["count"] != null)
      SetupJson.Integer(body["count"], at.In("count"));
    if (body["attribute"] != null) {
      SetupJson.Where where = at.In("attribute");
      JObject attribute = SetupJson.Object(body["attribute"], where);
      SetupJson.RefuseUnknown(attribute, where, "key", "equals");
      SetupJson.String(attribute["key"], where.In("key"));
      SetupJson.Literal(attribute["equals"], where.In("equals"));
    }
  }

  /// <summary>The slot index the reading names.</summary>
  public static int SlotOf(JObject body) => (int)body["slot"]!;

  /// <summary>Tests the whole hotbar against a hotbar-wide reading.</summary>
  /// <param name="body">The <c>expect</c> body: a string <c>code</c> (domain defaults to <c>game</c>) and an
  /// optional <c>count</c>.</param>
  /// <param name="hotbar">The stacks of hotbar slots 0 to <see cref="Slots"/> - 1; null entries are empty.</param>
  /// <returns>What differs, or null when some slot holds the code and, with a <c>count</c>, the stacks of that
  /// code add up to it.</returns>
  public static string? HotbarMismatch(
    JObject body,
    IEnumerable<ItemStack?> hotbar
  ) {
    string code = (string)body["code"]!;
    int held = HotbarHeld(body, hotbar);
    if (held == 0)
      return $"holds no {code}; it holds {Contents(hotbar)}";
    if (body["count"] != null && held != (int)body["count"]!)
      return $"holds {held} of {code} in all, expected {(int)body["count"]!}";
    return null;
  }

  /// <summary>The total stack size of the hotbar's stacks of a hotbar-wide reading's <c>code</c>.</summary>
  public static int HotbarHeld(JObject body, IEnumerable<ItemStack?> hotbar) {
    var wanted = new AssetLocation((string)body["code"]!);
    int held = 0;
    foreach (ItemStack? stack in hotbar)
      if (stack != null && wanted.Equals(stack.Collectible.Code))
        held += stack.StackSize;
    return held;
  }

  private static string Contents(IEnumerable<ItemStack?> hotbar) =>
    hotbar
      .Where(s => s != null)
      .Select(Describe)
      .DefaultIfEmpty("nothing")
      .Aggregate((a, b) => $"{a}, {b}");

  /// <summary>What a slot holds, as the step line prints it.</summary>
  /// <param name="stack">The slot's stack, or null for an empty slot.</param>
  public static string Describe(ItemStack? stack) =>
    stack == null ? "empty" : $"{stack.Collectible.Code} x {stack.StackSize}";

  /// <summary>Tests a slot against the reading.</summary>
  /// <param name="body">The <c>expect</c> body. A <c>code</c> of null expects an empty slot; a string
  /// <c>code</c> is an asset location whose domain defaults to <c>game</c>.</param>
  /// <param name="stack">The slot's stack, or null for an empty slot.</param>
  /// <returns>What differs, or null when the slot is as expected.</returns>
  public static string? Mismatch(JObject body, ItemStack? stack) {
    if (body["code"] is { Type: JTokenType.Null } && stack != null)
      return $"holds {Describe(stack)}, expected it empty";
    if (body["code"] is { Type: JTokenType.String } code) {
      if (stack == null)
        return $"is empty, expected {(string)code!}";
      if (!new AssetLocation((string)code!).Equals(stack.Collectible.Code))
        return $"holds {Describe(stack)}, expected {(string)code!}";
    }
    if (body["count"] != null) {
      int held = stack?.StackSize ?? 0;
      if (held != (int)body["count"]!)
        return $"holds a count of {held}, expected {(int)body["count"]!}";
    }
    if (body["attribute"] is JObject attribute) {
      string key = (string)attribute["key"]!;
      object? value = stack?.Attributes?[key]?.GetValue();
      if (!Same(value, ((JValue)attribute["equals"]!).Value))
        return $"reads {key} as {Text(value)}, expected {Text(((JValue)attribute["equals"]!).Value)}";
    }
    return null;
  }

  internal static bool Same(object? actual, object? expected) {
    if (actual == null || expected == null)
      return actual == expected;
    string a = Text(actual);
    string e = Text(expected);
    return
      double.TryParse(
        a,
        NumberStyles.Float,
        CultureInfo.InvariantCulture,
        out double x
      )
      && double.TryParse(
        e,
        NumberStyles.Float,
        CultureInfo.InvariantCulture,
        out double y
      )
      ? Math.Abs(x - y) < 1e-4
      : a == e;
  }

  internal static string Text(object? value) =>
    value == null
      ? "absent"
      : Convert.ToString(value, CultureInfo.InvariantCulture)!;
}
