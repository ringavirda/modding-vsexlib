using System;
using System.Globalization;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Scenarios;

/// <summary>The <c>expect</c> form that reads one hotbar slot: <c>{ slot, code?, count?, attribute?: { key,
/// equals } }</c>. Both layers read the slot and test it here.</summary>
internal static class SetupSlotExpect {
  /// <summary>The number of hotbar slots a <c>slot</c> index may name.</summary>
  public const int Slots = 12;

  /// <summary>Whether the <c>expect</c> body is a slot reading.</summary>
  public static bool Is(JObject body) => body["slot"] != null;

  /// <summary>Checks the keys of a slot reading.</summary>
  /// <exception cref="SetupFileException">The body has a key besides those or none of <c>code</c>,
  /// <c>count</c> and <c>attribute</c>, the slot is no hotbar index, <c>code</c> is neither a string nor
  /// null, <c>count</c> is no whole number, or <c>attribute</c> lacks a <c>key</c> or an <c>equals</c>.</exception>
  public static void Validate(JObject body, SetupJson.Where at) {
    SetupJson.RefuseUnknown(body, at, "slot", "code", "count", "attribute");
    if (!body.ContainsKey("code") && !body.ContainsKey("count") && !body.ContainsKey("attribute"))
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

  private static bool Same(object? actual, object? expected) {
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

  private static string Text(object? value) =>
    value == null
      ? "absent"
      : Convert.ToString(value, CultureInfo.InvariantCulture)!;
}
