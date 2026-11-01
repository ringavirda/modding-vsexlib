#if GAME_GE_1_22
using System.IO;
using ExpandedLib.WorldGen;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.ServerMods;

namespace ExpandedLib.Tests.WorldGen;

/// <summary>The example deposit files copied to the test output, and the blocks they name.</summary>
internal static class SeamFixtures
{
  public static DepositVariant Variant(string name)
  {
    string path = Path.Combine(
      System.AppContext.BaseDirectory,
      "WorldGen",
      "Fixtures",
      name + ".json"
    );
    DepositVariant variant = JsonConvert.DeserializeObject<DepositVariant[]>(
      File.ReadAllText(path)
    )![0];
    variant.fromFile = name + ".json";
    return variant;
  }

  /// <summary>Adds the rocks and layer blocks the fixtures name to the rig's world.</summary>
  public static void AddBlocks(SeamRig rig)
  {
    int id = 100;
    foreach (
      string rock in (string[])["sandstone", "conglomerate", "limestone"]
    )
    {
      rig.Add("game:rock-" + rock, id++, EnumBlockMaterial.Stone);
    }
    foreach (
      string code in (string[])
        [
          "iiex:ore-ooliticironstone",
          "iiex:ore-massivehematite",
          "iiex:ore-bituminouscoal",
          "iiex:nodules-clayironstone",
          "game:rawclay-fire-none",
        ]
    )
    {
      rig.Add(code, id++, EnumBlockMaterial.Ore);
    }
    foreach (
      string code in (string[])
        ["iiex:looseooliticironstone-free", "iiex:looseclayironstone-free"]
    )
    {
      rig.Add(code, id++, EnumBlockMaterial.Stone);
    }
    foreach (
      string rock in (string[])
        ["claystone", "shale", "sandstone", "conglomerate"]
    )
    {
      rig.Add(
        "game:looseores-bituminouscoal-" + rock + "-free",
        id++,
        EnumBlockMaterial.Stone
      );
      foreach (string grade in (string[])["poor", "medium"])
      {
        if (rock != "claystone")
        {
          rig.Add(
            "game:ore-" + grade + "-limonite-" + rock,
            id++,
            EnumBlockMaterial.Ore
          );
        }
      }
    }
  }

  public static SeamDepositGenerator Generator(
    SeamRig rig,
    DepositVariant variant,
    int seed = 1
  )
  {
    var generator = new SeamDepositGenerator(
      rig.World.Api,
      variant,
      new LCGRandom(seed),
      rig.Noise
    );
    generator.Init();
    return generator;
  }
}
#endif
