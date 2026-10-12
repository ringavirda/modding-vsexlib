#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Scenarios;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ExpandedLib.Testing;

public sealed partial class SetupScene
{
  private const BindingFlags Members =
    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

  private TestPlayer? _player;

  private TestPlayer Player => _player ??= World.Player();

  private BlockEntity EntityAt(SetupStep step, out BlockPos at)
  {
    at = At(step, "at");
    return World.GetBlockEntity(at)
      ?? throw Fail(
        step.Path,
        $"cell {Describe(at)} holds {World.GetBlock(at).Code}, which has no block entity."
      );
  }

  private ItemStack StackOf(JToken body, string where)
  {
    string code =
      (string?)body["stack"]
      ?? throw Fail(where, "a stack needs its \"stack\" code.");
    var location = new AssetLocation(code);
    CollectibleObject collectible =
      (CollectibleObject?)World.World.GetItem(location)
      ?? World.World.GetBlock(location)
      ?? throw Fail(where, $"\"{code}\" is not registered by the load.");
    var stack = new ItemStack(
      collectible,
      body["count"] is { } count ? WholeOf(count, where) : 1
    );
    if (body["temperature"] is { } temperature)
      stack.Collectible.SetTemperature(World.World, stack, (float)temperature);
    if (body["content"] is { } content)
      Fill(stack, (string)content!, body["litres"], where);
    return stack;
  }

  private void Fill(
    ItemStack container,
    string contentCode,
    JToken? litres,
    string where
  )
  {
    if (container.Collectible is not BlockLiquidContainerBase vessel)
      throw Fail(
        where,
        $"\"{container.Collectible.Code}\" is not a liquid container, so it holds no \"content\"."
      );
    Item liquid =
      World.World.GetItem(new AssetLocation(contentCode))
      ?? throw Fail(where, $"\"{contentCode}\" is not registered by the load.");
    float amount = litres is null
      ? throw Fail(where, "a \"content\" needs its \"litres\".")
      : (float)litres;
    container.Attributes ??= new TreeAttribute();
    Ready(vessel);
    var poured = new ItemStack(liquid);
    float perLitre =
      BlockLiquidContainerBase.GetContainableProps(poured)?.ItemsPerLitre
      ?? throw Fail(
        where,
        $"\"{contentCode}\" is no liquid a container takes."
      );
    poured.StackSize = (int)Math.Ceiling(perLitre * amount);
    vessel.TryPutLiquid(container, poured, amount);
    float held = vessel.GetCurrentLitres(container);
    if (held < amount - 0.001f)
      throw Fail(
        where,
        $"\"{container.Collectible.Code}\" took {held} of the {amount} litres of \"{contentCode}\"."
      );
  }

  private void InsertStep(SetupStep step)
  {
    BlockEntity be = EntityAt(step, out BlockPos at);
    if (be is not IBlockEntityContainer { Inventory: { } inventory })
      throw Fail(
        step.Path,
        $"{be.GetType().Name} at {Describe(at)} has no inventory."
      );
    ItemStack stack = StackOf(step.Body, step.Path);
    int count = stack.StackSize;
    var source = new DummySlot(stack);
    ItemSlot? target = step.Body["slot"] is { } slot
      ? (int)slot >= 0 && (int)slot < inventory.Count
        ? inventory[(int)slot]
        : throw Fail(
          step.Path,
          (int)slot < 0
            ? $"slot {(int)slot} is negative."
            : $"slot {(int)slot} is past the inventory's {inventory.Count}."
        )
      : inventory.GetBestSuitedSlot(source)?.slot;
    int moved =
      target == null ? 0 : source.TryPutInto(World.World, target, count);
    if (moved < count)
      throw Fail(
        step.Path,
        $"{be.GetType().Name} at {Describe(at)} took {moved} of {count} {stack.Collectible.Code}."
      );
  }

  private void SetStep(SetupStep step)
  {
    BlockEntity be = EntityAt(step, out BlockPos at);
    if (step.Body["tree"] is JObject values)
    {
      var tree = new TreeAttribute();
      be.ToTreeAttributes(tree);
      foreach (JProperty value in values.Properties())
      {
        if (!tree.HasAttribute(value.Name))
          throw Fail(
            step.Path,
            $"{be.GetType().Name} at {Describe(at)} has no tree key \"{value.Name}\"."
          );
        tree[value.Name] = Retyped(
          tree[value.Name],
          value.Value,
          step.Path,
          value.Name
        );
      }
      be.FromTreeAttributes(tree, World.World);
    }
    if (step.Body["member"] is JObject members)
      foreach (JProperty member in members.Properties())
        WriteMember(be, member.Name, member.Value, step.Path);
    be.MarkDirty();
  }

  private IAttribute Retyped(
    IAttribute old,
    JToken value,
    string where,
    string key
  )
  {
    try
    {
      return old switch
      {
        IntAttribute => new IntAttribute((int)value),
        LongAttribute => new LongAttribute((long)value),
        FloatAttribute => new FloatAttribute((float)value),
        DoubleAttribute => new DoubleAttribute((double)value),
        BoolAttribute => new BoolAttribute((bool)value),
        StringAttribute => new StringAttribute((string?)value),
        _ => throw Fail(
          where,
          $"tree key \"{key}\" holds a {old.GetType().Name}, which \"set\" does not write."
        ),
      };
    }
    catch (Exception e)
      when (e
          is FormatException
            or InvalidCastException
            or OverflowException
            or ArgumentException
      )
    {
      throw Fail(
        where,
        $"{value} does not fit tree key \"{key}\", a {old.GetType().Name}: {e.Message}"
      );
    }
  }

  private void WriteMember(
    object target,
    string path,
    JToken value,
    string where
  )
  {
    string[] names = path.Split('.');
    foreach (string name in names[..^1])
      target =
        ReadMember(target, name, where)
        ?? throw Fail(where, $"\"{name}\" in \"{path}\" is null.");
    string last = names[^1];
    for (Type? type = target.GetType(); type != null; type = type.BaseType)
    {
      if (type.GetField(last, Members | BindingFlags.DeclaredOnly) is { } field)
      {
        field.SetValue(target, ConvertTo(value, field.FieldType, where));
        return;
      }
      if (
        type.GetProperty(last, Members | BindingFlags.DeclaredOnly) is
        { CanWrite: true } property
      )
      {
        property.SetValue(
          target,
          ConvertTo(value, property.PropertyType, where)
        );
        return;
      }
    }
    throw Fail(
      where,
      $"{target.GetType().Name} has no writable member \"{last}\"."
    );
  }

  private object? ReadMember(object target, string name, string where)
  {
    for (Type? type = target.GetType(); type != null; type = type.BaseType)
    {
      if (type.GetField(name, Members | BindingFlags.DeclaredOnly) is { } field)
        return field.GetValue(target);
      if (
        type.GetProperty(name, Members | BindingFlags.DeclaredOnly) is
        { CanRead: true } property
      )
        return property.GetValue(target);
    }
    throw Fail(where, $"{target.GetType().Name} has no member \"{name}\".");
  }

  private void CallStep(SetupStep step)
  {
    BlockEntity be = EntityAt(step, out BlockPos at);
    string method = (string)step.Body["method"]!;
    object target = be;
    int dot = method.IndexOf('.');
    if (dot >= 0)
    {
      string behaviour = method[..dot];
      method = method[(dot + 1)..];
      target =
        be.Behaviors.FirstOrDefault(b => b.GetType().Name == behaviour)
        ?? throw Fail(
          step.Path,
          $"{be.GetType().Name} at {Describe(at)} has no behaviour {behaviour}."
        );
    }
    JToken[] args = step.Body["args"] is JArray array ? [.. array] : [];
    MethodInfo info =
      target
        .GetType()
        .GetMethods(BindingFlags.Instance | BindingFlags.Public)
        .FirstOrDefault(m =>
          m.Name == method && m.GetParameters().Length == args.Length
        )
      ?? throw Fail(
        step.Path,
        $"{target.GetType().Name} has no public method {method} taking {args.Length} arguments."
      );
    object?[] converted =
    [
      .. info.GetParameters()
        .Select((p, i) => ConvertTo(args[i], p.ParameterType, step.Path)),
    ];
    object? result;
    try
    {
      result = info.Invoke(target, converted);
    }
    catch (TargetInvocationException e) when (e.InnerException != null)
    {
      throw Fail(
        step.Path,
        $"{method} threw {e.InnerException.GetType().Name}: {e.InnerException.Message}"
      );
    }
    if (step.Body["returns"] is { } expected && !Same(expected, result))
      throw Fail(
        step.Path,
        $"{method} returned {result ?? "null"}, not {expected}."
      );
  }

  private static bool Same(JToken expected, object? actual)
  {
    if (actual == null)
      return expected.Type == JTokenType.Null;
    if (actual is Enum named && expected.Type == JTokenType.String)
      return named.ToString() == (string?)expected;
    JToken got = JToken.FromObject(actual);
    return
      expected.Type is JTokenType.Integer or JTokenType.Float
      && got.Type is JTokenType.Integer or JTokenType.Float
      ? (double)expected == (double)got
      : JToken.DeepEquals(expected, got);
  }

  private object? ConvertTo(JToken value, Type type, string where)
  {
    Type bare = Nullable.GetUnderlyingType(type) ?? type;
    if (value.Type == JTokenType.Null)
      return bare.IsValueType && bare == type
        ? throw Fail(where, $"null is not a {type.Name}.")
        : null;
    if (bare.IsEnum)
    {
      if (value.Type != JTokenType.String)
        try
        {
          return Enum.ToObject(bare, (int)value);
        }
        catch (Exception e)
          when (e
              is FormatException
                or InvalidCastException
                or OverflowException
                or ArgumentException
          )
        {
          throw Fail(where, $"{value} is not a member of {bare.Name}.");
        }
      return Enum.TryParse(bare, (string)value!, false, out object? member)
        ? member
        : throw Fail(
          where,
          $"\"{(string)value!}\" is not a member of {bare.Name}."
        );
    }
    if (bare == typeof(AssetLocation))
      return new AssetLocation((string)value!);
    if (bare == typeof(IPlayer))
      return (string?)value == "player"
        ? Player.Player
        : throw Fail(where, "an IPlayer argument is \"player\".");
    if (bare == typeof(ItemStack) || bare == typeof(ItemSlot))
    {
      ItemStack stack = StackOf(value, where);
      return bare == typeof(ItemStack) ? stack : new DummySlot(stack);
    }
    try
    {
      return value.ToObject(bare);
    }
    catch (Exception e)
      when (e
          is FormatException
            or InvalidCastException
            or Newtonsoft.Json.JsonException
      )
    {
      throw Fail(where, $"{value} is not a {bare.Name}: {e.Message}");
    }
  }
}
#endif
