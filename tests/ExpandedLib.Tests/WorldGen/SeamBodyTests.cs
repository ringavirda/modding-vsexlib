#if GAME_GE_1_22
using System;
using ExpandedLib.WorldGen;
using Newtonsoft.Json;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.WorldGen;

public class SeamBodyTests
{
  private static SeamSpec Spec(
    SeamAnchor anchor = SeamAnchor.Sealevel,
    float run = 100
  ) =>
    new()
    {
      Anchor = anchor,
      Footprint = new SeamFootprintSpec
      {
        Run = NatFloat.createUniform(run, 20),
        Strike = NatFloat.createUniform(60, 10),
      },
      Dip = NatFloat.createUniform(0.3f, 0.1f),
      YCropRel = NatFloat.createUniform(1.1f, 0.05f),
      Depth = NatFloat.createUniform(26, 14),
      Layers =
      [
        new SeamLayer { Thickness = NatFloat.createUniform(3, 1) },
        new SeamLayer
        {
          Thickness = NatFloat.createUniform(2, 0.5f),
          Outcrop = new SeamOutcrop { Depth = NatFloat.createUniform(4, 1) },
        },
      ],
    };

  // Fails when two draws are swapped (strike before run).
  [Fact]
  public void A_body_equals_the_hand_drawn_values_in_the_documented_order()
  {
    var spec = Spec();
    var hand = new LCGRandom(4242);
    float run = spec.Footprint.Run.nextFloat(1, hand);
    float strike = spec.Footprint.Strike.nextFloat(1, hand);
    float direction = hand.NextFloat() * MathF.Tau;
    float dip = spec.Dip.nextFloat(1, hand);
    float anchor = spec.YCropRel.nextFloat(1, hand);
    float t0 = spec.Layers[0].Thickness.nextFloat(1, hand);
    float t1 = spec.Layers[1].Thickness.nextFloat(1, hand);
    float o1 = spec.Layers[1].Outcrop!.Depth.nextFloat(1, hand);
    int seed = hand.NextInt();

    var body = SeamBody.Draw(spec, new LCGRandom(4242));

    Assert.Equal(run, body.Run);
    Assert.Equal(strike, body.Strike);
    Assert.Equal(direction, body.DipDirection);
    Assert.Equal(dip, body.Dip);
    Assert.Equal(anchor, body.Anchor);
    Assert.Equal([t0, t1], body.Thicknesses);
    Assert.Equal([null, (float?)o1], body.OutcropDepths);
    Assert.Equal(seed, body.Seed);
  }

  // Fails when a surface anchor still draws yCropRel: the depth is then drawn from the wrong value.
  [Fact]
  public void A_surface_body_draws_its_depth_and_not_yCropRel()
  {
    var spec = Spec(SeamAnchor.Surface);
    var hand = new LCGRandom(77);
    spec.Footprint.Run.nextFloat(1, hand);
    spec.Footprint.Strike.nextFloat(1, hand);
    hand.NextFloat();
    spec.Dip.nextFloat(1, hand);
    float depth = spec.Depth.nextFloat(1, hand);

    var body = SeamBody.Draw(spec, new LCGRandom(77));

    Assert.Equal(depth, body.Anchor);
  }

  // Fails when the run and strike are not capped at 240.
  [Fact]
  public void A_run_over_the_reach_is_capped()
  {
    var body = SeamBody.Draw(Spec(run: 400), new LCGRandom(5));

    Assert.Equal(240f, body.Run);
    Assert.Equal(240f, body.Footprint.DistanceDown(0) * 2);
  }

  // Fails when a field loses its JsonProperty or the file is read without strict members.
  [Fact]
  public void The_ironstone_example_loads_strictly_with_its_defaults()
  {
    const string json = """
      { "inBlock": { "code": "game:rock-*", "name": "rock", "allowedVariants": ["shale"] },
        "footprint": { "run": { "dist": "uniform", "avg": 160, "var": 0 },
                       "strike": { "dist": "uniform", "avg": 96, "var": 0 },
                       "exponent": 4, "taperFull": 0.6, "taperZero": 1.0 },
        "dip": { "dist": "uniform", "avg": 0.3, "var": 0 },
        "anchor": "sealevel",
        "yCropRel": { "dist": "uniform", "avg": 1.115, "var": 0.065 },
        "propickDepth": 40,
        "layers": [ { "block": "iiex:seamore-ironstone",
                      "thickness": { "dist": "uniform", "avg": 3, "var": 1 },
                      "surfaceBlock": "iiex:looseore-ironstone-free", "surfaceBlockChance": 0.08,
                      "outcrop": { "block": "game:ore-{grade}-limonite-{rock}", "grades": ["poor", "medium"],
                                   "depth": { "dist": "uniform", "avg": 3, "var": 0 } } } ],
        "worldScale": { "code": "iiexBulkOre", "values": { "off": 0, "rich": 2 } },
        "provinceChance": { "basin": 1 } }
      """;
    var strict = new JsonSerializerSettings
    {
      MissingMemberHandling = MissingMemberHandling.Error,
    };

    var spec = JsonConvert.DeserializeObject<SeamSpec>(json, strict)!;

    Assert.Equal(4f, spec.Footprint.Exponent);
    Assert.Equal(160f, spec.Footprint.Run.avg);
    Assert.Equal(SeamAnchor.Sealevel, spec.Anchor);
    Assert.Equal(40, spec.PropickDepth);
    Assert.Equal(0, spec.MinCover);
    var layer = Assert.Single(spec.Layers);
    Assert.True(layer.Propick);
    Assert.Equal(0.08f, layer.SurfaceBlockChance);
    Assert.Equal(["poor", "medium"], layer.Outcrop!.Grades);
    Assert.Equal(2f, spec.WorldScale!.Values["rich"]);
    Assert.Equal(1f, spec.ProvinceChance!["basin"]);
    Assert.Throws<JsonSerializationException>(() =>
      JsonConvert.DeserializeObject<SeamSpec>("""{ "taperfull": 1 }""", strict)
    );
  }
}
#endif
