using System.Collections.Generic;
using System.Text;
using ExpandedLib.Registries;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Structures;

/// <summary>
/// Block entity for an invisible structure-filler block: carries the link to its principal block
/// and reroutes the HUD readout to it.
/// </summary>
[BlockEntityRegister]
public class BlockEntityStructureFiller : BlockEntity {
  /// <summary>The controller block this filler cell belongs to, or null if orphaned.</summary>
  public BlockPos? Principal { get; set; }

  /// <summary>Whether other blocks may attach to this cell. Defaults to false.</summary>
  public bool AllowAttach { get; set; }

  /// <summary>Per-cell collision/selection boxes, already rotated into the placed orientation, or null for a plain full cube.</summary>
  public Cuboidf[]? CollisionBoxes { get; set; }

  /// <summary>Single-char face code of the network port this cell exposes, or null for a plain filler.</summary>
  public string? PortFace { get; set; }

  /// <summary>Network type of the exposed port (e.g. "pipe"), or null when this cell has no port.</summary>
  public string? PortNetworkType { get; set; }

  /// <summary>Behaviours this cell hosts on the principal's behalf, or null for a plain filler.</summary>
  public FillerBehavior[]? HostedBehaviors { get; set; }

  /// <summary>The behaviour instances created from <see cref="HostedBehaviors"/>.</summary>
  private readonly List<BlockEntityBehavior> _hosted = [];

  // The declaration set _hosted was built from; guards against rebuilding on an unchanged declaration.
  private FillerBehavior[]? _appliedSpecs;

  // The most recent save/sync tree, kept so a hosted behaviour created late can still read it.
  private ITreeAttribute? _savedTree;

  private static JsonObject EmptyProps => new(new JObject());

  public override void Initialize(ICoreAPI api) {
    base.Initialize(api);
    ApplyHostedBehaviors();
  }

  /// <summary>Stores the cell's hosted-behaviour declarations and recreates them. Null clears any existing ones.</summary>
  public void SetHostedBehaviors(FillerBehavior[]? behaviors) {
    HostedBehaviors = behaviors is { Length: > 0 } ? behaviors : null;
    ApplyHostedBehaviors();
    MarkDirty(true);
  }

  /// <summary>Instantiates each declared behaviour by its registered class code and adds it to this BE.</summary>
  private void ApplyHostedBehaviors() {
    if (Api == null || ReferenceEquals(HostedBehaviors, _appliedSpecs))
      return;
    _appliedSpecs = HostedBehaviors;

    foreach (BlockEntityBehavior previous in _hosted) {
      // Told as a removal, not just dropped from the list, so a network membership deregisters its graph node.
      previous.OnBlockRemoved();
      Behaviors.Remove(previous);
    }
    _hosted.Clear();

    if (HostedBehaviors == null)
      return;

    foreach (FillerBehavior spec in HostedBehaviors) {
      BlockEntityBehavior? beh = Api.ClassRegistry.CreateBlockEntityBehavior(
        this,
        spec.Code
      );
      if (beh == null) {
        Api.Logger.Warning(
          "[exlib] StructureFiller at {0}: unknown hosted behaviour class '{1}'.",
          Pos,
          spec.Code
        );
        continue;
      }
      // Must run before Initialize.
      (beh as IFillerHostedBehavior)?.ConfigureFromFiller(
        Principal,
        spec.ConnectorFace,
        spec.Properties
      );
      Behaviors.Add(beh);
      _hosted.Add(beh);
      beh.Initialize(Api, spec.Properties ?? EmptyProps);
      // Client only: replays the loaded tree since the behaviour was created too late for FromTreeAttributes.
      if (Api.Side == EnumAppSide.Client && _savedTree != null)
        beh.FromTreeAttributes(_savedTree, Api.World);
    }
  }

  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    // (-1, -1, -1) is the "no principal" sentinel.
    tree.SetInt("cx", Principal?.X ?? -1);
    tree.SetInt("cy", Principal?.Y ?? -1);
    tree.SetInt("cz", Principal?.Z ?? -1);
    tree.SetBool("allowAttach", AllowAttach);
    if (CollisionBoxes is { Length: > 0 }) {
      var flat = new float[CollisionBoxes.Length * 6];
      for (int i = 0; i < CollisionBoxes.Length; i++) {
        Cuboidf b = CollisionBoxes[i];
        flat[i * 6 + 0] = b.X1;
        flat[i * 6 + 1] = b.Y1;
        flat[i * 6 + 2] = b.Z1;
        flat[i * 6 + 3] = b.X2;
        flat[i * 6 + 4] = b.Y2;
        flat[i * 6 + 5] = b.Z2;
      }
      tree["cboxes"] = new FloatArrayAttribute(flat);
    }
    if (PortFace != null && PortNetworkType != null) {
      tree.SetString("portFace", PortFace);
      tree.SetString("portNet", PortNetworkType);
    }
    if (HostedBehaviors is { Length: > 0 } hosted) {
      var bt = new TreeAttribute();
      bt.SetInt("n", hosted.Length);
      for (int i = 0; i < hosted.Length; i++) {
        bt.SetString($"c{i}", hosted[i].Code);
        bt.SetString($"f{i}", hosted[i].ConnectorFace?.Code ?? "");
        bt.SetString($"p{i}", hosted[i].Properties?.ToString() ?? "");
      }
      tree["hostedBehaviors"] = bt;
    }
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  ) {
    base.FromTreeAttributes(tree, worldForResolving);
    int cx = tree.GetInt("cx", -1);
    int cy = tree.GetInt("cy", -1);
    int cz = tree.GetInt("cz", -1);
    Principal =
      cx == -1 && cy == -1 && cz == -1 ? null : new BlockPos(cx, cy, cz);
    AllowAttach = tree.GetBool("allowAttach", false);
    CollisionBoxes = tree["cboxes"]
      is FloatArrayAttribute { value.Length: >= 6 } fa
      ? ReadFlatBoxes(fa.value)
      : null;
    PortFace = tree.GetString("portFace", null);
    PortNetworkType = tree.GetString("portNet", null);
    HostedBehaviors = tree["hostedBehaviors"] is ITreeAttribute bt
      ? ReadHostedBehaviors(bt)
      : null;
    // Kept so a behaviour created below, or in Initialize, can still read its state from it.
    _savedTree = tree;
    // Covers a sync update that first sets HostedBehaviors after Initialize already ran.
    if (Api != null && _hosted.Count == 0 && HostedBehaviors is { Length: > 0 })
      ApplyHostedBehaviors();
  }

  /// <summary>Rebuilds the hosted-behaviour specs from the save tree (faces stay rotated as stored).</summary>
  private static FillerBehavior[]? ReadHostedBehaviors(ITreeAttribute bt) {
    int n = bt.GetInt("n", 0);
    if (n <= 0)
      return null;
    var list = new List<FillerBehavior>(n);
    for (int i = 0; i < n; i++) {
      string code = bt.GetString($"c{i}", "");
      if (string.IsNullOrEmpty(code))
        continue;
      string faceCode = bt.GetString($"f{i}", "");
      string propsJson = bt.GetString($"p{i}", "");
      list.Add(
        new FillerBehavior(
          code,
          string.IsNullOrEmpty(faceCode)
            ? null
            : BlockFacing.FromCode(faceCode),
          string.IsNullOrEmpty(propsJson)
            ? null
            : new JsonObject(JToken.Parse(propsJson))
        )
      );
    }
    return list.Count > 0 ? [.. list] : null;
  }

  /// <summary>Rebuilds the cuboid array from the flattened 6-floats-per-box save form.</summary>
  private static Cuboidf[] ReadFlatBoxes(float[] flat) {
    int n = flat.Length / 6;
    var boxes = new Cuboidf[n];
    for (int i = 0; i < n; i++)
      boxes[i] = new Cuboidf(
        flat[i * 6 + 0],
        flat[i * 6 + 1],
        flat[i * 6 + 2],
        flat[i * 6 + 3],
        flat[i * 6 + 4],
        flat[i * 6 + 5]
      );
    return boxes;
  }

  /// <summary>Reroutes the HUD readout to the principal block entity.</summary>
  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb) {
    if (Principal == null)
      return;
    Api.World.BlockAccessor.GetBlockEntity(Principal)
      ?.GetBlockInfo(forPlayer, sb);
  }
}
