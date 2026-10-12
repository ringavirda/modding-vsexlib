using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Scenarios;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing.Live;

/// <summary>Turns file values into the arguments, fields and stacks the steps hand to the
/// game.</summary>
internal static class StepValues {
  private const BindingFlags Instance =
    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

  /// <summary>A stack of <paramref name="count"/> of the item or block with
  /// <paramref name="code"/>.</summary>
  /// <exception cref="StepFailure">The code names neither an item nor a block.</exception>
  public static ItemStack Stack(
    ScenarioContext ctx,
    string code,
    int count,
    float? temperature = null
  ) {
    var location = new AssetLocation(code);
    CollectibleObject? collectible =
      ctx.World.GetItem(location)
      ?? (CollectibleObject?)ctx.World.GetBlock(location);
    if (collectible == null || collectible.Id == 0)
      throw new StepFailure(
        $"\"{code}\" is no item or block code.",
        Suspect.Scenario
      );
    var stack = new ItemStack(collectible, count);
    if (temperature is { } t)
      collectible.SetTemperature(ctx.World, stack, t);
    return stack;
  }

  /// <summary>The value of <paramref name="token"/> as <paramref name="type"/>: null, numbers,
  /// strings (enum names and asset codes too), booleans, and <c>{ stack, count }</c> for an
  /// <see cref="ItemStack"/> or an <see cref="ItemSlot"/>.</summary>
  /// <exception cref="StepFailure">The value has no conversion to the type.</exception>
  public static object? Convert(ScenarioContext ctx, JToken token, Type type) {
    if (token.Type == JTokenType.Null)
      return null;
    if (type == typeof(IPlayer))
      throw new StepFailure(
        "a player argument needs the scenario player.",
        Suspect.Scenario
      );
    if (type == typeof(ItemStack) || type == typeof(ItemSlot)) {
      var o =
        token as JObject
        ?? throw new StepFailure(
          "a stack is { \"stack\": code, \"count\": n }.",
          Suspect.Scenario
        );
      ItemStack stack = Stack(
        ctx,
        (string?)o["stack"]
          ?? throw new StepFailure(
            "a stack takes \"stack\".",
            Suspect.Scenario
          ),
        (int?)o["count"] ?? 1
      );
      return type == typeof(ItemStack) ? stack : new DummySlot(stack);
    }
    if (type == typeof(AssetLocation))
      return new AssetLocation((string)token!);
    if (type.IsEnum)
      return Enum.Parse(type, (string)token!, ignoreCase: true);
    try {
      return token.ToObject(type);
    } catch (Exception e)
        when (e is FormatException or JsonException or InvalidCastException) {
      throw new StepFailure(
        $"{token} does not convert to {type.Name}.",
        Suspect.Scenario,
        e
      );
    }
  }

  /// <summary>The object a <c>member</c> or <c>method</c> path starts at: the block entity,
  /// or the behaviour whose class the first of several segments names.</summary>
  public static (object Root, IReadOnlyList<string> Path) Root(
    BlockEntity be,
    string path
  ) {
    string[] segments = path.Split('.');
    BlockEntityBehavior? behavior =
      segments.Length > 1
        ? be.Behaviors?.FirstOrDefault(b => b.GetType().Name == segments[0])
        : null;
    return behavior == null ? (be, segments) : (behavior, segments[1..]);
  }

  /// <summary>Writes <paramref name="token"/> to the field or property the path names, public
  /// or not.</summary>
  /// <exception cref="StepFailure">A segment names no member.</exception>
  public static void SetMember(
    ScenarioContext ctx,
    object root,
    IReadOnlyList<string> path,
    JToken token
  ) {
    object holder =
      path.Count == 1
        ? root
        : SetupMembers.Walk(root, [.. path.Take(path.Count - 1)], ctx.World)!;
    string name = path[^1];
    for (Type? t = holder.GetType(); t != null; t = t.BaseType) {
      if (t.GetField(name, Instance | BindingFlags.DeclaredOnly) is { } field) {
        field.SetValue(holder, Convert(ctx, token, field.FieldType));
        return;
      }
      if (
        t.GetProperty(name, Instance | BindingFlags.DeclaredOnly) is { CanWrite: true } property
      ) {
        property.SetValue(holder, Convert(ctx, token, property.PropertyType));
        return;
      }
    }
    throw new StepFailure(
      $"{holder.GetType().Name} has no writable member \"{name}\".",
      Suspect.Scenario
    );
  }
}
