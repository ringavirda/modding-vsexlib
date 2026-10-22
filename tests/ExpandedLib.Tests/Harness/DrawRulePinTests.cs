#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Pins the engine rule <c>DrawnCell</c> re-encodes: which game methods call
/// <c>BlockEntity.OnTesselation</c>, what each does with the answer, and which draw types reach them. A
/// game update that moves any of it fails here, and the message gives the <c>ilspycmd</c> command that
/// re-reads the method.</summary>
public class DrawRulePinTests
{
  private const string Namespace = "Vintagestory.Client.NoObf.";

  private static readonly string Golden = Path.Combine(
    RepoPaths.Root,
    "tests",
    "ExpandedLib.Tests",
    "Harness",
    "draw-rule-pin.txt"
  );

  private static readonly string[] Callers =
  {
    Namespace + "JsonTesselator::Tesselate",
    Namespace + "BleedingJsonTesselator::Tesselate",
  };

  private static readonly Dictionary<int, string> DrawTypes = new()
  {
    [8] = Namespace + "JsonTesselator",
    [16] = Namespace + "JsonAndLiquidTesselator",
    [17] = Namespace + "JsonAndSnowLayerTesselator",
  };

  private static readonly Dictionary<short, OpCode> OpCodeTable =
    typeof(OpCodes)
      .GetFields(BindingFlags.Public | BindingFlags.Static)
      .Select(f => (OpCode)f.GetValue(null)!)
      .ToDictionary(o => o.Value);

  private static readonly Assembly GameLib = Type.GetType(
    Namespace + "ChunkTesselator, VintagestoryLib",
    throwOnError: true
  )!.Assembly;

  [Fact]
  public void Only_the_two_json_tesselators_call_OnTesselation()
  {
    var found = new SortedSet<string>(StringComparer.Ordinal);
    foreach (var method in AllMethods())
    {
      foreach (var (op, member) in Instructions(method))
      {
        if (
          (op == OpCodes.Call || op == OpCodes.Callvirt)
          && member is MethodBase { Name: "OnTesselation" } callee
          && callee.DeclaringType is { } owner
          && typeof(BlockEntity).IsAssignableFrom(owner)
        )
        {
          found.Add(Label(method));
        }
      }
    }

    Assert.True(
      found.SequenceEqual(Callers.OrderBy(c => c, StringComparer.Ordinal)),
      $"Callers of BlockEntity.OnTesselation in VintagestoryLib are now [{string.Join(", ", found)}]; "
        + $"DrawnCell re-encodes only [{string.Join(", ", Callers)}]. {Reread(found.FirstOrDefault())}"
    );
  }

  [Fact]
  public void Each_caller_keeps_the_instruction_sequence_the_rule_was_read_from()
  {
    var golden = File.ReadAllLines(Golden)
      .Where(l => l.Length > 0)
      .Select(l => l.Split('\t', 2))
      .ToDictionary(p => p[0], p => p[1]);

    foreach (var caller in Callers)
    {
      var method = AllMethods().Single(m => Label(m) == caller);
      Assert.True(
        golden.TryGetValue(caller, out var expected),
        $"{caller} has no golden line."
      );
      var actual = Fingerprint(method);
      Assert.True(
        expected == actual,
        $"{caller} no longer matches draw-rule-pin.txt. {Reread(caller)} Update DrawnCell and the golden "
          + $"together; the current fingerprint is:\n{actual}"
      );
    }
  }

  [Fact]
  public void The_chunk_tesselator_binds_the_json_draw_types()
  {
    var ctor = GameLib
      .GetType(Namespace + "ChunkTesselator", throwOnError: true)!
      .GetConstructors(
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
      )
      .Single(c => c.GetParameters().Length > 0);

    var bound = new Dictionary<int, string>();
    int? index = null;
    foreach (var (op, member, operand) in Walk(ctor))
    {
      if (TryConstant(op, operand, out var value))
      {
        index = value;
      }
      else if (op == OpCodes.Stelem_Ref)
      {
        index = null;
      }
      else if (
        op == OpCodes.Newobj
        && index is { } slot
        && member is ConstructorInfo { DeclaringType.Name: var name } made
        && name.EndsWith("Tesselator", StringComparison.Ordinal)
      )
      {
        bound[slot] = made.DeclaringType!.FullName!;
      }
    }

    foreach (var (slot, type) in DrawTypes)
    {
      Assert.True(
        bound.TryGetValue(slot, out var actual) && actual == type,
        $"ChunkTesselator binds draw type {slot} to {(bound.TryGetValue(slot, out var a) ? a : "nothing")}, "
          + $"expected {type}. {Reread("Vintagestory.Client.NoObf.ChunkTesselator::.ctor")}"
      );
    }
  }

  private static string Reread(string? caller)
  {
    var type = (caller ?? Namespace + "JsonTesselator").Split("::")[0];
    return $"Re-read it with: ilspycmd -t {type} \"{GameLib.Location}\"";
  }

  private static IEnumerable<MethodBase> AllMethods()
  {
    Type[] types;
    try
    {
      types = GameLib.GetTypes();
    }
    catch (ReflectionTypeLoadException e)
    {
      types = e.Types.Where(t => t != null).ToArray()!;
    }

    const BindingFlags all =
      BindingFlags.Instance
      | BindingFlags.Static
      | BindingFlags.Public
      | BindingFlags.NonPublic
      | BindingFlags.DeclaredOnly;
    foreach (var type in types)
    {
      foreach (var m in type.GetMethods(all))
        yield return m;
      foreach (var c in type.GetConstructors(all))
        yield return c;
    }
  }

  private static string Label(MethodBase method) =>
    $"{method.DeclaringType!.FullName}::{method.Name}";

  private static string Fingerprint(MethodBase method)
  {
    var text = new StringBuilder();
    foreach (var (op, member) in Instructions(method))
    {
      if (text.Length > 0)
        text.Append(' ');
      text.Append(op.Name);
      if (member != null)
        text.Append(' ').Append(MemberText(member));
    }
    return text.ToString();
  }

  private static string MemberText(MemberInfo member) =>
    member is Type type
      ? type.FullName ?? type.Name
      : $"{member.DeclaringType?.FullName}::{member.Name}";

  private static IEnumerable<(OpCode Op, MemberInfo? Member)> Instructions(
    MethodBase method
  ) => Walk(method).Select(i => (i.Op, i.Member));

  private static IEnumerable<(
    OpCode Op,
    MemberInfo? Member,
    long Operand
  )> Walk(MethodBase method)
  {
    var il = method.GetMethodBody()?.GetILAsByteArray();
    if (il == null)
      yield break;

    var typeArgs = method.DeclaringType!.IsGenericType
      ? method.DeclaringType.GetGenericArguments()
      : null;
    var methodArgs = method.IsGenericMethod
      ? method.GetGenericArguments()
      : null;
    var module = method.Module;
    var at = 0;
    while (at < il.Length)
    {
      short code = il[at++];
      if (code == 0xFE)
        code = (short)(0xFE00 | il[at++]);
      var op = OpCodeTable[code];
      MemberInfo? member = null;
      long operand = 0;
      switch (op.OperandType)
      {
        case OperandType.InlineNone:
          break;
        case OperandType.ShortInlineBrTarget:
        case OperandType.ShortInlineVar:
          at += 1;
          break;
        case OperandType.ShortInlineI:
          operand = (sbyte)il[at];
          at += 1;
          break;
        case OperandType.InlineVar:
          at += 2;
          break;
        case OperandType.InlineI:
          operand = BitConverter.ToInt32(il, at);
          at += 4;
          break;
        case OperandType.InlineI8:
        case OperandType.InlineR:
          at += 8;
          break;
        case OperandType.InlineSwitch:
          at += 4 + 4 * BitConverter.ToInt32(il, at);
          break;
        case OperandType.InlineMethod:
        case OperandType.InlineField:
        case OperandType.InlineType:
        case OperandType.InlineTok:
          try
          {
            member = module.ResolveMember(
              BitConverter.ToInt32(il, at),
              typeArgs,
              methodArgs
            );
          }
          catch (Exception e)
            when (e is ArgumentException or BadImageFormatException)
          {
            member = null;
          }
          at += 4;
          break;
        default:
          at += 4;
          break;
      }
      yield return (op, member, operand);
    }
  }

  private static bool TryConstant(OpCode op, long operand, out int value)
  {
    value = 0;
    if (op == OpCodes.Ldc_I4_S || op == OpCodes.Ldc_I4)
    {
      value = (int)operand;
      return true;
    }
    if (
      op.Value >= OpCodes.Ldc_I4_0.Value
      && op.Value <= OpCodes.Ldc_I4_8.Value
    )
    {
      value = op.Value - OpCodes.Ldc_I4_0.Value;
      return true;
    }
    return false;
  }
}
#endif
