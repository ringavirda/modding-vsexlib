#if GAME_GE_1_22
using System;
using System.IO;
using System.Linq;
using ExpandedLib.Catalogues;
using ExpandedLib.Industry.Machining;
using ExpandedLib.Testing;
using ExpandedLib.Tests.Machining;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="MachiningChecks"/> over a one-job world that is clean, then broken one way at a time.</summary>
public sealed class MachiningChecksTests : IDisposable
{
  private const string Recipe = "test:drill blank";

  private const string Kinds = """
    { "schema": 1, "kinds": [
      { "code": "test:big", "ratedSpeed": 4, "steps": { "drill":
        { "motion": "rotary", "power": 360, "tooling": "bit",
          "envelope": { "across": 12, "length": 48 } } } },
      { "code": "test:small", "ratedSpeed": 4, "steps": { "drill":
        { "motion": "rotary", "power": 360, "tooling": "bit",
          "envelope": { "across": 8, "length": 20 } } } }
    ] }
    """;

  private readonly string _shapes = Path.Combine(
    Path.GetTempPath(),
    "machiningchecks-" + Guid.NewGuid().ToString("N")
  );

  public void Dispose()
  {
    if (Directory.Exists(_shapes))
      Directory.Delete(_shapes, true);
  }

  private sealed record Rig(
    TestWorld World,
    MachiningJobRegistry Jobs,
    MachineKindRegistry Kinds,
    CuttingMaterialRegistry Materials,
    CraftSequenceRegistry Sequences
  );

  private static MachiningJob Job() =>
    MachiningTestData.Job(
      "drill",
      "test:blank",
      "test:holed",
      1,
      "castiron"
    ) with
    {
      Removed = 0.01f,
      Byproducts = [new MachiningByproduct("test:chips", 1)],
      Hand = new MachiningHand("workbench", Recipe),
    };

  private static CraftStep[] HandSteps(float seconds, int steps) =>
    [
      .. Enumerable
        .Range(0, steps)
        .Select(_ => new CraftStep(
          "test:handdrill",
          null,
          seconds,
          null,
          null,
          null
        )),
    ];

  private static Item Add(TestWorld world, string code, int units)
  {
    Item item = world.RegisterItem(code);
    item.Attributes = new JsonObject(
      new JObject { [MachiningJob.UnitsAttribute] = units }
    );
    return item;
  }

  // The job removes 0.01 kg of cast iron, 1858 J: the hand route of two 0.5 s steps holds 1.0 s
  // against 1.02 s, the machine's 0.17 s, and the piece (10 x 10 x 40) fits test:big.
  private System.Collections.Generic.List<string> Resolved = [];

  private Rig Build(
    System.Func<MachiningJob, MachiningJob>? job = null,
    string kinds = Kinds,
    CraftStep[]? steps = null,
    Action<GridRecipe>? recipe = null,
    int chips = 10,
    bool shape = true
  )
  {
    var world = new TestWorld();
    Item blank = Add(world, "test:blank", 100);
    Add(world, "test:holed", 90);
    Add(world, "test:chips", chips);
    Add(world, "test:other", 100);
    if (shape)
    {
      Directory.CreateDirectory(_shapes);
      File.WriteAllText(
        Path.Combine(_shapes, "blank.json"),
        """{ "elements": [ { "from": [0, 0, 0], "to": [10, 10, 40] } ] }"""
      );
      blank.Shape = new CompositeShape
      {
        Base = new AssetLocation("test:blank"),
      };
    }

    var grid = new GridRecipe
    {
      Name = new AssetLocation(Recipe),
      Width = 1,
      Height = 2,
      ResolvedIngredients =
      [
        new CraftingRecipeIngredient { Code = new AssetLocation("test:blank") },
        new CraftingRecipeIngredient
        {
          Code = new AssetLocation("test:handdrill"),
          IsTool = true,
        },
      ],
      Output = new CraftingRecipeIngredient
      {
        Code = new AssetLocation("test:holed"),
      },
    };
    recipe?.Invoke(grid);
    world.World.GridRecipes.Add(grid);

    var jobs = new MachiningJobRegistry();
    jobs.Contribute(MachiningTestData.Set((job ?? (j => j))(Job())));
    MachineKindRegistry registry = new();
    MachineKindLoader.Load(
      [("test:config/machinekinds/k.json", kinds)],
      registry
    );
    CuttingMaterialRegistry materials = MachiningTestData.MaterialRegistry(
      MachiningTestData.Materials
    );
    Resolved = MachiningJobs.Resolve(world.World.Collectibles, jobs, materials);
    var sequences = new CraftSequenceRegistry();
    sequences.Contribute(
      new CraftSequenceSet(
        1,
        "workbench",
        [new CraftSequence(Recipe, steps ?? HandSteps(0.5f, 2), [])]
      )
    );
    return new Rig(world, jobs, registry, materials, sequences);
  }

  private string Shape(string shapeBase) => Path.Combine(_shapes, "blank.json");

  [Fact]
  public void The_baseline_jobs_resolve_against_the_world()
  {
    Build();

    Assert.Empty(Resolved);
  }

  [Fact]
  public void A_job_a_kind_offers_is_offered()
  {
    Rig rig = Build();

    Assert.Empty(MachiningChecks.Offered(rig.Jobs, rig.Kinds));
  }

  // Fails when a job naming a hand route is still reported unoffered, or one with no route passes.
  [Fact]
  [PlantedDefect(typeof(MachiningChecks), nameof(MachiningChecks.Offered))]
  public void A_job_no_kind_offers_and_no_hand_route_covers_is_found()
  {
    Rig rig = Build(job: j => j with { Hand = null, Machines = ["test:none"] });

    string finding = Assert.Single(
      MachiningChecks.Offered(rig.Jobs, rig.Kinds)
    );

    Assert.Contains("drill 'test:blank'", finding);
  }

  [Fact]
  public void A_job_no_kind_offers_passes_with_a_hand_route()
  {
    Rig rig = Build(job: j => j with { Machines = ["test:none"] });

    Assert.Empty(MachiningChecks.Offered(rig.Jobs, rig.Kinds));
  }

  [Fact]
  public void A_hand_route_that_matches_its_job_has_no_findings()
  {
    Rig rig = Build();

    Assert.Empty(
      MachiningChecks.HandParity(
        rig.World,
        rig.Jobs,
        rig.Kinds,
        rig.Materials,
        rig.Sequences
      )
    );
  }

  // Fails when the check compares the output only: the recipe takes test:other, the job test:blank.
  [Fact]
  [PlantedDefect(typeof(MachiningChecks), nameof(MachiningChecks.HandParity))]
  public void A_hand_route_that_takes_another_input_is_found()
  {
    Rig rig = Build(recipe: r =>
      r.ResolvedIngredients![0] = new CraftingRecipeIngredient
      {
        Code = new AssetLocation("test:other"),
      }
    );

    string finding = Assert.Single(
      MachiningChecks.HandParity(
        rig.World,
        rig.Jobs,
        rig.Kinds,
        rig.Materials,
        rig.Sequences
      )
    );

    Assert.Contains("takes [test:other]", finding);
  }

  [Fact]
  public void A_hand_route_that_gives_another_output_is_found()
  {
    Rig rig = Build(recipe: r =>
      r.Output = new CraftingRecipeIngredient
      {
        Code = new AssetLocation("test:other"),
        StackSize = 2,
      }
    );

    string finding = Assert.Single(
      MachiningChecks.HandParity(
        rig.World,
        rig.Jobs,
        rig.Kinds,
        rig.Materials,
        rig.Sequences
      )
    );

    Assert.Contains("gives 2 x test:other", finding);
  }

  // Fails when the 10 % band is dropped or widened past 3 s: 3 s held against the energy figure's 1.02 s.
  [Fact]
  public void A_hand_route_held_far_off_the_energy_figure_is_found()
  {
    Rig rig = Build(steps: HandSteps(1.5f, 2));

    string finding = Assert.Single(
      MachiningChecks.HandParity(
        rig.World,
        rig.Jobs,
        rig.Kinds,
        rig.Materials,
        rig.Sequences
      )
    );

    Assert.Contains("held 3 s", finding);
    Assert.Contains("1.02 s", finding);
  }

  [Fact]
  public void A_hand_route_that_names_no_grid_recipe_is_found()
  {
    Rig rig = Build(job: j =>
      j with
      {
        Hand = new MachiningHand("workbench", "test:missing"),
      }
    );

    string finding = Assert.Single(
      MachiningChecks.HandParity(
        rig.World,
        rig.Jobs,
        rig.Kinds,
        rig.Materials,
        rig.Sequences
      )
    );

    Assert.Contains("is no grid recipe", finding);
  }

  [Fact]
  public void A_machine_that_beats_the_hand_route_has_no_findings()
  {
    Rig rig = Build();

    Assert.Empty(
      MachiningChecks.MachinesFirst(
        rig.World,
        rig.Jobs,
        rig.Kinds,
        rig.Materials,
        rig.Sequences
      )
    );
  }

  // Fails when the machine's seconds are compared as game seconds: 1858 J at 360 W is 5.2 s, over the hand's 1.0 s.
  [Fact]
  [PlantedDefect(
    typeof(MachiningChecks),
    nameof(MachiningChecks.MachinesFirst)
  )]
  public void A_machine_slower_than_the_hand_route_is_found_with_both_times()
  {
    Rig rig = Build(kinds: Kinds.Replace("\"power\": 360", "\"power\": 1"));

    string[] findings =
    [
      .. MachiningChecks.MachinesFirst(
        rig.World,
        rig.Jobs,
        rig.Kinds,
        rig.Materials,
        rig.Sequences
      ),
    ];

    Assert.Equal(2, findings.Length);
    Assert.All(
      findings,
      f => Assert.Contains("the machine takes 61.93 s, the hand route 1 s", f)
    );
  }

  // Fails when the wear points are compared with <= instead of <: two points against two tool steps.
  [Fact]
  public void A_machine_that_wears_its_tool_as_much_as_the_hand_is_found()
  {
    Rig rig = Build(job: j => j with { Wear = 2 });

    string[] findings =
    [
      .. MachiningChecks.MachinesFirst(
        rig.World,
        rig.Jobs,
        rig.Kinds,
        rig.Materials,
        rig.Sequences
      ),
    ];

    Assert.Equal(2, findings.Length);
    Assert.All(
      findings,
      f =>
        Assert.Contains(
          "wears its tool 2 points, the hand route's 2 tool steps",
          f
        )
    );
  }

  [Fact]
  public void A_piece_that_fits_every_kind_has_no_findings()
  {
    Rig rig = Build(
      kinds: Kinds[..Kinds.IndexOf("{ \"code\": \"test:small\"")]
        .TrimEnd()
        .TrimEnd(',') + " ] }"
    );

    Assert.Empty(
      MachiningChecks.Envelopes(rig.World, rig.Jobs, rig.Kinds, Shape)
    );
  }

  // Fails when a job's machines list is ignored: the 40 long piece does not fit test:small, which it does not name.
  [Fact]
  public void A_job_naming_its_kinds_is_held_to_those_envelopes_only()
  {
    Rig rig = Build(job: j => j with { Machines = ["test:big"] });

    Assert.Empty(
      MachiningChecks.Envelopes(rig.World, rig.Jobs, rig.Kinds, Shape)
    );
  }

  [Fact]
  [PlantedDefect(typeof(MachiningChecks), nameof(MachiningChecks.Envelopes))]
  public void A_piece_longer_than_a_kinds_envelope_is_found_at_that_kind()
  {
    Rig rig = Build();

    string finding = Assert.Single(
      MachiningChecks.Envelopes(rig.World, rig.Jobs, rig.Kinds, Shape)
    );

    Assert.Contains("at test:small", finding);
    Assert.Contains("40 x 10 voxels", finding);
  }

  [Fact]
  public void A_piece_wider_than_a_kinds_envelope_is_found()
  {
    Rig rig = Build(
      job: j => j with { Machines = ["test:big"] },
      kinds: Kinds.Replace("\"across\": 12", "\"across\": 9")
    );

    string finding = Assert.Single(
      MachiningChecks.Envelopes(rig.World, rig.Jobs, rig.Kinds, Shape)
    );

    Assert.Contains("at test:big", finding);
  }

  [Fact]
  public void An_input_with_no_shape_is_found_not_skipped()
  {
    Rig rig = Build(shape: false);

    string finding = Assert.Single(
      MachiningChecks.Envelopes(rig.World, rig.Jobs, rig.Kinds, Shape)
    );

    Assert.Contains("has no shape", finding);
  }

  [Fact]
  public void An_input_whose_shape_file_is_missing_is_found()
  {
    Rig rig = Build();

    string finding = Assert.Single(
      MachiningChecks.Envelopes(
        rig.World,
        rig.Jobs,
        rig.Kinds,
        _ => Path.Combine(_shapes, "gone.json")
      )
    );

    Assert.Contains("is missing", finding);
  }

  [Fact]
  public void A_job_whose_input_is_not_loaded_is_found_at_its_envelope()
  {
    Rig rig = Build(job: j => j with { Input = "test:nowhere" });

    string finding = Assert.Single(
      MachiningChecks.Envelopes(rig.World, rig.Jobs, rig.Kinds, Shape)
    );

    Assert.Contains("no such input item", finding);
  }

  [Fact]
  public void Cuttings_within_the_removed_units_have_no_findings()
  {
    Rig rig = Build();

    Assert.Empty(MachiningChecks.CuttingsMass(rig.World, rig.Jobs));
  }

  // Fails when the byproduct's count is left out of the sum: two chips of 10 units against 10 removed.
  [Fact]
  [PlantedDefect(typeof(MachiningChecks), nameof(MachiningChecks.CuttingsMass))]
  public void Cuttings_heavier_than_the_removed_units_are_found()
  {
    Rig rig = Build(job: j =>
      j with
      {
        Byproducts = [new MachiningByproduct("test:chips", 2)],
      }
    );

    string finding = Assert.Single(
      MachiningChecks.CuttingsMass(rig.World, rig.Jobs)
    );

    Assert.Contains("hold 20 units, the cut removes 10", finding);
  }

  [Fact]
  public void A_shape_base_of_another_domain_resolves_under_that_domains_assets()
  {
    string path = MachiningChecks.GameShapeFile("exlib:block/x");

    Assert.EndsWith(
      Path.Combine("assets", "exlib", "shapes", "block", "x.json"),
      path
    );
  }

  [Fact]
  public void A_game_shape_base_resolves_to_the_installs_survival_file()
  {
    string install = ExpandedLib.Testing.VsAssemblyResolver.InstallPath!;
    string survival = Path.Combine(install, "assets", "survival", "shapes");
    string file = Directory
      .EnumerateFiles(survival, "*.json", SearchOption.AllDirectories)
      .First();
    string shapeBase =
      "game:" + Path.GetRelativePath(survival, file)[..^5].Replace('\\', '/');

    Assert.Equal(file, MachiningChecks.GameShapeFile(shapeBase));
  }

  [Fact]
  public void A_game_shape_base_the_survival_tree_lacks_falls_to_the_game_tree()
  {
    string install = ExpandedLib.Testing.VsAssemblyResolver.InstallPath!;

    Assert.Equal(
      Path.Combine(install, "assets", "game", "shapes", "zz", "none.json"),
      MachiningChecks.GameShapeFile("game:zz/none")
    );
  }
}
#endif
