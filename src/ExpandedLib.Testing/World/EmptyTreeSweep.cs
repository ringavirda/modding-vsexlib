using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Blocks;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

/// <summary>
/// Loads each registered block entity type from a tree that holds none of its keys, as a save written
/// before the type's members existed does, on the server and on the client side.
/// </summary>
public static class EmptyTreeSweep {
  private const BindingFlags MemberFlags =
    BindingFlags.Instance
    | BindingFlags.Public
    | BindingFlags.NonPublic
    | BindingFlags.DeclaredOnly;

  private static readonly BlockPos Here = new(0, 64, 0);

  /// <summary>Every block entity type <see cref="EntityRegistry.RegisterAll"/> registers from
  /// <paramref name="assemblies"/>: the concrete <see cref="BlockEntity"/> classes carrying
  /// <see cref="BlockEntityRegisterAttribute"/>.</summary>
  /// <param name="assemblies">The assemblies a mod registers, for example its own and its modules'.</param>
  /// <returns>The types, ordered by assembly then full name.</returns>
  public static IReadOnlyList<Type> BlockEntityTypes(
    params Assembly[] assemblies
  ) =>
    [
      .. ReflectionScan
        .GetCandidateTypes(assemblies)
        .Where(t =>
          typeof(BlockEntity).IsAssignableFrom(t)
          && t.GetCustomAttribute<BlockEntityRegisterAttribute>() != null
        ),
    ];

  /// <summary>Creates <paramref name="type"/> in a fresh <see cref="TestWorld"/> on a plain block,
  /// loads it from an empty tree and initialises it on the server side, then loads and initialises a
  /// client twin of it from an empty tree.</summary>
  /// <remarks>A <see cref="PersistAttribute"/> member is compared with the value a new instance of
  /// <paramref name="type"/> holds, straight after each load and before any initialiser runs. A type
  /// that needs a neighbour, a multiblock or a network gets it from <paramref name="prepare"/>, which
  /// runs once the entity is placed and before the first load.</remarks>
  /// <param name="type">A <see cref="BlockEntity"/> type with a parameterless constructor.</param>
  /// <param name="prepare">Sets up what the type cannot stand without; null for nothing.</param>
  /// <returns>One line per failure, naming the step and the exception or the member; empty when the
  /// type loads and initialises cleanly on both sides.</returns>
  public static IReadOnlyList<string> Run(
    Type type,
    Action<TestWorld, BlockEntity>? prepare = null
  ) {
    var problems = new List<string>();
    using var world = new TestWorld();
    world.RegisterClasses(type.Assembly);
    BlockEntity? reference = Try(
      problems,
      "construct",
      () => (BlockEntity)Activator.CreateInstance(type)!
    );
    if (reference == null)
      return problems;

    string key = EntityRegistry.KeyFor(
      EntityRegistry.DomainOf(
        type.Assembly,
        type.Assembly.GetName().Name ?? ""
      ),
      type
    );
    Block block = TestBlocks.Configure(
      new Block(),
      "sweep:" + type.Name.ToLowerInvariant(),
      39000
    );
    block.EntityClass = key;
    var server = (BlockEntity)Activator.CreateInstance(type)!;
    world.Place(Here, block, server);
    if (Try(problems, "prepare", () => prepare?.Invoke(world, server)) == null)
      return problems;

    if (
      Try(
        problems,
        "server load",
        () => server.FromTreeAttributes(new TreeAttribute(), world.World)
      ) != null
    ) {
      CompareMembers(problems, "server load", type, server, reference);
      Try(problems, "server initialize", () => world.Initialize(server));
    }

    BlockEntity? twin = Try(
      problems,
      "client load",
      () => world.NewClientTwin(server, new TreeAttribute())
    );
    if (twin == null)
      return problems;
    CompareMembers(problems, "client load", type, twin, reference);
    Try(
      problems,
      "client initialize",
      () => world.InitializeClientTwins([twin])
    );
    return problems;
  }

  private static T? Try<T>(List<string> problems, string step, Func<T> act) {
    try {
      return act();
    } catch (Exception e) {
      string frames = string.Join(
        " <- ",
        (e.StackTrace ?? "")
          .Split('\n')
          .Take(3)
          .Select(f => f.Trim().Split(" in ")[0])
      );
      problems.Add($"{step}: {e.GetType().Name}: {e.Message} [{frames}]");
      return default;
    }
  }

  private static object? Try(List<string> problems, string step, Action act) =>
    Try<object?>(
      problems,
      step,
      () => {
        act();
        return true;
      }
    );

  private static void CompareMembers(
    List<string> problems,
    string step,
    Type type,
    BlockEntity loaded,
    BlockEntity reference
  ) {
    for (Type? t = type; t != null && t != typeof(object); t = t.BaseType) {
      foreach (MemberInfo member in t.GetMembers(MemberFlags)) {
        if (
          member is not (FieldInfo or PropertyInfo)
          || member.GetCustomAttribute<PersistAttribute>() == null
        )
          continue;
        object? got = Read(member, loaded);
        object? want = Read(member, reference);
        if (!SameValue(got, want))
          problems.Add(
            $"{step}: {t.Name}.{member.Name} is {Show(got)}, its initializer {Show(want)}"
          );
      }
    }
  }

  private static object? Read(MemberInfo member, object owner) =>
    member is FieldInfo f
      ? f.GetValue(owner)
      : ((PropertyInfo)member).GetValue(owner);

  private static bool SameValue(object? a, object? b) {
    if (a == null || b == null)
      return a == b;
    if (a is ItemStack sa && b is ItemStack sb)
      return sa.Collectible == sb.Collectible && sa.StackSize == sb.StackSize;
    if (a is IPersistable pa && b is IPersistable pb)
      return Saved(pa) == Saved(pb);
    if (a is not string && a is IEnumerable ea && b is IEnumerable eb)
      return ea.Cast<object?>().SequenceEqual(eb.Cast<object?>());
    return a.Equals(b);
  }

  private static string Saved(IPersistable value) {
    var tree = new TreeAttribute();
    value.ToTree(tree);
    return tree.ToJsonToken();
  }

  private static string Show(object? value) =>
    value is string s ? $"\"{s}\"" : value?.ToString() ?? "null";
}
