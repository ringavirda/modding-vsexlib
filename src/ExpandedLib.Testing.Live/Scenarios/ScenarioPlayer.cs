using System;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Server;

namespace ExpandedLib.Testing.Live;

/// <summary>The server-side player a run's cases act through, built from the engine's own classes
/// with no connection and marked connected so the engine reads its game mode (scenarios.md, The
/// scenario player).</summary>
internal sealed class ScenarioPlayer {
  public const string Uid = "exlib-scenario";
  private const string Name = "Scenario";
  private const int ParkHeight = 16;

  private readonly ServerMain _server;
  private readonly ServerWorldPlayerData _data;
  private readonly BlockPos _park;

  private ScenarioPlayer(
    ServerMain server,
    ServerWorldPlayerData data,
    ServerPlayer player,
    BlockPos park
  ) {
    _server = server;
    _data = data;
    _park = park;
    Player = player;
  }

  /// <summary>The engine's player the steps act through.</summary>
  public ServerPlayer Player { get; }

  /// <summary>The cell the player waits in between steps: <paramref name="row"/> lifted by 16 blocks.</summary>
  public static BlockPos ParkCell(BlockPos row) =>
    new(row.X, row.Y + ParkHeight, row.Z);

  /// <summary>Builds the player at the park cell above <paramref name="row"/> and runs its checks
  /// (scenarios.md, The scenario player), leaving it parked.</summary>
  /// <param name="server">The running server, from which the engine's internal system list and
  /// player maps are read.</param>
  /// <param name="row">The block the default spawn stands on.</param>
  /// <param name="failure">Null when every check holds; otherwise the first check's failure, as
  /// <c>&lt;check&gt;: read &lt;value&gt;</c> or <c>&lt;check&gt;: threw &lt;type&gt;: &lt;message&gt;</c>.</param>
  /// <returns>The player, which must be <see cref="Stop"/>ped, or null when the build threw. A player
  /// whose check failed is returned unparked.</returns>
  public static ScenarioPlayer? Start(
    ServerMain server,
    BlockPos row,
    out string? failure
  ) {
    if (
      !PlayerCheck.Read(
        "ServerMain.Systems",
        () =>
          typeof(ServerMain).GetField(
            "Systems",
            BindingFlags.Instance | BindingFlags.NonPublic
          ),
        f => f != null,
        out FieldInfo? systems,
        out failure
      )
      || !PlayerCheck.Read(
        "Vintagestory.Server.ServerSystemInventory",
        () =>
          typeof(ServerMain).Assembly.GetType(
            "Vintagestory.Server.ServerSystemInventory"
          ),
        t => t != null,
        out Type? inventory,
        out failure
      )
      || !PlayerCheck.Read(
        "ServerWorldPlayerData.connected",
        () =>
          typeof(ServerWorldPlayerData).GetField(
            "connected",
            BindingFlags.Instance | BindingFlags.NonPublic
          ),
        f => f != null,
        out FieldInfo? connected,
        out failure
      )
    )
      return null;
    ScenarioPlayer made;
    try {
      made = Build(server, row, systems!, inventory!, connected!);
    } catch (Exception e) {
      failure = $"build: threw {e.GetType().Name}: {e.Message}";
      return null;
    }
    BlockPos at = made._park;
    bool passed =
      PlayerCheck.Read(
        "PlayerByUid",
        () => ((IWorldAccessor)server).PlayerByUid(Uid),
        p => p == made.Player,
        out _,
        out failure
      )
      && PlayerCheck.Read(
        "Entity.Player",
        () => made._data.EntityPlayer.Player,
        p => p == made.Player,
        out _,
        out failure
      )
      && PlayerCheck.Read(
        "ActiveHotbarSlot",
        () => made.Player.InventoryManager.ActiveHotbarSlot,
        s => s != null,
        out _,
        out failure
      )
      && PlayerCheck.Read(
        "GameMode",
        () => {
          ((IWorldPlayerData)made._data).CurrentGameMode =
            EnumGameMode.Survival;
          return ((IWorldPlayerData)made._data).CurrentGameMode;
        },
        m => m == EnumGameMode.Survival,
        out _,
        out failure
      )
      && PlayerCheck.Read(
        "BuildAccess",
        () =>
          server.WorldMap.TestBlockAccess(
            made.Player,
            new BlockSelection { Position = at.Copy() },
            EnumBlockAccessFlags.BuildOrBreak
          ),
        r => r == EnumWorldAccessResponse.Granted,
        out _,
        out failure
      )
      && PlayerCheck.Read(
        "IsInInteractionRangeOf",
        () =>
          ((IPlayer)made.Player).IsInInteractionRangeOf(
            new BlockPos(at.X, at.Y - 1, at.Z)
          ),
        held => held,
        out _,
        out failure
      );
    if (passed)
      made.Park();
    return made;
  }

  /// <summary>Puts the player in creative mode with free movement at the park cell, where it neither
  /// falls, goes hungry nor picks up a drop.</summary>
  public void Park() {
    ((IWorldPlayerData)_data).CurrentGameMode = EnumGameMode.Creative;
    _data.FreeMove = true;
    MoveTo(_park);
  }

  /// <summary>The cell the player stands in to place into <paramref name="at"/>: two blocks south
  /// of it, on its level.</summary>
  public static Vec3d Stand(BlockPos at) => new(at.X + 0.5, at.Y, at.Z + 2.5);

  /// <summary>The point, relative to the clicked block, where a click on its <paramref name="face"/>
  /// lands: the middle of that face.</summary>
  public static Vec3d Hit(BlockFacing face) =>
    new(
      0.5 + 0.5 * face.Normali.X,
      0.5 + 0.5 * face.Normali.Y,
      0.5 + 0.5 * face.Normali.Z
    );

  /// <summary>The yaw and pitch, in radians, that turn a player at <paramref name="eye"/> toward
  /// <paramref name="target"/> in the engine's view convention (yaw 0 looks along -Z).</summary>
  public static (float Yaw, float Pitch) Look(Vec3d eye, Vec3d target) {
    double dx = target.X - eye.X;
    double dy = target.Y - eye.Y;
    double dz = target.Z - eye.Z;
    return (
      (float)Math.Atan2(-dx, -dz),
      (float)Math.Atan2(dy, Math.Sqrt(dx * dx + dz * dz))
    );
  }

  /// <summary>Places <paramref name="block"/> into <paramref name="at"/> the way a player's click on
  /// the <paramref name="face"/> of the block beside it does (<c>Block.TryPlaceBlock</c> with the
  /// active slot's stack, then the did-place event): moved into place in survival mode, the slot filled,
  /// then emptied and the player parked again.</summary>
  /// <param name="block">The block to hold and place.</param>
  /// <param name="at">The absolute cell the block goes into.</param>
  /// <param name="face">The face of the neighbouring block the click lands on.</param>
  /// <param name="failure">Null when the block was placed; otherwise the failure code the block gave.</param>
  /// <returns>Whether the block went down.</returns>
  public bool Place(
    Block block,
    BlockPos at,
    BlockFacing face,
    out string? failure
  ) {
    ((IWorldPlayerData)_data).CurrentGameMode = EnumGameMode.Survival;
    _data.FreeMove = false;
    Vec3d stand = Stand(at);
    MoveTo(stand);
    Vec3d hit = Hit(face);
    Vec3d eye = stand.AddCopy(_data.EntityPlayer.LocalEyePos);
    Vec3i n = face.Normali;
    (_data.EntityPlayer.ServerPos.Yaw, _data.EntityPlayer.ServerPos.Pitch) =
      Look(
        eye,
        new Vec3d(at.X - n.X + hit.X, at.Y - n.Y + hit.Y, at.Z - n.Z + hit.Z)
      );
    _data.EntityPlayer.Pos.SetFrom(_data.EntityPlayer.ServerPos);
    ItemSlot slot = Player.InventoryManager.ActiveHotbarSlot;
    slot.Itemstack = new ItemStack(block);
    try {
      var selection = new BlockSelection {
        Position = at.Copy(),
        Face = face,
        HitPosition = hit,
        DidOffset = true,
      };
      int before = _server.BlockAccessor.GetBlock(at).Id;
      string code = "";
      if (
        !block.TryPlaceBlock(
          _server,
          Player,
          slot.Itemstack,
          selection,
          ref code
        )
      ) {
        failure = code == "" ? "refused" : code;
        return false;
      }
      _server.TriggerNeighbourBlocksUpdate(at);
      _server.EventManager.TriggerDidPlaceBlock(
        Player,
        before,
        selection,
        slot.Itemstack
      );
      failure = null;
      return true;
    } finally {
      slot.Itemstack = null;
      Park();
    }
  }

  /// <summary>Despawns the player and removes it from the server's player maps.</summary>
  public void Stop() {
    _server.DespawnEntity(
      _data.EntityPlayer,
      new EntityDespawnData { Reason = EnumDespawnReason.Removed }
    );
    _server.PlayersByUid.Remove(Uid);
    _server.PlayerDataManager.WorldDataByUID.Remove(Uid);
    _server.PlayerDataManager.PlayerDataByUid.Remove(Uid);
  }

  private static ScenarioPlayer Build(
    ServerMain server,
    BlockPos row,
    FieldInfo systems,
    Type inventory,
    FieldInfo connected
  ) {
    BlockPos park = ParkCell(row);
    ServerWorldPlayerData data = ServerWorldPlayerData.CreateNew(Name, Uid);
    data.Init(server);
    connected.SetValue(data, true);
    data.EntityPlayer.Code = server
      .GetEntityType(GlobalConstants.EntityPlayerTypeCode)
      .Code;
    data.EntityPlayer.WatchedAttributes.SetString("playerUID", Uid);
    var player = new ServerPlayer(server, data);
    server.PlayerDataManager.WorldDataByUID[Uid] = data;
    server.PlayersByUid[Uid] = player;
    object join = ((Array)systems.GetValue(server)!)
      .Cast<object>()
      .Single(s => s.GetType() == inventory);
    inventory
      .GetMethod("OnPlayerJoin", [typeof(ServerPlayer)])!
      .Invoke(join, [player]);
    var made = new ScenarioPlayer(server, data, player, park);
    made.MoveTo(park);
    server.SpawnEntity(data.EntityPlayer);
    return made;
  }

  private void MoveTo(BlockPos at) =>
    MoveTo(new Vec3d(at.X + 0.5, at.Y, at.Z + 0.5));

  private void MoveTo(Vec3d at) {
    _data.EntityPlayer.ServerPos.SetPos(at.X, at.Y, at.Z);
    _data.EntityPlayer.Pos.SetFrom(_data.EntityPlayer.ServerPos);
  }
}
