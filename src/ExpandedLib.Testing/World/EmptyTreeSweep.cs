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
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

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

  /// <summary>The blocks of <paramref name="loaded"/> whose <c>EntityClass</c> resolves to
  /// <paramref name="type"/>, the key <see cref="EntityRegistry.RegisterAll"/> registers it under.</summary>
  /// <param name="loaded">A world with the mod's assets loaded, for example by
  /// <see cref="TestWorld.LoadAsGame"/>.</param>
  /// <param name="type">A <see cref="BlockEntity"/> type.</param>
  /// <returns>The blocks in id order; empty when no loaded block names the type.</returns>
  public static IReadOnlyList<Block> BlocksOf(TestWorld loaded, Type type) {
    string key = KeyOf(type);
    return
    [
      .. loaded
        .World.Blocks.Where(b => b?.EntityClass == key)
        .OrderBy(b => b.Id),
    ];
  }

  /// <summary>Creates <paramref name="type"/> in a fresh <see cref="TestWorld"/> on
  /// <paramref name="block"/> or a plain block, loads it from an empty tree and initialises it on the
  /// server side, then loads and initialises a client twin of it from an empty tree.</summary>
  /// <remarks>A <see cref="PersistAttribute"/> member is compared with the value a new instance holds,
  /// straight after each load and before any initialiser runs. Given a block, the world holds
  /// vanilla's classes, the exlib assemblies' classes and a started <see cref="MechanicalPowerMod"/>,
  /// and the behaviours the block declares load from the same empty tree. Each Warning, Error and
  /// Fatal the world logs is a failure line and is not left for a
  /// <see cref="FailOnWarningsAttribute"/> check to report again.</remarks>
  /// <param name="type">A <see cref="BlockEntity"/> type with a parameterless constructor.</param>
  /// <param name="prepare">Runs once the entity is placed and before the first load, to set up a
  /// neighbour, a multiblock or a network; null for nothing.</param>
  /// <param name="block">A block from <see cref="BlocksOf"/>; null for a plain block.</param>
  /// <returns>One line per failure; empty when the type is clean on both sides.</returns>
  /// <exception cref="ArgumentNullException"><paramref name="type"/> is null.</exception>
  public static IReadOnlyList<string> Run(
    Type type,
    Action<TestWorld, BlockEntity>? prepare = null,
    Block? block = null
  ) {
    ArgumentNullException.ThrowIfNull(type);
    var problems = new List<string>();
    using var world = new TestWorld();
    RunSteps(world, problems, type, prepare, block);
    foreach ((EnumLogType kind, string message) in world.Log.Entries.ToList())
      if (kind is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal) {
        problems.Add($"log: {kind}: {message}");
        world.Log.Expect(kind, message);
      }
    return problems;
  }

  private static string KeyOf(Type type) =>
    EntityRegistry.KeyFor(type.Assembly.GetName().Name ?? "", type);

  private static void RunSteps(
    TestWorld world,
    List<string> problems,
    Type type,
    Action<TestWorld, BlockEntity>? prepare,
    Block? shipped
  ) {
    if (shipped != null) {
      world.RegisterVanillaClasses();
      world.RegisterClass("Animatable", typeof(BEBehaviorAnimatable));
      var power = new MechanicalPowerMod();
      world.Mods.Register(power);
      power.Start(world.Api);
      world.RegisterClasses([
        .. type
          .Assembly.GetReferencedAssemblies()
          .Where(n =>
            n.Name?.StartsWith("exlib", StringComparison.OrdinalIgnoreCase)
            == true
          )
          .Select(Assembly.Load),
      ]);
    }
    world.RegisterClasses(type.Assembly);
    BlockEntity? reference = Try(
      problems,
      "construct",
      () => (BlockEntity)Activator.CreateInstance(type)!
    );
    if (reference == null)
      return;

    Block block;
    if (shipped != null)
      block = shipped;
    else {
      block = TestBlocks.Configure(
        new Block(),
        "sweep:" + type.Name.ToLowerInvariant(),
        39000
      );
      block.EntityClass = KeyOf(type);
    }
    BlockEntity? server = Try(
      problems,
      "construct",
      () => world.NewBlockEntityLike(reference, block)
    );
    if (server == null)
      return;
    world.Place(Here, block, server);
    if (Try(problems, "prepare", () => prepare?.Invoke(world, server)) == null)
      return;

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
      return;
    CompareMembers(problems, "client load", type, twin, reference);
    Try(
      problems,
      "client initialize",
      () => world.InitializeClientTwins([twin])
    );
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
