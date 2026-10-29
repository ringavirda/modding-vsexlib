#if GAME_GE_1_22
using Vintagestory.GameContent;

namespace ExpandedLib.Testing;

/// <summary>The weather system a scene registers in place of the game's: no rain falls anywhere,
/// so a lit block entity that asks <c>GetPrecipitation</c> on its tick (a firepit's
/// <c>BEBehaviorTemperatureSensitive</c>) reads zero (setups.md, Loading).</summary>
internal sealed class SetupDryWeather : WeatherSystemBase
{
  public override float? OverridePrecipitation
  {
    get => 0f;
    set { }
  }
}
#endif
