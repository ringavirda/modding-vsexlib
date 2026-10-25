using System.IO;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Tests;

/// <summary>A block of the turning fixture shape (a still Bearing and a turning Shaft) carrying
/// <see cref="BEBehaviorTurningParts"/>, stood on a client twin.</summary>
internal static class TurningFixtures {
  private static readonly string Assets = Path.Combine(
    RepoPaths.Root,
    "tests",
    "ExpandedLib.Tests",
    "Fixtures",
    "turning"
  );

  public sealed class TurnEntity : BlockEntity { }

  public static TestWorld NewWorld() =>
    new TestWorld()
      .ClientAssets("turnfix", Assets)
      .RegisterClass("test-turn", typeof(TurnEntity))
      .RegisterClass("TurningParts", typeof(BEBehaviorTurningParts));

  /// <summary>Defines the block <paramref name="code"/> on the fixture shape; <paramref name="properties"/> is the behavior's JSON, the default shape-wide part when null.</summary>
  public static Block Define(
    TestWorld world,
    string code,
    string? properties = null,
    int rotateY = 0
  ) {
    ExBlockDef def = ExBlockDef
      .Create("turnfix", code)
      .Shape("turnfix:bearingshaft")
      .EntityClass("test-turn");
    def =
      properties == null
        ? def.EntityBehavior("TurningParts")
        : def.EntityBehavior("TurningParts", JObject.Parse(properties));
    Block block = world.DefineBlock(def, DefinitionCodes.Expand(def).First());
    block.Shape.rotateY = rotateY;
    return block;
  }

  public const string ShaftPart =
    "{ \"parts\": [ { \"elements\": [\"Shaft\"] } ] }";

  public static ClientTwin Stand(TestWorld world, Block block, BlockPos pos) {
    world.Accessor.SetBlock(block.Id, pos);
    return ClientTwin.Stand(world, world.GetBlockEntity(pos)!)[0];
  }

  public static BEBehaviorTurningParts Behavior(ClientTwin twin) =>
    twin.Twin.GetBehavior<BEBehaviorTurningParts>()!;

  /// <summary>Names the run of slot 0 on the server entity and carries it to the twin.</summary>
  public static void NameRun(ClientTwin twin, BlockPos anchor, float ratio = 1f) {
    twin.Server.GetBehavior<BEBehaviorTurningParts>()!
      .SetRun(0, new ExpandedLib.Networks.NodeKey(anchor, 0), ratio);
    twin.Sync();
  }

  public static MpRunAnglePacket Packet(
    BlockPos anchor,
    double angle,
    float speed
  ) =>
    new() {
      X = [anchor.X],
      Y = [anchor.Y],
      Z = [anchor.Z],
      Slot = [0],
      Angle = [angle],
      Speed = [speed],
    };

  public static MpRunAngles Angles(TestWorld world) =>
    MpRunAngles.For(world.ClientApi);
}
