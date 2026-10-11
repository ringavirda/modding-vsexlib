#if GAME_GE_1_22
using System;
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
public class SetupSceneTests {
  #region Fixtures

  private const string Plated = "platedpipes:pipe-plated-";

  private const string PlatedFamily =
    "[\"" + Plated + "straight-*\", \"" + Plated + "bend-*\", \"" + Plated
    + "tjunction-*\", \"" + Plated + "xjunction-*\"]";

  private static string ModPath(string id) =>
    id switch {
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

  private static string Run(char glyph, string id, string code, string family = PlatedFamily) =>
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
      Pipes(Run('P', "main", "straight-we") + ", " + Open, "oPP.", "..P.", "..o.")
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
        Run('P', "main", "straight-we", "\"" + Plated + "straight-*\"") + ", " + Open,
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
          + ", \"V\": {\"code\": \"" + Plated + "straight-ns\"}, " + Open,
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
          + ", \"V\": {\"code\": \"" + Plated + "straight-we\"}, " + Open,
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
          + ", " + Run('Q', "side", "straight-we") + ", " + Open,
        ".oQo.",
        "oPPPo"
      )
    );

    using SetupScene scene = Build(file);

    Assert.Equal(Plated + "straight-we", CodeAt(scene, 2, 0, 1));
  }

  #endregion
}
#endif
