using System.Collections.Generic;
using ExpandedLib.Industry.Machining;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Tests.Machining;

internal static class MachiningTestData {
  public const string Materials = """
    {
      "schema": 1,
      "handPower": { "rotary": 61.0, "reciprocating": 44.7 },
      "materials": [
        { "code": "castiron", "density": 7200,
          "energy": { "rotary": 185800, "reciprocating": 295500 } },
        { "code": "wroughtiron", "density": 7810,
          "energy": { "rotary": 193500, "reciprocating": 307800 } }
      ]
    }
    """;

  public static CuttingMaterialRegistry MaterialRegistry(params string[] files) {
    var registry = new CuttingMaterialRegistry();
    var sources = new List<(string, string)>();
    foreach (string json in files)
      sources.Add(
        ($"test:config/cuttingmaterials/f{sources.Count}.json", json)
      );
    CuttingMaterialLoader.Load(sources, registry);
    return registry;
  }

  public static MachiningJobRegistry JobRegistry(params string[] files) {
    var registry = new MachiningJobRegistry();
    var sources = new List<(string, string)>();
    foreach (string json in files)
      sources.Add(($"test:config/machiningjobs/f{sources.Count}.json", json));
    MachiningJobLoader.Load(sources, registry);
    return registry;
  }

  public static MachiningJob Job(
    string step,
    string input,
    string output,
    int count = 1,
    string? material = null
  ) =>
    new(
      step,
      input,
      output,
      count,
      material,
      null,
      null,
      0,
      1,
      null,
      [],
      [],
      null
    );

  public static MachiningJobSet Set(params MachiningJob[] jobs) => new(1, jobs);

  public static Item Collectible(
    string code,
    int? units,
    string? material = null
  ) {
    var attributes = new JObject();
    if (units is { } u)
      attributes[MachiningJob.UnitsAttribute] = u;
    if (material is not null)
      attributes[MachiningJob.MaterialAttribute] = material;
    return new Item {
      Code = new AssetLocation(code),
      Attributes = new JsonObject(attributes),
    };
  }
}
