using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ExpandedLib.Scenarios;

/// <summary>A reading or a check that cannot be read: the step, or the file at its first
/// sample, fails with it.</summary>
internal sealed class SetupReadingException(string message)
  : Exception(message);

/// <summary>When a reading is asked: once after the build (<see cref="Start"/>), before a
/// second's ticks, after them, or over a finished recording.</summary>
internal enum ReadingPhase {
  Start,
  Before,
  After,
  Check,
}

/// <summary>What a reading is read against: the world, the second, and for a check the
/// recording its <c>series</c> readings take their values from.</summary>
internal sealed class ReadingContext(
  ISetupWorld? world,
  SetupRecording? recording = null
) {
  public ISetupWorld World =>
    world ?? throw new SetupReadingException("This reading needs a world.");

  public SetupRecording? Recording => recording;

  /// <summary>The second being sampled or checked, from 0; -1 before second 0.</summary>
  public int Second { get; set; } = -1;

  public ReadingPhase Phase { get; set; } = ReadingPhase.After;
}

/// <summary>A value read off the world (setups.md, Readings): a float, a bool or a string,
/// an enum read as its member's name.</summary>
internal abstract class Reading {
  public virtual IEnumerable<Reading> Children => [];

  public abstract object Read(ReadingContext ctx);

  /// <summary>Takes what a reading keeps from the finished build, children first.</summary>
  public virtual void Start(ReadingContext ctx) {
    foreach (Reading child in Children)
      child.Start(ctx);
  }

  /// <summary>Takes what a reading keeps from before a second's ticks, children first.</summary>
  public virtual void Before(ReadingContext ctx) {
    foreach (Reading child in Children)
      child.Before(ctx);
  }

  /// <summary>This reading and every reading under it, depth first.</summary>
  public IEnumerable<Reading> All() =>
    new[] { this }.Concat(Children.SelectMany(c => c.All()));

  public float Number(ReadingContext ctx) => SetupValues.Number(Read(ctx));

  public virtual string Describe => GetType().Name;
}

/// <summary>How a reading's raw value becomes a float, a bool or a string.</summary>
internal static class SetupValues {
  public static object Normalize(object? value) =>
    value switch {
      null => "",
      bool b => b,
      string s => s,
      float f => f,
      Enum e => e.ToString(),
      IConvertible c when IsNumeric(c) => Convert.ToSingle(
        c,
        CultureInfo.InvariantCulture
      ),
      _ => value.ToString() ?? "",
    };

  private static bool IsNumeric(IConvertible c) =>
    c.GetTypeCode()
      is TypeCode.SByte
        or TypeCode.Byte
        or TypeCode.Int16
        or TypeCode.UInt16
        or TypeCode.Int32
        or TypeCode.UInt32
        or TypeCode.Int64
        or TypeCode.UInt64
        or TypeCode.Single
        or TypeCode.Double
        or TypeCode.Decimal;

  /// <exception cref="SetupReadingException">The value is no number.</exception>
  public static float Number(object value) =>
    value is float f
      ? f
      : throw new SetupReadingException($"\"{Text(value)}\" is no number.");

  public static string Text(object value) =>
    value switch {
      bool b => b ? "true" : "false",
      float f => f.ToString("R", CultureInfo.InvariantCulture),
      _ => value.ToString() ?? "",
    };
}

/// <summary>Walks a dotted member path over fields, properties and world-taking methods,
/// public or not.</summary>
internal static class SetupMembers {
  private const BindingFlags Instance =
    BindingFlags.Instance
    | BindingFlags.Public
    | BindingFlags.NonPublic
    | BindingFlags.DeclaredOnly;

  public static bool Has(Type type, string segment) =>
    Find(type, segment) != null;

  /// <exception cref="SetupReadingException">A segment names a member the type lacks, or a
  /// segment before the last reads null.</exception>
  public static object? Walk(
    object root,
    IReadOnlyList<string> path,
    IWorldAccessor? world
  ) {
    object? at = root;
    for (int i = 0; i < path.Count; i++) {
      if (at == null)
        throw new SetupReadingException(
          $"\"{string.Join('.', path.Take(i))}\" is null."
        );
      MemberInfo member = Find(at.GetType(), path[i])
        ?? throw new SetupReadingException(
          $"{at.GetType().Name} has no member \"{path[i]}\"."
        );
      at = member switch {
        FieldInfo f => f.GetValue(at),
        PropertyInfo p => p.GetValue(at),
        MethodInfo m => m.Invoke(at, Arguments(m, world)),
        _ => null,
      };
    }
    return at;
  }

  private static object?[] Arguments(MethodInfo method, IWorldAccessor? world) {
    if (world == null)
      throw new SetupReadingException($"{method.Name}() needs a world.");
    return
    [
      .. method
        .GetParameters()
        .Select(p =>
          p.ParameterType.IsInstanceOfType(world)
            ? (object)world
            : world.BlockAccessor
        ),
    ];
  }

  private static MemberInfo? Find(Type type, string segment) {
    bool call = segment.EndsWith("()", StringComparison.Ordinal);
    string name = call ? segment[..^2] : segment;
    for (Type? t = type; t != null; t = t.BaseType) {
      if (call) {
        MethodInfo? method = t.GetMethods(Instance)
          .FirstOrDefault(m =>
            m.Name == name
            && m.GetParameters()
              .All(p =>
                p.ParameterType == typeof(IWorldAccessor)
                || p.ParameterType == typeof(IBlockAccessor)
              )
          );
        if (method != null)
          return method;
        continue;
      }
      if (t.GetField(name, Instance) is { } field)
        return field;
      if (
        t.GetProperty(name, Instance) is { } property
        && property.GetIndexParameters().Length == 0
      )
        return property;
    }
    return null;
  }
}

/// <summary><c>{ at, member: path, or? }</c>: a member of the block entity at the cell.</summary>
internal sealed class MemberReading(
  SetupPosition at,
  string path,
  object? or
) : Reading {
  private readonly string[] _path = path.Split('.');

  public override object Read(ReadingContext ctx) {
    BlockPos pos = at.Resolve(ctx.World);
    BlockEntity? be = ctx.World.BlockEntityAt(pos);
    if (be == null)
      return or ?? throw new SetupReadingException($"No block entity at {pos}.");
    object root = be;
    IReadOnlyList<string> path = _path;
    BlockEntityBehavior? behavior = _path.Length > 1
      ? be.Behaviors?.FirstOrDefault(b => b.GetType().Name == _path[0])
      : null;
    if (behavior != null) {
      root = behavior;
      path = _path[1..];
    }
    if (!SetupMembers.Has(root.GetType(), path[0]))
      return or
        ?? throw new SetupReadingException(
          $"{root.GetType().Name} at {pos} has no member \"{path[0]}\"."
        );
    return SetupValues.Normalize(
      SetupMembers.Walk(root, path, ctx.World.World)
    );
  }

  public override string Describe => path;
}

/// <summary><c>{ at, tree: key, or? }</c>: a key of the block entity's saved tree.</summary>
internal sealed class TreeReading(SetupPosition at, string key, object? or)
  : Reading {
  public override object Read(ReadingContext ctx) {
    BlockPos pos = at.Resolve(ctx.World);
    BlockEntity? be = ctx.World.BlockEntityAt(pos);
    if (be == null)
      return or ?? throw new SetupReadingException($"No block entity at {pos}.");
    var tree = new TreeAttribute();
    be.ToTreeAttributes(tree);
    IAttribute? value = tree[key];
    if (value == null)
      return or
        ?? throw new SetupReadingException(
          $"{be.GetType().Name} at {pos} has no tree key \"{key}\"."
        );
    return SetupValues.Normalize(value.GetValue());
  }

  public override string Describe => "tree " + key;
}

/// <summary><c>{ at, block: wildcard }</c>: whether the cell's block matches.</summary>
internal sealed class BlockReading(SetupPosition at, string wildcard)
  : Reading {
  private readonly AssetLocation _wildcard = new(wildcard);

  public override object Read(ReadingContext ctx) {
    Block block = ctx.World.BlockAt(at.Resolve(ctx.World));
    return block.Code != null && WildcardUtil.Match(_wildcard, block.Code);
  }

  public override string Describe => "block " + wildcard;
}

/// <summary><c>{ at, network: type, member?, or? }</c>: the network at the cell, its state's
/// member before its own, or whether the cell is a node of one.</summary>
internal sealed class NetworkReading(
  SetupPosition at,
  string type,
  string? member,
  object? or
) : Reading {
  private readonly string[]? _path = member?.Split('.');

  public override object Read(ReadingContext ctx) {
    BlockPos pos = at.Resolve(ctx.World);
    object? network = ctx.World.NetworkAt(pos, type);
    if (_path == null)
      return network != null;
    if (network == null)
      return or ?? throw new SetupReadingException($"No {type} network at {pos}.");
    object? state = SetupMembers.Has(network.GetType(), "State")
      ? SetupMembers.Walk(network, ["State"], null)
      : null;
    object root =
      state != null && SetupMembers.Has(state.GetType(), _path[0])
        ? state
        : network;
    return SetupValues.Normalize(
      SetupMembers.Walk(root, _path, ctx.World.World)
    );
  }

  public override string Describe => type + (member == null ? "" : " " + member);
}

/// <summary><c>{ static: "Type.Member" }</c>: a static field, property or constant, read
/// once.</summary>
internal sealed class StaticReading(string name) : Reading {
  private object? _value;

  public override object Read(ReadingContext ctx) => _value ??= Load();

  private object Load() {
    int dot = name.LastIndexOf('.');
    if (dot <= 0)
      throw new SetupReadingException($"\"{name}\" is not Type.Member.");
    string typeName = name[..dot];
    string memberName = name[(dot + 1)..];
    Type type = AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType(typeName))
        .FirstOrDefault(t => t != null)
      ?? throw new SetupReadingException($"No type \"{typeName}\" is loaded.");
    const BindingFlags Static =
      BindingFlags.Static
      | BindingFlags.Public
      | BindingFlags.NonPublic
      | BindingFlags.FlattenHierarchy;
    object? value = type.GetField(memberName, Static) is { } field
      ? field.GetValue(null)
      : type.GetProperty(memberName, Static) is { } property
        ? property.GetValue(null)
        : throw new SetupReadingException(
          $"{typeName} has no static \"{memberName}\"."
        );
    return SetupValues.Normalize(value);
  }

  public override string Describe => name;
}

/// <summary><c>{ value: v }</c>: a literal.</summary>
internal sealed class ValueReading(object value) : Reading {
  public object Value => value;

  public override object Read(ReadingContext ctx) => value;

  public override string Describe => SetupValues.Text(value);
}

/// <summary><c>sum</c>, <c>product</c>, <c>quotient</c> and <c>abs</c>, in single
/// precision.</summary>
internal sealed class ArithmeticReading(string op, IReadOnlyList<Reading> terms)
  : Reading {
  public override IEnumerable<Reading> Children => terms;

  public override object Read(ReadingContext ctx) {
    float[] v = [.. terms.Select(t => t.Number(ctx))];
    switch (op) {
      case "sum":
        return v.Sum();
      case "product":
        return v.Aggregate(1f, (a, b) => a * b);
      case "abs":
        return Math.Abs(v[0]);
      default:
        if (v[1] == 0f)
          throw new SetupReadingException(
            $"quotient of {terms[0].Describe} by {terms[1].Describe}, which is 0."
          );
        return v[0] / v[1];
    }
  }

  public override string Describe => op;
}

/// <summary><c>{ when, then, else }</c>: <c>then</c> while every test holds.</summary>
internal sealed class WhenReading(
  IReadOnlyList<ReadingTest> tests,
  Reading then,
  Reading otherwise
) : Reading {
  public override IEnumerable<Reading> Children =>
    tests.SelectMany(t => t.Readings).Append(then).Append(otherwise);

  public override object Read(ReadingContext ctx) =>
    tests.All(t => t.Holds(ctx)) ? then.Read(ctx) : otherwise.Read(ctx);
}

/// <summary><c>{ words: [...] }</c>: the first rule's word whose test holds, else the last
/// entry's string form.</summary>
internal sealed class WordsReading(
  IReadOnlyList<(ReadingTest Test, string Word)> rules,
  Reading last,
  bool lower
) : Reading {
  public override IEnumerable<Reading> Children =>
    rules.SelectMany(r => r.Test.Readings).Append(last);

  public override object Read(ReadingContext ctx) {
    foreach (var (test, word) in rules)
      if (test.Holds(ctx))
        return word;
    string text = SetupValues.Text(last.Read(ctx));
    return lower ? text.ToLowerInvariant() : text;
  }
}

/// <summary><c>delta</c> and <c>rise</c>: the reading less its value at the last sample, at
/// second 0 less its value after the build; a <c>rise</c> below that value reads as
/// itself.</summary>
internal sealed class ChangeReading(Reading inner, bool rise) : Reading {
  private float _last;
  private int _second = -1;
  private float _change;

  public override IEnumerable<Reading> Children => [inner];

  public override void Start(ReadingContext ctx) {
    base.Start(ctx);
    _last = inner.Number(ctx);
    _second = -1;
  }

  public override object Read(ReadingContext ctx) {
    if (ctx.Second == _second)
      return _change;
    float now = inner.Number(ctx);
    _change = rise && now < _last ? now : now - _last;
    _last = now;
    _second = ctx.Second;
    return _change;
  }

  public override string Describe => (rise ? "rise " : "delta ") + inner.Describe;
}

/// <summary><c>{ before: r }</c>: the reading taken before the second's ticks.</summary>
internal sealed class BeforeReading(Reading inner) : Reading {
  private object? _value;
  private int _second = -2;

  public override IEnumerable<Reading> Children => [inner];

  public override void Before(ReadingContext ctx) {
    base.Before(ctx);
    _value = inner.Read(ctx);
    _second = ctx.Second;
  }

  public override object Read(ReadingContext ctx) {
    if (ctx.Phase == ReadingPhase.Start)
      return inner.Read(ctx);
    return _second == ctx.Second && _value != null
      ? _value
      : throw new SetupReadingException(
        $"before {inner.Describe} was not read before second {ctx.Second}."
      );
  }

  public override string Describe => "before " + inner.Describe;
}

/// <summary><c>{ series: "id.name" }</c>: a recorded series at the checked second.</summary>
internal sealed class SeriesReading(string id, string name) : Reading {
  public string Id => id;

  public string Name => name;

  public override object Read(ReadingContext ctx) =>
    (
      ctx.Recording
      ?? throw new SetupReadingException($"{Describe} needs a recording.")
    ).ValueAt(id, name, ctx.Second);

  public override string Describe => id + "." + name;
}

/// <summary>Whether every cell the named runs laid still holds a block.</summary>
internal sealed class IntactReading(IReadOnlyList<string> runs) : Reading {
  public override object Read(ReadingContext ctx) {
    foreach (string run in runs) {
      SetupPlacement placed = ctx.World.Placed(run)
        ?? throw new SetupReadingException($"\"{run}\" has placed nothing.");
      if (placed.Cells.Any(c => ctx.World.BlockAt(c).Id == 0))
        return false;
    }
    return true;
  }

  public override string Describe => "intact " + string.Join(",", runs);
}

/// <summary>A check's reading of the world, read each second of the run and kept, then
/// read back per second when the check runs.</summary>
internal sealed class SampledReading(Reading inner) : Reading {
  private readonly List<object> _values = [];

  public override IEnumerable<Reading> Children => [inner];

  /// <summary>Reads and keeps second <see cref="ReadingContext.Second"/>'s value; seconds are
  /// sampled in order from 0.</summary>
  public void Sample(ReadingContext ctx) {
    if (ctx.Second != _values.Count)
      throw new InvalidOperationException(
        $"{Describe} sampled at {ctx.Second} after {_values.Count} samples."
      );
    _values.Add(inner.Read(ctx));
  }

  public override object Read(ReadingContext ctx) =>
    ctx.Phase == ReadingPhase.Check
      ? ctx.Second < _values.Count
        ? _values[ctx.Second]
        : throw new SetupReadingException(
          $"{Describe} has no sample at {ctx.Second}."
        )
      : inner.Read(ctx);

  public override string Describe => inner.Describe;
}

/// <summary>Where a reading is parsed: the <c>at</c> it defaults to, and whether the
/// record-only and check-only forms are allowed.</summary>
internal sealed record ReadingScope(
  SetupPosition? DefaultAt,
  bool InRecord,
  bool InCheck,
  SetupReferences References
);

/// <summary>Parses the reading forms of setups.md, Readings.</summary>
internal static class SetupReadings {
  private static readonly string[] Forms =
  [
    "member",
    "tree",
    "block",
    "network",
    "static",
    "value",
    "sum",
    "product",
    "quotient",
    "abs",
    "when",
    "words",
    "delta",
    "rise",
    "before",
    "series",
  ];

  /// <summary>A reading; a bare string, number or boolean is a literal.</summary>
  /// <exception cref="SetupFileException">The reading names no form, two, an unknown key,
  /// or a form its scope refuses.</exception>
  public static Reading Parse(
    JToken? token,
    SetupJson.Where where,
    ReadingScope scope
  ) {
    Reading reading = ParseForm(token, where, scope);
    return
      scope.InCheck
      && reading is MemberReading or TreeReading or BlockReading or NetworkReading
      ? new SampledReading(reading)
      : reading;
  }

  private static Reading ParseForm(
    JToken? token,
    SetupJson.Where where,
    ReadingScope scope
  ) {
    if (token is JValue)
      return new ValueReading(SetupJson.Literal(token, where));
    JObject node = SetupJson.Object(token, where);
    string[] forms = [.. Forms.Where(f => node[f] != null)];
    if (forms.Contains("network"))
      forms = [.. forms.Where(f => f != "member")];
    if (forms.Length != 1)
      throw where.Fail(
        forms.Length == 0
          ? "names no reading form."
          : $"names two reading forms, \"{forms[0]}\" and \"{forms[1]}\"."
      );
    string form = forms[0];
    if (form is "delta" or "rise" or "before" && !scope.InRecord)
      throw where.Fail($"\"{form}\" is read in record only.");
    if (form == "series" && !scope.InCheck)
      throw where.Fail("\"series\" is read in checks only.");
    switch (form) {
      case "member": {
        SetupJson.RefuseUnknown(node, where, "at", "member", "or");
        return new MemberReading(
          At(node, where, scope),
          SetupJson.String(node["member"], where.In("member")),
          Or(node, where)
        );
      }
      case "tree": {
        SetupJson.RefuseUnknown(node, where, "at", "tree", "or");
        return new TreeReading(
          At(node, where, scope),
          SetupJson.String(node["tree"], where.In("tree")),
          Or(node, where)
        );
      }
      case "block": {
        SetupJson.RefuseUnknown(node, where, "at", "block");
        return new BlockReading(
          At(node, where, scope),
          SetupJson.String(node["block"], where.In("block"))
        );
      }
      case "network": {
        SetupJson.RefuseUnknown(node, where, "at", "network", "member", "or");
        string? member = node["member"] == null
          ? null
          : SetupJson.String(node["member"], where.In("member"));
        if (member == null && node["or"] != null)
          throw where.In("or").Fail("takes a \"member\".");
        return new NetworkReading(
          At(node, where, scope),
          SetupJson.String(node["network"], where.In("network")),
          member,
          Or(node, where)
        );
      }
      case "static": {
        SetupJson.RefuseUnknown(node, where, "static");
        return new StaticReading(
          SetupJson.String(node["static"], where.In("static"))
        );
      }
      case "value": {
        SetupJson.RefuseUnknown(node, where, "value");
        return new ValueReading(
          SetupJson.Literal(node["value"], where.In("value"))
        );
      }
      case "sum" or "product": {
        SetupJson.RefuseUnknown(node, where, form);
        return new ArithmeticReading(form, Many(node[form], where.In(form), scope));
      }
      case "quotient": {
        SetupJson.RefuseUnknown(node, where, form);
        IReadOnlyList<Reading> terms = Many(node[form], where.In(form), scope);
        if (terms.Count != 2)
          throw where.In(form).Fail("takes two readings.");
        return new ArithmeticReading(form, terms);
      }
      case "abs": {
        SetupJson.RefuseUnknown(node, where, form);
        return new ArithmeticReading(
          form,
          [Parse(node[form], where.In(form), scope)]
        );
      }
      case "when": {
        SetupJson.RefuseUnknown(node, where, "when", "then", "else");
        if (node["then"] == null)
          throw where.Fail("\"when\" takes a \"then\".");
        return new WhenReading(
          ReadingTests.ParseList(node["when"], where.In("when"), scope),
          Parse(node["then"], where.In("then"), scope),
          node["else"] == null
            ? new ValueReading(0f)
            : Parse(node["else"], where.In("else"), scope)
        );
      }
      case "words":
        return Words(node, where, scope);
      case "delta" or "rise": {
        SetupJson.RefuseUnknown(node, where, form);
        return new ChangeReading(
          Parse(node[form], where.In(form), scope),
          rise: form == "rise"
        );
      }
      case "before": {
        SetupJson.RefuseUnknown(node, where, form);
        return new BeforeReading(Parse(node[form], where.In(form), scope));
      }
      default: {
        SetupJson.RefuseUnknown(node, where, "series");
        string series = SetupJson.String(node["series"], where.In("series"));
        int dot = series.IndexOf('.');
        if (dot <= 0 || dot == series.Length - 1)
          throw where.In("series").Fail($"\"{series}\" is not <id>.<series>.");
        var reading = new SeriesReading(series[..dot], series[(dot + 1)..]);
        scope.References.Series(reading, where.In("series"));
        return reading;
      }
    }
  }

  private static Reading Words(
    JObject node,
    SetupJson.Where where,
    ReadingScope scope
  ) {
    SetupJson.RefuseUnknown(node, where, "words");
    JArray entries = SetupJson.Array(node["words"], where.In("words"));
    if (entries.Count == 0)
      throw where.In("words").Fail("takes a last word.");
    var rules = new List<(ReadingTest, string)>();
    for (int i = 0; i < entries.Count - 1; i++) {
      SetupJson.Where at = where.In("words").At(i);
      JObject rule = SetupJson.Object(entries[i], at);
      SetupJson.RefuseUnknown(rule, at, "when", "word");
      rules.Add(
        (
          ReadingTests.Parse(rule["when"], at.In("when"), scope),
          SetupJson.String(rule["word"], at.In("word"))
        )
      );
    }
    SetupJson.Where lastAt = where.In("words").At(entries.Count - 1);
    JToken last = entries[^1];
    bool lower = false;
    if (last is JObject o && o["lower"] != null) {
      lower = SetupJson.Bool(o["lower"], lastAt.In("lower"));
      last = (JObject)o.DeepClone();
      ((JObject)last).Remove("lower");
    }
    return new WordsReading(rules, Parse(last, lastAt, scope), lower);
  }

  private static IReadOnlyList<Reading> Many(
    JToken? token,
    SetupJson.Where where,
    ReadingScope scope
  ) =>
    [
      .. SetupJson
        .Array(token, where)
        .Select((t, i) => Parse(t, where.At(i), scope)),
    ];

  private static SetupPosition At(
    JObject node,
    SetupJson.Where where,
    ReadingScope scope
  ) {
    if (node["at"] == null)
      return scope.DefaultAt ?? throw where.Fail("takes an \"at\".");
    SetupPosition at = SetupPosition.Parse(node["at"], where.In("at"));
    scope.References.Position(at, where.In("at"));
    return at;
  }

  private static object? Or(JObject node, SetupJson.Where where) =>
    node["or"] == null ? null : SetupJson.Literal(node["or"], where.In("or"));
}
