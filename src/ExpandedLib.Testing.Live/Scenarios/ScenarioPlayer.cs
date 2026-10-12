using System;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Server;

namespace ExpandedLib.Testing.Live;

/// <summary>The server-side survival player a run's cases act through, built from the engine's own
/// classes with no connection (scenarios.md, The scenario player).</summary>
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
    )
      return null;
    ScenarioPlayer made;
    try {
      made = Build(server, row, systems!, inventory!);
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
          return made._data.GameMode;
        },
        m => m == EnumGameMode.Survival,
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
    Type inventory
  ) {
    BlockPos park = ParkCell(row);
    ServerWorldPlayerData data = ServerWorldPlayerData.CreateNew(Name, Uid);
    data.Init(server);
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

  private void MoveTo(BlockPos at) {
    _data.EntityPlayer.ServerPos.SetPos(at.X + 0.5, at.Y, at.Z + 0.5);
    _data.EntityPlayer.Pos.SetFrom(_data.EntityPlayer.ServerPos);
  }
}
