using System.Collections.Generic;
using ExpandedLib.Industry.Casting;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Tests.Casting;

internal static class CastingTestData {
  public const string TestFlaskFile = """
    {
      "schema": 1,
      "stations": [
        { "code": "testflask", "moulds": ["open"],
          "sand": { "base": "test:casting/base", "half": "test:casting/half" } }
      ]
    }
    """;

  public static CastingStationRegistry Registry(params string[] files) {
    var registry = new CastingStationRegistry();
    var sources = new List<(string, string)>();
    foreach (string json in files)
      sources.Add(($"test:config/castingstations/f{sources.Count}.json", json));
    CastingStationLoader.Load(sources, registry);
    return registry;
  }

  public static JsonObject Mold(string station, string extra = "") =>
    new(
      JToken.Parse(
        $$"""
        {
          "output": { "type": "item", "code": "game:ingot-iron" },
          "units": 100,
          "stations": [
            { "size": "{{station}}", "shape": "test:casting/piece", "pieces": 1,
              "cavity": [ { "x1": 0, "y1": 0, "z1": 0, "x2": 1, "y2": 1, "z2": 1 } ]{{extra}} }
          ]
        }
        """
      )
    );

  public static Item Collectible(string code, JsonObject? mold) =>
    new() {
      Code = new AssetLocation(code),
      Attributes = mold is null
        ? new JsonObject(JToken.Parse("{}"))
        : new JsonObject(new JObject { ["mold"] = mold.Token }),
    };
}
