#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>What <see cref="SetupScene.Build"/> places from a setup file, kind by kind (setups.md,
/// Placement kinds), and what it refuses.</summary>
[Collection("Setups")]
public class SetupSceneTests : IDisposable
{
  public void Dispose() => SetupPlacers.Clear();

  #region Fixtures

  private const string Plated = "platedpipes:pipe-plated-";

  private const string PlatedFamily =
    "[\""
    + Plated
    + "straight-*\", \""
    + Plated
    + "bend-*\", \""
    + Plated
    + "tjunction-*\", \""
    + Plated
    + "xjunction-*\"]";

  private static string ModPath(string id) =>
    id switch
    {
      "platedpipes" => Path.Combine(RepoPaths.Root, "samples", "PlatedPipes"),
      "twintubblower" => Path.Combine(
        RepoPaths.Root,
        "samples",
        "TwinTubBlower"
      ),
      "smokestack" => Path.Combine(RepoPaths.Root, "samples", "SmokeStack"),
      "setupfixture" => Path.Combine(
        RepoPaths.Root,
        "tests",
        "ExpandedLib.Tests",
        "Setups",
        "Fixtures",
        "SetupFixture"
      ),
      _ => throw new ArgumentOutOfRangeException(nameof(id), id, "no such mod"),
    };

  private static SetupScene Build(SetupFile file) =>
    SetupScene.Build(file, [.. file.Mods.Select(ModPath)]);

  private static string FilePath(string name) =>
    Path.Combine(
      RepoPaths.Root,
      "tests",
      "ExpandedLib.Tests",
      "Setups",
      "Files",
      name
    );

  private static SetupScene BuildFile(string name) =>
    Build(SetupFile.Load(FilePath(name)));

  private static SetupFile Inline(
    string mod,
    string? layout,
    string steps = "[]"
  ) =>
    SetupFile.Parse(
      "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\""
        + mod
        + "\"], "
        + (layout == null ? "" : "\"layout\": " + layout + ", ")
        + "\"steps\": "
        + steps
        + ", \"seconds\": 10, \"record\": {}}",
      "t.json"
    );

  private static string Refused(SetupFile file) =>
    Assert.Throws<InvalidOperationException>(() => Build(file)).Message;

  private static string Pipes(string legend, params string[] rows) =>
    "{\"legend\": {"
    + legend
    + "}, \"layers\": {\"0\": ["
    + string.Join(", ", rows.Select(r => "\"" + r + "\""))
    + "]}}";

  private static string Run(
    char glyph,
    string id,
    string code,
    string family = PlatedFamily
  ) =>
    $"\"{glyph}\": {{\"run\": \"{id}\", \"code\": \"{Plated}{code}\", \"family\": {family}}}";

  private const string Open = "\"o\": {\"open\": true}";

  private static string CodeAt(SetupScene scene, int x, int y, int z) =>
    scene.World.GetBlock(new BlockPos(x, y, z)).Code.ToString();

  #endregion

  #region Entity and block kinds

  // Fails when Put sets the block without the entity the world spawns for its class: a run cell
  // then has no pipe entity and joins no network.
  [Fact]
  public void An_entity_block_is_placed_with_its_entity_and_joins_its_network()
  {
    using SetupScene scene = BuildFile("pipe-run.json");

    BlockPos[] cells = [.. scene.CellsOf("main")];

    Assert.Equal(3, cells.Length);
    Assert.All(cells, c => Assert.NotNull(scene.World.GetBlockEntity(c)));
    BlockNetwork? first = scene.World.NetworkAt(cells[0]);
    Assert.NotNull(first);
    Assert.Equal("pipe", first!.NetworkType);
    Assert.All(cells, c => Assert.Same(first, scene.World.NetworkAt(c)));
    Assert.Equal(
      [Plated + "straight-we", Plated + "straight-we", Plated + "straight-we"],
      cells.Select(c => scene.World.GetBlock(c).Code.ToString())
    );
  }

  // Fails when a block with no entity class is given an entity, or is not placed at all.
  [Fact]
  public void A_block_with_no_entity_class_is_placed_alone()
  {
    SetupFile file = Inline(
      "setupfixture",
      "{\"legend\": {\"s\": {\"code\": \"setupfixture:slab\"}}, \"layers\": {\"0\": [\"ss\"]}}"
    );

    using SetupScene scene = Build(file);

    Assert.Equal("setupfixture:slab", CodeAt(scene, 0, 0, 0));
    Assert.Equal("setupfixture:slab", CodeAt(scene, 1, 0, 0));
    Assert.Null(scene.World.GetBlockEntity(new BlockPos(0, 0, 0)));
  }

  // Fails when Put does not refuse a cell that holds a block.
  [Fact]
  public void A_cell_that_is_not_air_fails_the_placement_naming_it()
  {
    SetupFile file = Inline(
      "setupfixture",
      null,
      "[{\"place\": {\"code\": \"setupfixture:slab\", \"at\": [1, 2, 3]}}, "
        + "{\"place\": {\"code\": \"setupfixture:slab\", \"at\": [1, 2, 3]}}]"
    );

    string message = Refused(file);

    Assert.Contains("steps[1]", message);
    Assert.Contains("(1, 2, 3) holds setupfixture:slab, not air", message);
  }

  // Fails when a fill box stops one cell short on any axis.
  [Fact]
  public void A_fill_step_sets_every_cell_of_its_inclusive_box()
  {
    SetupFile file = Inline(
      "setupfixture",
      null,
      "[{\"fill\": {\"id\": \"ground\", \"code\": \"setupfixture:slab\", \"from\": [0, 0, 0], \"to\": [1, 1, 2]}}]"
    );

    using SetupScene scene = Build(file);

    Assert.Equal(12, scene.CellsOf("ground").Count);
    Assert.All(
      scene.CellsOf("ground"),
      c => Assert.Equal("setupfixture:slab", CodeAt(scene, c.X, c.Y, c.Z))
    );
    Assert.Equal("setupfixture:slab", CodeAt(scene, 1, 1, 2));
  }

  #endregion

  #region Runs

  // Fails when the shape lookup takes the first block of the family instead of the one whose
  // connector faces are the cell's.
  [Fact]
  public void A_run_corner_is_placed_as_the_bend_whose_faces_are_its_joins()
  {
    SetupFile file = Inline(
      "platedpipes",
      Pipes(
        Run('P', "main", "straight-we") + ", " + Open,
        "oPP.",
        "..P.",
        "..o."
      )
    );

    using SetupScene scene = Build(file);

    Block corner = scene.World.GetBlock(new BlockPos(2, 0, 0));
    Assert.Contains("bend", corner.Code.Path);
    var connector = (INetworkConnector)corner;
    Assert.True(connector.HasConnectorAt(BlockFacing.WEST));
    Assert.True(connector.HasConnectorAt(BlockFacing.SOUTH));
    Assert.False(connector.HasConnectorAt(BlockFacing.NORTH));
  }

  // Fails when a cell no block of the family fits is placed with another shape, or when the
  // message leaves out the cell.
  [Fact]
  public void A_shape_no_family_ships_fails_the_load_naming_the_cell()
  {
    SetupFile file = Inline(
      "platedpipes",
      Pipes(
        Run('P', "main", "straight-we", "\"" + Plated + "straight-*\"")
          + ", "
          + Open,
        "oPP.",
        "..P.",
        "..o."
      )
    );

    string message = Refused(file);

    Assert.Contains("run \"main\"", message);
    Assert.Contains("cell (2, 0, 0) needs a block with the faces sw", message);
    Assert.Contains("none of", message);
  }

  // Fails when two blocks of a family with the cell's faces are not refused: the straight and the
  // passthrough both hold the north and south faces.
  [Fact]
  public void Two_blocks_of_a_family_with_the_cells_faces_fail_the_load_naming_both()
  {
    SetupFile file = Inline(
      "platedpipes",
      Pipes(
        Run('P', "main", "straight-we", "\"" + Plated + "*\"") + ", " + Open,
        "oPo"
      )
    );

    string message = Refused(file);

    Assert.Contains("cell (1, 0, 0)", message);
    Assert.Contains("both have them", message);
    Assert.Contains(Plated + "straight-we", message);
    Assert.Contains(Plated + "passthrough", message);
  }

  // Fails when rule 2 does not open a run cell toward a neighbouring block entry whose block has
  // a connector on the shared face.
  [Fact]
  public void A_run_cell_opens_toward_a_connector_piece_taking_that_face()
  {
    SetupFile file = Inline(
      "platedpipes",
      Pipes(
        Run('P', "main", "straight-we")
          + ", \"V\": {\"code\": \""
          + Plated
          + "straight-ns\"}, "
          + Open,
        "..V..",
        "oPPPo"
      )
    );

    using SetupScene scene = Build(file);

    Block middle = scene.World.GetBlock(new BlockPos(2, 0, 1));
    Assert.Contains("tjunction", middle.Code.Path);
    var connector = (INetworkConnector)middle;
    Assert.True(connector.HasConnectorAt(BlockFacing.NORTH));
    Assert.False(connector.HasConnectorAt(BlockFacing.SOUTH));
  }

  // Fails when rule 2 opens toward a block entry whose block has no connector on the shared face.
  [Fact]
  public void A_run_cell_stays_closed_beside_a_piece_with_no_connector_on_that_face()
  {
    SetupFile file = Inline(
      "platedpipes",
      Pipes(
        Run('P', "main", "straight-we")
          + ", \"V\": {\"code\": \""
          + Plated
          + "straight-we\"}, "
          + Open,
        "..V..",
        "oPPPo"
      )
    );

    using SetupScene scene = Build(file);

    Assert.Equal(Plated + "straight-we", CodeAt(scene, 2, 0, 1));
  }

  // Fails when rule 2 opens a run cell toward a cell of another run: the two runs then merge into
  // a tee on the first.
  [Fact]
  public void A_run_cell_stays_closed_beside_another_run()
  {
    SetupFile file = Inline(
      "platedpipes",
      Pipes(
        Run('P', "main", "straight-we")
          + ", "
          + Run('Q', "side", "straight-ns")
          + ", "
          + Open,
        ".oQo.",
        "oPPPo"
      )
    );

    using SetupScene scene = Build(file);

    Assert.Equal(Plated + "straight-we", CodeAt(scene, 2, 0, 1));
  }

  #endregion

  #region Megablocks and mechanical lines

  private static SetupFile FromFile(
    string name,
    System.Func<string, string> edit
  ) => SetupFile.Parse(edit(File.ReadAllText(FilePath(name))), name);

  private static bool IsConstructed(SetupScene scene)
  {
    BlockEntity be = scene.World.GetBlockEntity(scene.PositionOf("blower"))!;
    return (bool)be.GetType().GetProperty("IsConstructed")!.GetValue(be)!;
  }

  private static BlockPos[] SolidCells(SetupScene scene) =>
    [
      .. from x in Enumerable.Range(-6, 13)
      from y in Enumerable.Range(-6, 13)
      from z in Enumerable.Range(-6, 13)
      let pos = new BlockPos(x, y, z)
      where scene.World.GetBlock(pos).Id != 0
      select pos,
    ];

  private static MechanicalNetwork?[] Lines(SetupScene scene) =>
    [
      .. scene
        .World.BlockEntities.SelectMany(kv =>
          kv.Value.Behaviors.OfType<BEBehaviorMPBase>()
        )
        .Select(b => b.Network),
    ];

  // Fails when the megablock's own OnBlockPlaced is not run: its fillers would not stand and the
  // drawn cells would hold air.
  [Fact]
  public void A_megablock_stands_its_footprint_on_the_drawn_cells()
  {
    using SetupScene scene = BuildFile("blower-alone.json");

    BlockPos[] solid = SolidCells(scene);

    Assert.Equal(6, solid.Length);
    Assert.Contains(scene.PositionOf("blower"), solid);
    Assert.All(
      solid.Where(c => !c.Equals(scene.PositionOf("blower"))),
      c =>
        Assert.IsAssignableFrom<BlockStructureFiller>(scene.World.GetBlock(c))
    );
  }

  // Fails when a layout cell of the structure is left without a glyph and the load does not
  // say so.
  [Fact]
  public void A_cells_glyph_one_short_fails_the_load_naming_the_cell()
  {
    SetupFile file = FromFile(
      "blower-alone.json",
      t => t.Replace("\"2\": [\"b\", \"b\", \"b\"]", "\"2\": [\"b\", \"b\"]")
    );

    string message = Refused(file);

    Assert.Contains("legend \"B\"", message);
    Assert.Contains("of the structure is drawn with no glyph", message);
  }

  // Fails when a cells glyph outside the structure's cells is not refused.
  [Fact]
  public void A_cells_glyph_outside_the_structure_fails_the_load_naming_it()
  {
    SetupFile file = FromFile(
      "blower-alone.json",
      t => t.Replace("\"layers\": {", "\"layers\": {\"5\": [\"b\"], ")
    );

    string message = Refused(file);

    Assert.Contains("is outside the cells of \"blower\"", message);
  }

  // Fails when the mechanical join leaves the two blowers' port behaviours on separate lines, or
  // joins a lone blower's behaviour to a line of its own.
  [Fact]
  public void Two_blowers_face_to_face_share_one_line_and_a_lone_blower_has_none()
  {
    using SetupScene paired = BuildFile("blowers-paired.json");
    using SetupScene alone = BuildFile("blower-alone.json");

    MechanicalNetwork?[] pairedLines = Lines(paired);
    MechanicalNetwork?[] aloneLines = Lines(alone);

    Assert.NotEmpty(pairedLines);
    Assert.All(pairedLines, l => Assert.NotNull(l));
    Assert.Single(pairedLines.Distinct());
    Assert.NotEmpty(aloneLines);
    Assert.All(aloneLines, l => Assert.Null(l));
  }

  // Fails when a megablock is placed over a cell its footprint needs, or the message leaves out
  // the cell.
  [Fact]
  public void A_footprint_cell_that_is_not_air_fails_the_placement_naming_it()
  {
    SetupFile file = SetupFile.Parse(
      "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"twintubblower\", \"setupfixture\"], "
        + "\"steps\": [{\"place\": {\"code\": \"setupfixture:slab\", \"at\": [0, 1, -1]}}, "
        + "{\"place\": {\"code\": \"twintubblower:blower-twintubblower-n\", \"at\": [0, 1, 0]}}], "
        + "\"seconds\": 10, \"record\": {}}",
      "t.json"
    );

    string message = Refused(file);

    Assert.Contains("steps[1]", message);
    Assert.Contains("footprint cell (0, 1, -1)", message);
    Assert.Contains("holds setupfixture:slab", message);
  }

  // Fails when a family no loaded block takes is read as an empty family that places nothing.
  [Fact]
  public void A_family_no_loaded_block_takes_fails_the_load_naming_it()
  {
    SetupFile file = Inline(
      "platedpipes",
      Pipes(
        Run('P', "main", "straight-we", "\"" + Plated + "nothing-*\"")
          + ", "
          + Open,
        "oPo"
      )
    );

    string message = Refused(file);

    Assert.Contains("run \"main\" legend \"P\"", message);
    Assert.Contains("no loaded block takes the family", message);
  }

  #endregion

  #region Completing


  // Fails when complete does not raise the cells the smoke stack reports missing or wait for its
  // monitor tick.
  [Fact]
  public void Completing_the_smoke_stack_raises_its_cells_until_the_structure_is_complete()
  {
    using SetupScene scene = BuildFile("smoke-stack.json");

    var stack = (BlockEntityMultiblockStructure)
      scene.World.GetBlockEntity(scene.PositionOf("stack"))!;

    Assert.True(stack.StructureComplete);
  }

  // Fails when complete on a construction does not run RccFake.Complete.
  [Fact]
  public void Completing_the_blower_completes_its_construction()
  {
    SetupFile file = FromFile(
      "blower-alone.json",
      t =>
        t.Replace(
          "\"code\": \"twintubblower:blower-twintubblower-n\"",
          "\"code\": \"twintubblower:blower-twintubblower-n\", \"complete\": \"all\""
        )
    );

    using SetupScene built = Build(file);
    using SetupScene bare = BuildFile("blower-alone.json");

    Assert.False(IsConstructed(bare));
    Assert.True(IsConstructed(built));
  }

  // Fails when a complete step on a block that is neither a multiblock nor a construction is
  // accepted.
  [Fact]
  public void Completing_a_block_with_nothing_to_complete_fails_naming_it()
  {
    SetupFile file = Inline(
      "platedpipes",
      Pipes(Run('P', "main", "straight-we") + ", " + Open, "oPo"),
      "[{\"complete\": {\"of\": \"main\"}}]"
    );

    string message = Refused(file);

    Assert.Contains("steps[0]", message);
    Assert.Contains("neither a multiblock nor a construction", message);
  }

  #endregion

  #region Placers

  private sealed class SlabPlacer(string? standIn, List<string> log)
    : ISetupPlacer
  {
    public bool Takes(string code) => code == "setupfixture:slab";

    public string? StandIn(string code) => standIn;

    public void Place(SetupScene scene, Block block, BlockPos pos)
    {
      log.Add($"{standIn}@{pos.X}");
      scene.World.Accessor.SetBlock(block.BlockId, pos);
    }
  }

  private const string TwoSlabs =
    "[{\"place\": {\"code\": \"setupfixture:slab\", \"at\": [0, 0, 0]}}, "
    + "{\"place\": {\"code\": \"setupfixture:slab\", \"at\": [1, 0, 0]}}]";

  // Fails when a registered placer is bypassed for the composed kinds, or its stand-in is printed
  // once per placement and not once per code.
  [Fact]
  public void A_registered_placer_places_its_code_and_its_stand_in_is_listed_once()
  {
    var log = new List<string>();
    SetupPlacers.Register(new SlabPlacer("a plain slab", log));

    using SetupScene scene = Build(Inline("setupfixture", null, TwoSlabs));

    Assert.Equal(["a plain slab@0", "a plain slab@1"], log);
    Assert.Equal(["stand-in setupfixture:slab: a plain slab"], scene.StandIns);
    Assert.Equal("setupfixture:slab", CodeAt(scene, 1, 0, 0));
  }

  // Fails when the first registered placer taking a code is not the only one to place it, or a
  // placer that departs nowhere still prints a stand-in.
  [Fact]
  public void The_first_placer_taking_a_code_places_it_alone()
  {
    var first = new List<string>();
    var second = new List<string>();
    SetupPlacers.Register(new SlabPlacer(null, first));
    SetupPlacers.Register(new SlabPlacer("later", second));

    using SetupScene scene = Build(Inline("setupfixture", null, TwoSlabs));

    Assert.Equal(2, first.Count);
    Assert.Empty(second);
    Assert.Empty(scene.StandIns);
  }

  #endregion

  #region Vanilla blocks and foreign nodes

  private static SetupScene BuildWithWires(SetupFile file) =>
    SetupScene.Build(
      file,
      [.. file.Mods.Select(ModPath)],
      world =>
      {
        foreach (
          TestNetworkBlock wire in TestNetworkBlock.Family(
            "molten",
            "platedpipes:wire",
            "Axis",
            ["ns", "we"],
            firstId: 9000
          )
        )
          world.Register(wire);
      }
    );

  // Fails when a `game:` code the layout or a step names is not resolved to its vanilla blocktype file, so the
  // load never registers the coal pile or the chimney course.
  [Fact]
  public void A_game_block_the_layout_or_a_step_names_is_loaded_and_placed()
  {
    SetupFile file = Inline(
      "setupfixture",
      "{\"legend\": {\"F\": {\"code\": \"game:coalpile\"}}, "
        + "\"layers\": {\"0\": [\"F\"]}}",
      "[{\"place\": {\"code\": \"game:claybrickchimney-four-red-ns\", \"at\": [1, 0, 0]}}]"
    );

    using SetupScene scene = Build(file);

    Assert.Equal("game:coalpile", CodeAt(scene, 0, 0, 0));
    Assert.Equal("game:claybrickchimney-four-red-ns", CodeAt(scene, 1, 0, 0));
  }

  // Fails when a vanilla file whose stem matches loads for a code none of its states define, or
  // an unknown `game:` code stops failing the file.
  [Fact]
  public void A_game_code_no_vanilla_file_defines_fails_naming_it()
  {
    SetupFile file = Inline(
      "setupfixture",
      null,
      "[{\"place\": {\"code\": \"game:claybrickchimney-four-mauve-ns\", \"at\": [0, 0, 0]}}]"
    );

    string message = Refused(file);

    Assert.Contains(
      "\"game:claybrickchimney-four-mauve-ns\" is not registered by the load",
      message
    );
  }

  // Fails when a wildcard `game:` code in a family is matched literally against a variant group's
  // states, so the file it names does not load.
  [Fact]
  public void A_wildcard_game_code_loads_the_file_it_matches()
  {
    SetupFile file = Inline(
      "platedpipes",
      Pipes(
        Run(
          'P',
          "main",
          "straight-we",
          "[\"" + Plated + "straight-*\", \"game:charcoalpile-*\"]"
        )
          + ", "
          + Open,
        "oPo"
      )
    );

    using SetupScene scene = Build(file);

    Assert.NotNull(
      scene.World.World.GetBlock(new AssetLocation("game:charcoalpile-3"))
    );
  }

  // Fails when a file is loaded only for a code with one token per variant group, so a hyphenated state
  // such as ported-north loads nothing.
  [Fact]
  public void A_game_code_with_a_hyphenated_variant_state_is_loaded_and_placed()
  {
    SetupFile file = Inline(
      "setupfixture",
      null,
      "[{\"place\": {\"code\": \"game:archimedesscrew-ported-north\", \"at\": [0, 0, 0]}}]"
    );

    using SetupScene scene = Build(file);

    Assert.Equal("game:archimedesscrew-ported-north", CodeAt(scene, 0, 0, 0));
  }

  // Fails when a wildcard `game:` code ending in `*` loads no file, or one with a `*` mid-code loads
  // none.
  [Theory]
  [InlineData("game:claybrickchimney-*")]
  [InlineData("game:claybrickchimney-*-ns")]
  public void A_wildcard_game_code_spanning_variant_groups_loads_the_file(
    string wildcard
  )
  {
    SetupFile file = Inline(
      "platedpipes",
      Pipes(
        Run(
          'P',
          "main",
          "straight-we",
          "[\"" + Plated + "straight-*\", \"" + wildcard + "\"]"
        )
          + ", "
          + Open,
        "oPo"
      )
    );

    using SetupScene scene = Build(file);

    Assert.NotNull(
      scene.World.World.GetBlock(
        new AssetLocation("game:claybrickchimney-four-red-ns")
      )
    );
  }

  // Fails when rule 2 opens a run cell toward a connector of another network type.
  [Fact]
  public void A_run_cell_stays_closed_beside_a_connector_of_another_network_type()
  {
    SetupFile file = Inline(
      "platedpipes",
      Pipes(
        "\"W\": {\"code\": \"platedpipes:wire-ns\"}, "
          + Run('P', "main", "straight-we")
          + ", "
          + Open,
        " W",
        "oPo"
      )
    );

    using SetupScene scene = BuildWithWires(file);

    Assert.Equal(Plated + "straight-we", CodeAt(scene, 1, 0, 1));
  }

  // Fails when a family holding blocks of two network types is accepted.
  [Fact]
  public void A_family_of_two_network_types_fails_the_load_naming_both()
  {
    SetupFile file = Inline(
      "platedpipes",
      Pipes(
        Run(
          'P',
          "main",
          "straight-we",
          "[\"" + Plated + "straight-*\", \"platedpipes:wire-*\"]"
        )
          + ", "
          + Open,
        "oPo"
      )
    );

    string message = Assert
      .Throws<InvalidOperationException>(() => BuildWithWires(file))
      .Message;

    Assert.Contains("more than one network type", message);
    Assert.Contains("pipe and molten", message);
  }

  #endregion

  #region Refusals

  public static TheoryData<string, string, string> Refused_steps =>
    new()
    {
      {
        "paste",
        "{\"file\": \"x\", \"at\": [0, 0, 0]}",
        "the fake world has no structure files to paste"
      },
      { "power", "{}", "the fake world has no creative power source" },
      { "source", "{}", "the fake world has no creative medium source" },
      {
        "break",
        "{\"at\": [0, 0, 0]}",
        "the fake world has no player to break with"
      },
      {
        "reload",
        "{}",
        "the fake world has no chunk save and load to reload through"
      },
      {
        "advance",
        "{\"hours\": 1}",
        "the fake world has no game calendar to advance"
      },
    };

  // Fails when a step kind the fake world cannot run is let through to a scene that silently skips it.
  [Theory]
  [MemberData(nameof(Refused_steps))]
  public void A_step_kind_the_reader_cannot_run_is_refused_by_name(
    string kind,
    string body,
    string reason
  )
  {
    SetupFile file = Inline("setupfixture", null, $"[{{\"{kind}\": {body}}}]");

    string message = Refused(file);

    Assert.Contains("steps[0]", message);
    Assert.Contains($"\"{kind}\"", message);
    Assert.Contains(reason, message);
  }

  public static TheoryData<string, string, string> Refused_placements =>
    new()
    {
      {
        "{\"place\": {\"code\": \"setupfixture:slab\", \"at\": [0, 0, 0], \"by\": \"player\"}}",
        "steps[0]",
        "by the player is refused"
      },
      {
        "{\"place\": {\"code\": \"setupfixture:slab\", \"at\": [0, 0, 0], \"complete\": 1}}",
        "steps[0]",
        "to a stage is refused"
      },
      {
        "{\"place\": {\"code\": \"setupfixture:slab\", \"at\": [0, 0, 0], \"tree\": {}}}",
        "steps[0]",
        "\"tree\" is not built"
      },
      {
        "{\"place\": {\"code\": \"setupfixture:slab\", \"at\": [0, 0, 0], \"materials\": {}}}",
        "steps[0]",
        "\"materials\" is not built"
      },
    };

  // Fails when a place or complete key the fake world cannot honour is accepted and ignored.
  [Theory]
  [MemberData(nameof(Refused_placements))]
  public void A_place_or_complete_key_the_fake_world_cannot_honour_is_refused(
    string steps,
    string where,
    string reason
  )
  {
    SetupFile file = Inline("setupfixture", null, $"[{steps}]");

    string message = Refused(file);

    Assert.Contains(where, message);
    Assert.Contains(reason, message);
  }

  // Fails when a layout entry carrying a key the reader does not build is placed without it.
  [Fact]
  public void A_layout_entry_with_a_tree_is_refused_naming_the_glyph()
  {
    SetupFile file = Inline(
      "setupfixture",
      "{\"legend\": {\"s\": {\"code\": \"setupfixture:slab\", \"tree\": {}}}, \"layers\": {\"0\": [\"s\"]}}"
    );

    string message = Refused(file);

    Assert.Contains("legend \"s\"", message);
    Assert.Contains("\"tree\" is not built", message);
  }

  // Fails when a code the load did not register places as air or throws something other than the
  // load's own message.
  [Fact]
  public void A_code_the_load_did_not_register_fails_naming_it()
  {
    SetupFile file = Inline(
      "setupfixture",
      null,
      "[{\"place\": {\"code\": \"setupfixture:nothing\", \"at\": [0, 0, 0]}}]"
    );

    string message = Refused(file);

    Assert.Contains(
      "\"setupfixture:nothing\" is not registered by the load",
      message
    );
  }

  // Fails when the folders do not have to match the file's mods one to one.
  [Fact]
  public void A_folder_count_that_differs_from_the_mods_is_refused()
  {
    SetupFile file = Inline("setupfixture", null);

    Assert.Throws<ArgumentException>(() => SetupScene.Build(file, []));
  }

  #endregion
}
#endif
