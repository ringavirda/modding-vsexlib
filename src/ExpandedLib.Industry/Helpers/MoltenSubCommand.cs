using System.ComponentModel;
using ExpandedLib.Industry.Metals;
using ExpandedLib.Industry.Molten;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace ExpandedLib.Industry.Helpers;

/// <summary>
/// Attaches <c>/exmod molten &lt;metal&gt; &lt;units&gt; [temperature]</c>: fills the
/// <see cref="IMoltenFillable"/> the player holds, or else the <see cref="ILiquidMetalSink"/> the
/// player looks at, with molten metal. Server-only; a creative or op player.
/// </summary>
[SubCommandRegister(Side = EnumAppSide.Server)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class MoltenSubCommand : IExSubCommand {
  /// <summary>Degrees C above the melting point a fill is poured at when no temperature is given.</summary>
  internal const float TemperatureMargin = 100f;

  public string ParentName => "exmod";

  public void Register(ICoreAPI api, Mod mod, IChatCommand parent) {
    var parsers = api.ChatCommands.Parsers;

    parent
      .BeginSubCommand("molten")
      .WithDescription(Lang.Get("exlib:command-molten-desc"))
      .WithArgs(
        parsers.Word("metal"),
        parsers.Int("units"),
        parsers.OptionalFloat("temperature")
      )
      .HandleWith(args =>
        args.Caller?.Player is IServerPlayer player
          ? Dispatch(
            player,
            (string)args[0],
            (int)args[1],
            (float)args[2],
            args.LanguageCode
          )
          : TextCommandResult.Error(
            Lang.GetL(args.LanguageCode, "exlib:command-molten-player")
          )
      )
      .EndSubCommand();
  }

  /// <summary>The command's logic with fluent arg parsing stripped, callable directly by a test.</summary>
  /// <param name="player">The caller; refused unless creative or holding <c>controlserver</c>.</param>
  /// <param name="metalToken">A metal token (<c>iron</c>, <c>pigiron</c>) or a full ingot item code.</param>
  /// <param name="units">Units to pour; capped at what the target holds.</param>
  /// <param name="temperature">Pour temperature in deg C, or 0 or less for the melting point plus <see cref="TemperatureMargin"/>.</param>
  /// <param name="languageCode">Language of the reply.</param>
  internal static TextCommandResult Dispatch(
    IServerPlayer player,
    string metalToken,
    int units,
    float temperature,
    string languageCode
  ) {
    if (
      player.WorldData.CurrentGameMode != EnumGameMode.Creative
      && !player.HasPrivilege(Privilege.controlserver)
    )
      return Fail(languageCode, "exlib:command-molten-denied");
    if (units <= 0)
      return Fail(languageCode, "exlib:command-molten-units");

    IWorldAccessor world = player.Entity.World;
    string code = metalToken.Contains(':')
      ? metalToken
      : MetalRegistry.MoltenItemOf(metalToken).ToString();
    ItemStack? metal = MoltenMetal.CreateStack(world, code, 20f);
    if (metal == null || MoltenMetal.MeltingPointOf(world, metal) <= 0f)
      return Fail(languageCode, "exlib:command-molten-unknown", metalToken);
    if (temperature <= 0f)
      temperature =
        MoltenMetal.MeltingPointOf(world, metal) + TemperatureMargin;
    MoltenMetal.SetTemperature(world, metal, temperature);

    string name = MoltenMetal.DisplayName(code);
    int rounded = (int)temperature;
    ItemSlot slot = player.InventoryManager.ActiveHotbarSlot;
    if (slot.Itemstack?.Collectible is IMoltenFillable fillable) {
      int taken = fillable.FillHeld(world, slot.Itemstack, metal, units);
      if (taken <= 0)
        return Fail(languageCode, "exlib:command-molten-refused", name);
      slot.MarkDirty();
      return Done(
        languageCode,
        "exlib:command-molten-held",
        name,
        taken,
        rounded
      );
    }

    BlockSelection? sel = player.CurrentBlockSelection;
    if (
      sel == null
      || MoltenSinks.SinkAt(world, sel.Position, out Vec3i offset)
        is not { } sink
    )
      return Fail(languageCode, "exlib:command-molten-target");
    if (!sink.CanReceiveAny || !sink.CanReceive(metal))
      return Fail(languageCode, "exlib:command-molten-refused", name);

    sink.BeginFill(sel.HitPosition.AddCopy(offset.X, offset.Y, offset.Z));
    int left = units;
    sink.ReceiveLiquidMetal(metal, ref left, temperature);
    sink.OnPourOver();
    if (left >= units)
      return Fail(languageCode, "exlib:command-molten-refused", name);
    return Done(
      languageCode,
      "exlib:command-molten-sink",
      name,
      units - left,
      rounded
    );
  }

  private static TextCommandResult Fail(
    string languageCode,
    string key,
    params object[] args
  ) => TextCommandResult.Error(Lang.GetL(languageCode, key, args));

  private static TextCommandResult Done(
    string languageCode,
    string key,
    params object[] args
  ) => TextCommandResult.Success(Lang.GetL(languageCode, key, args));
}
