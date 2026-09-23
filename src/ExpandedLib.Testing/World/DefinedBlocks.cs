using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Definitions;
using ExpandedLib.Registries;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.Common;
using Vintagestory.ServerMods.NoObf;

namespace ExpandedLib.Testing;

/// <summary>
/// Code-first definitions stood up as the blocks the game registers from them: a real class
/// registry, each variant's JSON resolved the way the object loader resolves it, and the block built
/// by vanilla's own <see cref="BlockType.CreateBlock"/>.
/// </summary>
public sealed partial class TestWorld {
  /// <summary>Block ids <see cref="DefineBlock"/> hands out, clear of the ids fixtures and
  /// <see cref="StructureRig"/> use.</summary>
  private const int FirstDefinedId = 40000;

  private ClassRegistry? _classes;
  private int _nextDefinedId = FirstDefinedId;

  /// <summary>Every block entity in the store, loaded chunk or not.</summary>
  internal IEnumerable<KeyValuePair<BlockPos, BlockEntity>> BlockEntities =>
    _blockEntities;

  /// <summary>
  /// Registers every <see cref="RegisterAttribute"/>-decorated class in
  /// <paramref name="assemblies"/> under the key the game registers it by, in a real class registry.
  /// A world holding that registry answers <see cref="Api"/>'s and <see cref="World"/>'s
  /// <c>ClassRegistry</c> from it, and
  /// creates a block entity the accessor spawns without a <see cref="RegisterBlockEntityFactory"/>
  /// factory from it, with its declared behaviours, as the engine does;
  /// <see cref="RegisterBlockEntityBehaviorFactory"/> has no effect on such a world.
  /// </summary>
  /// <returns>This world.</returns>
  public TestWorld RegisterClasses(params Assembly[] assemblies) {
    foreach (Assembly asm in assemblies) {
      string domain = EntityRegistry.DomainOf(asm, asm.GetName().Name ?? "");
      foreach (Type type in ReflectionScan.GetCandidateTypes(asm))
        if (
          IsRegistrable(type)
          && type.GetCustomAttributes().OfType<RegisterAttribute>().Any()
        )
          RegisterClass(EntityRegistry.KeyFor(domain, type), type);
    }
    return this;
  }

  /// <summary>
  /// Registers <paramref name="type"/> under <paramref name="key"/> in this world's real class
  /// registry, as a block, block entity, block behaviour or block-entity behaviour by its base type;
  /// the way to add a vanilla class (<c>"Animatable"</c>) a definition names.
  /// </summary>
  /// <exception cref="ArgumentException"><paramref name="type"/> is none of the four.</exception>
  /// <returns>This world.</returns>
  public TestWorld RegisterClass(string key, Type type) {
    ClassRegistry classes = Classes();
    if (typeof(Block).IsAssignableFrom(type))
      classes.RegisterBlockClass(key, type);
    else if (typeof(BlockEntity).IsAssignableFrom(type))
      classes.RegisterBlockEntityType(key, type);
    else if (typeof(BlockBehavior).IsAssignableFrom(type))
      classes.RegisterBlockBehaviorClass(key, type);
    else if (typeof(BlockEntityBehavior).IsAssignableFrom(type))
      classes.RegisterBlockEntityBehaviorClass(key, type);
    else
      throw new ArgumentException(
        $"{type.FullName} is not a block, block entity or behaviour class.",
        nameof(type)
      );
    return this;
  }

  /// <summary>
  /// The block the game registers for <paramref name="variant"/> of <paramref name="def"/>: its
  /// <c>*ByType</c> keys and <c>{group}</c> placeholders resolved for that variant, built through
  /// <see cref="BlockType.CreateBlock"/> against the classes <see cref="RegisterClasses"/> put in
  /// this world, given a fresh id and this world's api, registered, its <c>drops</c> resolved
  /// against this world's registries, and its <see cref="Block.OnLoaded"/> run.
  /// </summary>
  /// <param name="variant">One of <see cref="DefinitionCodes.Expand"/>'s results for
  /// <paramref name="def"/>.</param>
  /// <exception cref="InvalidOperationException"><see cref="RegisterClasses"/> has not run.</exception>
  public Block DefineBlock(ExBlockDef def, DefinitionCodes.Registered variant) {
    if (_classes == null)
      throw new InvalidOperationException(
        "DefineBlock builds through the class registry; call RegisterClasses first."
      );

    var code = new AssetLocation(variant.Code);
    var variants = new Vintagestory.API.Datastructures.OrderedDictionary<
      string,
      string
    >();
    foreach (var (key, value) in variant.Variants)
      variants[key] = value;

    var json = (JObject)def.ToJson().DeepClone();
    json.Remove("variantgroups");
    SolveByType.Invoke(null, [json, code.Path, variants]);

    var type = new BlockType();
    JsonUtil.PopulateObject(
      type,
      json,
      JsonUtil.CreateSerializerForDomain(code.Domain)
    );
    type.Code = code;
    type.Variant = variants;

    Block block = type.CreateBlock(Api);
    block.BlockId = _nextDefinedId++;
    Register(block);
    foreach (BlockDropItemStack drop in block.Drops ?? [])
      drop.Resolve(World, "DefineBlock", code);
    // The engine assigns the api when it registers the block; OnLoaded does not.
    ReflectionHelpers.SetField(block, "api", Api);
    block.OnLoaded(Api);
    return block;
  }

  private static bool IsRegistrable(Type type) =>
    typeof(Block).IsAssignableFrom(type)
    || typeof(BlockEntity).IsAssignableFrom(type)
    || typeof(BlockBehavior).IsAssignableFrom(type)
    || typeof(BlockEntityBehavior).IsAssignableFrom(type);

  /// <summary>Vanilla's per-variant resolver of <c>*ByType</c> keys and <c>{group}</c>
  /// placeholders, the one the object loader runs on every variant.</summary>
  private static readonly MethodInfo SolveByType =
    typeof(RegistryObjectType).GetMethod(
      "solveByType",
      BindingFlags.Static | BindingFlags.NonPublic
    )
    ?? throw new InvalidOperationException(
      "VSEssentials declares no RegistryObjectType.solveByType."
    );

  /// <summary>The real class registry, created and wired into <see cref="Api"/> on first use.</summary>
  private ClassRegistry Classes() {
    if (_classes != null)
      return _classes;
    _classes = new ClassRegistry();
    var registry = new ClassRegistryAPI(World, _classes);
    Api.ClassRegistry.Returns(registry);
    ((ICoreAPI)Api).ClassRegistry.Returns(registry);
    World.ClassRegistry.Returns(registry);
    return _classes;
  }

  /// <summary>A block entity of <paramref name="classname"/> with the behaviours
  /// <paramref name="block"/> declares, from the real class registry; null when none is set or it
  /// holds no such class.</summary>
  private BlockEntity? CreateRegisteredBlockEntity(
    string classname,
    Block block
  ) {
    if (_classes == null || Api.ClassRegistry.GetBlockEntity(classname) == null)
      return null;
    BlockEntity be = Api.ClassRegistry.CreateBlockEntity(classname);
    be.CreateBehaviors(block, World);
    return be;
  }
}
