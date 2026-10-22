#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The draw law against small blocks that each draw wrongly: an entity that returns true
/// and pools nothing at placement or at a construction stage; a mesh outside the footprint; shapes
/// the tesselator drops or cannot find; entities that throw or override a call their draw type
/// never makes; and the variants the law keeps apart or counts once.</summary>
public class DrawLawTests
{
  private static readonly string DrawFixtures = Path.Combine(
    RepoPaths.Root,
    "tests",
    "ExpandedLib.Tests",
    "Harness",
    "Fixtures",
    "draw"
  );

  private static readonly System.Reflection.Assembly[] Exlib =
  [
    typeof(BlockStructureFiller).Assembly,
  ];

  private static void Prepare(TestWorld world)
  {
    world.ClientAssets("drawfix", DrawFixtures);
    world.RegisterClass("test-plain", typeof(PlainBe));
    world.RegisterClass("test-inside", typeof(PoolsInside));
    world.RegisterClass("test-turned", typeof(PoolsTurned));
    world.RegisterClass("test-throws", typeof(ThrowsInTesselation));
    world.RegisterClass("test-overrides", typeof(OverridesTesselation));
    world.RegisterClass("test-initthrows", typeof(ThrowsOnInitialize));
    world.RegisterClass("test-logs", typeof(LogsOnInitialize));
    world.RegisterClass("test-nan", typeof(PoolsNaN));
    world.RegisterClass("test-drawsformed", typeof(DrawsFormed));
  }

  private static BlockLaws.Law Run(params ExBlockDef[] defs) =>
    DrawLaw.Run(BlockLaws.Stand(defs, Exlib, Prepare), "test");

  private static ExBlockDef Drawn(string code, string shape = "oneface") =>
    ExBlockDef.Create("test", code).DrawType("json").Shape($"drawfix:{shape}");

  private static readonly ExBlockDef Wall = Drawn("wall");

  private static ExBlockDef Framed(string code, string entity) =>
    Drawn(code)
      .SideVariant()
      .EntityClass(entity)
      .Multiblock(m =>
        m.Number($"test:{code}-*", 1)
          .Number("test:wall", 2)
          .At(0, 0, 0, 1)
          .At(1, 0, 0, 2)
          .At(0, 0, 1, 2)
      );

  private static ExBlockDef FramedWithFiller(string code) =>
    Drawn(code)
      .SideVariant()
      .EntityClass("test-plain")
      .Multiblock(m =>
        m.Number($"test:{code}-*", 1)
          .Number("exlib:structurefiller", 2)
          .At(0, 0, 0, 1)
          .At(1, 0, 0, 2)
      );

  // Fails when the law stops paying construction stages, pays them in every facing, or stops
  // naming a cell whose entity returns true and pools nothing.
  [Fact]
  [PlantedDefect(typeof(DrawLaw), nameof(DrawLaw.Run))]
  public void A_construction_that_returns_true_and_pools_nothing_draws_nothing_at_every_stage()
  {
    BlockLaws.Law law = Run(
      Drawn("bed")
        .VariantGroup("side", "north", "east")
        .Behavior("HorizontalOrientable")
        .EntityClass("test-plain")
        .Construction(c =>
          c.Stage(s => s.Require("game:stick", 1))
            .Stage(s => s.Require("game:plank-oak", 1))
            .Stage(s => s.Require("game:plank-oak", 2))
        )
    );

    const string why =
      "draws nothing (OnTesselation returned true and pooled nothing)";
    Assert.Equal(
      [
        $"test:bed-north placed {why}",
        $"test:bed-north stage 1 {why}",
        $"test:bed-north stage 2 {why}",
        $"test:bed-east placed {why}",
      ],
      law.Findings
    );
    Assert.Equal(4, law.Cases);
  }

  // Fails when the footprint check is skipped, or the tolerance grows to a block.
  [Fact]
  [PlantedDefect(typeof(DrawLaw), nameof(DrawLaw.Run))]
  public void A_part_drawn_untranslated_is_outside_the_footprint()
  {
    BlockLaws.Law law = Run(
      Drawn("turned").EntityClass("test-turned"),
      Drawn("inside").EntityClass("test-inside")
    );

    string finding = Assert.Single(law.Findings);
    Assert.StartsWith(
      "test:turned placed draws 4 of 4 vertices outside its footprint, up to 1 blocks beyond, bounds ",
      finding
    );
  }

  // Fails when variants differing only in a texture group stop collapsing, or the facing stops
  // keeping variants apart.
  [Fact]
  [PlantedDefect(typeof(DrawLaw), nameof(DrawLaw.Run))]
  public void Variants_differing_only_in_texture_count_once_and_two_facings_twice()
  {
    BlockLaws.Law law = Run(
      Drawn("tinted")
        .VariantGroup("color", "red", "blue")
        .VariantGroup("side", "north", "east")
        .Behavior("HorizontalOrientable")
    );

    Assert.Equal(1, law.Blocks);
    Assert.Equal(2, law.Cases);
    Assert.Empty(law.Findings);
  }

  // Fails when a formed anchor's footprint leaves out its layout cells, or the law stops drawing
  // the structure formed.
  [Fact]
  [PlantedDefect(typeof(DrawLaw), nameof(DrawLaw.Run))]
  public void A_formed_anchor_drawing_its_whole_structure_is_inside()
  {
    BlockLaws.Law law = Run(Wall, Framed("frame", "test-drawsformed"));

    Assert.DoesNotContain(law.Findings, f => f.Contains(" formed "));
    Assert.Contains(
      law.Findings,
      f =>
        f.StartsWith("test:frame-n placed draws ")
        && f.Contains("outside its footprint")
    );
    Assert.Equal(8, law.Cases);
  }

  // Fails when a structure filler among a formed structure's cells is judged as a block of its own.
  [Fact]
  [PlantedDefect(typeof(DrawLaw), nameof(DrawLaw.Run))]
  public void A_filler_cell_of_a_formed_structure_is_no_case()
  {
    BlockLaws.Law law = Run(Wall, FramedWithFiller("fillframe"));

    Assert.DoesNotContain(law.Findings, f => f.Contains("structurefiller"));
    Assert.Equal(5, law.Cases);
  }

  // Fails when the structure filler is judged as a block of its own.
  [Fact]
  [PlantedDefect(typeof(DrawLaw), nameof(DrawLaw.Run))]
  public void A_structure_filler_is_no_case()
  {
    BlockLaws.Law law = DrawLaw.Run(
      BlockLaws.Stand([Wall], Exlib, Prepare),
      "exlib"
    );

    Assert.Equal(0, law.Blocks);
    Assert.Equal(0, law.Cases);
  }

  // Fails when a throw in OnTesselation is dropped.
  [Fact]
  [PlantedDefect(typeof(DrawLaw), nameof(DrawLaw.Run))]
  public void An_entity_that_throws_while_drawing_is_named()
  {
    BlockLaws.Law law = Run(Drawn("thrower").EntityClass("test-throws"));

    Assert.Equal(
      [
        "test:thrower placed OnTesselation threw InvalidOperationException: the mesh broke",
      ],
      law.Findings
    );
  }

  // Fails when the block's own mesh failing is dropped.
  [Fact]
  [PlantedDefect(typeof(DrawLaw), nameof(DrawLaw.Run))]
  public void A_shape_nobody_ships_is_named()
  {
    BlockLaws.Law law = Run(Drawn("lost", "missing"));

    Assert.Contains(
      law.Findings,
      f =>
        f.StartsWith(
          "test:lost placed block mesh threw FileNotFoundException: "
        )
    );
  }

  // Fails when a face without 4 uv values stops being told from other tesselator failures.
  [Fact]
  [PlantedDefect(typeof(DrawLaw), nameof(DrawLaw.Run))]
  public void A_face_without_four_uv_values_is_named()
  {
    BlockLaws.Law law = Run(Drawn("crooked", "baduv"));

    Assert.Contains(
      "test:crooked placed block mesh threw a face without 4 uv values (the game warns and drops it), or an element colour map the draw layer cannot resolve",
      law.Findings
    );
  }

  // Fails when an element without from and to of 3 values stops being told from other tesselator
  // failures.
  [Fact]
  [PlantedDefect(typeof(DrawLaw), nameof(DrawLaw.Run))]
  public void An_element_with_illegal_from_is_named()
  {
    BlockLaws.Law law = Run(Drawn("short", "badfrom"));

    Assert.Contains(
      "test:short placed block mesh threw a shape element without from and to of 3 values (the game warns and drops it)",
      law.Findings
    );
  }

  // Fails when a block of draw type Empty is let through, or a JSON block with no shape is.
  [Fact]
  [PlantedDefect(typeof(DrawLaw), nameof(DrawLaw.Run))]
  public void A_block_with_nothing_to_show_is_named()
  {
    BlockLaws.Law law = Run(
      ExBlockDef.Create("test", "hollow").DrawType("empty"),
      Drawn("bare", "noelements")
    );

    Assert.Equal(
      [
        "test:bare placed draws nothing (block shape has no faces)",
        "test:hollow placed draws nothing (draw type Empty)",
      ],
      law.Findings.Order()
    );
  }

  // Fails when an override on a draw type the game never calls it for stops being named, or one
  // on a draw type it calls is named.
  [Fact]
  [PlantedDefect(typeof(DrawLaw), nameof(DrawLaw.Run))]
  public void An_override_the_draw_type_never_calls_is_named()
  {
    BlockLaws.Law law = Run(
      ExBlockDef
        .Create("test", "cubed")
        .DrawType("cube")
        .EntityClass("test-overrides"),
      Drawn("called").EntityClass("test-overrides")
    );

    string finding = Assert.Single(law.Findings);
    Assert.StartsWith(
      "test:cubed overrides OnTesselation on draw type Cube, which never calls it (OverridesTesselation)",
      finding
    );
  }

  // Fails when a warning logged while placing is dropped.
  [Fact]
  [PlantedDefect(typeof(DrawLaw), nameof(DrawLaw.Run))]
  public void A_warning_logged_while_placing_is_named()
  {
    BlockLaws.Law law = Run(Drawn("noisy").EntityClass("test-logs"));

    Assert.Equal(
      ["test:noisy placed logged Warning: it grumbled"],
      law.Findings
    );
  }

  // Fails when a vertex with a NaN coordinate is let through.
  [Fact]
  [PlantedDefect(typeof(DrawLaw), nameof(DrawLaw.Run))]
  public void A_vertex_with_a_NaN_coordinate_is_named()
  {
    BlockLaws.Law law = Run(Drawn("nan").EntityClass("test-nan"));

    string finding = Assert.Single(law.Findings);
    Assert.StartsWith(
      "test:nan placed draws 1 of 4 vertices outside its footprint, some with a NaN coordinate, bounds ",
      finding
    );
  }

  // Fails when a throw while placing is not reported against the block.
  [Fact]
  [PlantedDefect(typeof(DrawLaw), nameof(DrawLaw.Run))]
  public void A_placement_that_throws_is_named()
  {
    BlockLaws.Law law = Run(Drawn("broken").EntityClass("test-initthrows"));

    string finding = Assert.Single(law.Findings);
    Assert.StartsWith(
      "test:broken placed threw InvalidOperationException: it would not start",
      finding
    );
  }

  private static MeshData Slab(ITesselatorAPI tess, double from, double to)
  {
    var element = new ShapeElement
    {
      Name = "slab",
      From = [from, 0, from],
      To = [to, 4, to],
      Faces = new Dictionary<string, ShapeElementFace>
      {
        ["up"] = new ShapeElementFace
        {
          Texture = "#all",
          Uv = [0, 0, 16, 16],
          Enabled = true,
        },
      },
    };
    element.TrimTextureNamesAndResolveFaces();
    tess.TesselateShape(
      "probe",
      new Shape { Elements = [element], Textures = [] },
      out MeshData mesh,
      tess.GetTextureSource(new Block())
    );
    return mesh;
  }

  private sealed class PlainBe : BlockEntity { }

  private sealed class PoolsInside : BlockEntity
  {
    public override bool OnTesselation(
      ITerrainMeshPool mesher,
      ITesselatorAPI tessThreadTesselator
    )
    {
      mesher.AddMeshData(Slab(tessThreadTesselator, 0, 16));
      return true;
    }
  }

  private sealed class PoolsTurned : BlockEntity
  {
    public override bool OnTesselation(
      ITerrainMeshPool mesher,
      ITesselatorAPI tessThreadTesselator
    )
    {
      mesher.AddMeshData(
        Slab(tessThreadTesselator, 8, 16)
          .Rotate(new Vec3f(0, 0, 0), 0, GameMath.PIHALF, 0)
      );
      return true;
    }
  }

  private sealed class ThrowsInTesselation : BlockEntity
  {
    public override bool OnTesselation(
      ITerrainMeshPool mesher,
      ITesselatorAPI tessThreadTesselator
    ) => throw new InvalidOperationException("the mesh broke");
  }

  private sealed class OverridesTesselation : BlockEntity
  {
    public override bool OnTesselation(
      ITerrainMeshPool mesher,
      ITesselatorAPI tessThreadTesselator
    ) => false;
  }

  private sealed class ThrowsOnInitialize : BlockEntity
  {
    public override void Initialize(ICoreAPI api) =>
      throw new InvalidOperationException("it would not start");
  }

  private sealed class LogsOnInitialize : BlockEntity
  {
    public override void Initialize(ICoreAPI api)
    {
      base.Initialize(api);
      api.Logger.Warning("it grumbled");
    }
  }

  private sealed class PoolsNaN : BlockEntity
  {
    public override bool OnTesselation(
      ITerrainMeshPool mesher,
      ITesselatorAPI tessThreadTesselator
    )
    {
      MeshData mesh = Slab(tessThreadTesselator, 0, 16);
      mesh.xyz[0] = float.NaN;
      mesher.AddMeshData(mesh);
      return true;
    }
  }

  private sealed class DrawsFormed : BlockEntityMultiblockStructure
  {
    protected override void UpdateStructureRotation()
    {
      if (Block != null)
        SetStructureAngle(ExOrientation.AngleFromSide(Block.Variant["side"]));
    }

    protected override string GetIncompleteMessage(int missingCount) =>
      $"missing {missingCount}";

    protected override string GetCompleteMessage() => "complete";

    public override bool OnTesselation(
      ITerrainMeshPool mesher,
      ITesselatorAPI tessThreadTesselator
    )
    {
      foreach (var cell in LayoutCells.Where(c => !c.At.Equals(Pos)))
      {
        float[] matrix = Mat4f.Create();
        Mat4f.Translate(
          matrix,
          matrix,
          cell.At.X - Pos.X,
          cell.At.Y - Pos.Y,
          cell.At.Z - Pos.Z
        );
        mesher.AddMeshData(Slab(tessThreadTesselator, 0, 16), matrix);
      }
      return true;
    }
  }
}
#endif
