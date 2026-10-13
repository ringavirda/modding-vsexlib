using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="MegablockFrames.Misfit"/> over a planted shape three blocks long on x and
/// the cells that do and do not declare it, and <see cref="MegablockFrames.Run"/> over planted
/// blocks drawing it.</summary>
public class MegablockFramesTests {
  private const string Shape = """
    { "elements": [ { "name": "body", "from": [0, 0, 0], "to": [48, 16, 16] } ] }
    """;

  private const string Arm = """
    { "elements": [ { "name": "arm", "from": [0, 0, 0], "to": [16, 16, 16], "children": [
      { "name": "tip", "from": [16, 0, 0], "to": [48, 16, 16] } ] } ] }
    """;

  private static JObject Cell(int x, JObject? box = null) {
    var cell = new JObject {
      ["x"] = x,
      ["y"] = 0,
      ["z"] = 0,
    };
    if (box != null)
      cell["collisionBox"] = box;
    return cell;
  }

  /// <summary>A principal declaring the given cells.</summary>
  private sealed class Declared(params JObject[] cells) : IFillerHost {
    public JsonObject FillerOffsets => new(new JArray(cells));
  }

  /// <summary>A principal reserving the cells east of it, <paramref name="length"/> blocks in all.</summary>
  private static Declared Principal(int length) =>
    new([.. Enumerable.Range(1, length - 1).Select(x => Cell(x))]);

  private static MegablockReach Reach(
    string element,
    float max,
    int x = 2,
    int y = 0
  ) => new(element, new Vec3i(x, y, 0), max, "planted");

  [Fact]
  [PlantedDefect(typeof(MegablockFrames), nameof(MegablockFrames.Misfit))]
  public void A_shape_spun_by_a_different_angle_than_its_footprint_is_reported() {
    using var files = new PlantedFiles();
    string shape = files.Write("shapes/long.json", Shape);

    string? misfit = MegablockFrames.Misfit(shape, 90, Principal(3), 0);

    Assert.NotNull(misfit);
    Assert.StartsWith(
      "draws 'body' 32 voxels past cell (0, 0, -2) from the principal, 4 allowed",
      misfit
    );
    Assert.Contains("draws 'body' 16 voxels past cell (0, 0, -1)", misfit);
    Assert.Contains("shape spins by 90deg and the footprint by 0deg", misfit);
  }

  [Fact]
  [PlantedDefect(typeof(MegablockFrames), nameof(MegablockFrames.Misfit))]
  public void A_shape_longer_than_its_footprint_is_reported() {
    using var files = new PlantedFiles();
    string shape = files.Write("shapes/long.json", Shape);

    string? misfit = MegablockFrames.Misfit(shape, 0, Principal(2), 0);

    Assert.NotNull(misfit);
    Assert.StartsWith(
      "draws 'body' 16 voxels past cell (2, 0, 0) from the principal, 4 allowed",
      misfit
    );
    Assert.DoesNotContain("\n", misfit);
  }

  // Fails when the shape is compared with the footprint's bounding box instead of its cells.
  [Fact]
  [PlantedDefect(typeof(MegablockFrames), nameof(MegablockFrames.Misfit))]
  public void A_part_in_an_undeclared_hole_inside_the_bounding_box_is_reported() {
    using var files = new PlantedFiles();
    string shape = files.Write("shapes/long.json", Shape);

    string? misfit = MegablockFrames.Misfit(shape, 0, new Declared(Cell(2)), 0);

    Assert.NotNull(misfit);
    Assert.StartsWith("draws 'body' 8 voxels past cell (1, 0, 0)", misfit);
  }

  // Fails when a cell's collisionBox is ignored and the whole cell counts.
  [Fact]
  [PlantedDefect(typeof(MegablockFrames), nameof(MegablockFrames.Misfit))]
  public void A_cell_with_a_collision_box_counts_only_that_box() {
    using var files = new PlantedFiles();
    string shape = files.Write("shapes/long.json", Shape);
    JObject sliver = new() {
      ["x1"] = 0,
      ["y1"] = 0,
      ["z1"] = 0,
      ["x2"] = 0.25,
      ["y2"] = 1,
      ["z2"] = 1,
    };

    string? misfit = MegablockFrames.Misfit(
      shape,
      0,
      new Declared(Cell(1, sliver), Cell(2)),
      0
    );

    Assert.NotNull(misfit);
    Assert.StartsWith("draws 'body' 6 voxels past cell (1, 0, 0)", misfit);
    Assert.Null(
      MegablockFrames.Misfit(shape, 0, new Declared(Cell(1), Cell(2)), 0)
    );
  }

  // Fails when an entry's max is ignored: the 16-voxel reach passes at 16 and fails at 15.5.
  [Fact]
  [PlantedDefect(typeof(MegablockFrames), nameof(MegablockFrames.Misfit))]
  public void A_reach_entry_passes_up_to_its_max_and_fails_past_it() {
    using var files = new PlantedFiles();
    string shape = files.Write("shapes/long.json", Shape);

    Assert.Null(
      MegablockFrames.Misfit(
        shape,
        0,
        Principal(2),
        0,
        reaches: [Reach("body", 16)]
      )
    );
    string? past = MegablockFrames.Misfit(
      shape,
      0,
      Principal(2),
      0,
      reaches: [Reach("body", 15.5f)]
    );
    Assert.NotNull(past);
    Assert.StartsWith(
      "draws 'body' 16 voxels past cell (2, 0, 0) from the principal, 4 allowed, past its allowed 15.5",
      past
    );
  }

  // Fails when an entry naming no element is not reported, or is reported as unused instead.
  [Fact]
  [PlantedDefect(typeof(MegablockFrames), nameof(MegablockFrames.Misfit))]
  public void A_reach_entry_naming_no_element_is_reported() {
    using var files = new PlantedFiles();
    string shape = files.Write("shapes/long.json", Shape);

    string? misfit = MegablockFrames.Misfit(
      shape,
      0,
      Principal(3),
      0,
      reaches: [Reach("ghost", 16)]
    );

    Assert.Equal(
      "reach 'ghost' for cell (2, 0, 0) names no element of the shape",
      misfit
    );
  }

  // Fails when an entry no element reaches its cell under passes unreported.
  [Fact]
  [PlantedDefect(typeof(MegablockFrames), nameof(MegablockFrames.Misfit))]
  public void A_reach_entry_no_element_uses_is_reported() {
    using var files = new PlantedFiles();
    string shape = files.Write("shapes/long.json", Shape);

    string? misfit = MegablockFrames.Misfit(
      shape,
      0,
      Principal(3),
      0,
      reaches: [Reach("body", 16, y: 1)]
    );

    Assert.Equal(
      "reach 'body' for cell (2, 1, 0) is unused: no element under it reaches that cell past the overhang",
      misfit
    );
  }

  // Fails when the entry's cell stays in the north frame while the footprint turns.
  [Fact]
  [PlantedDefect(typeof(MegablockFrames), nameof(MegablockFrames.Misfit))]
  public void A_reach_cell_turns_with_the_facing() {
    using var files = new PlantedFiles();
    string shape = files.Write("shapes/long.json", Shape);

    Assert.Null(
      MegablockFrames.Misfit(
        shape,
        90,
        Principal(2),
        90,
        reaches: [Reach("body", 16)]
      )
    );
  }

  // Fails when an entry covers only the element it names and not the elements under it.
  [Fact]
  [PlantedDefect(typeof(MegablockFrames), nameof(MegablockFrames.Misfit))]
  public void A_reach_entry_covers_the_elements_under_it() {
    using var files = new PlantedFiles();
    string shape = files.Write("shapes/arm.json", Arm);

    Assert.Null(
      MegablockFrames.Misfit(
        shape,
        0,
        Principal(2),
        0,
        reaches: [Reach("arm", 16)]
      )
    );
  }

  // Fails when the shortest nesting entry holds instead of the nearest: 'arm' would hold at 1.
  [Fact]
  [PlantedDefect(typeof(MegablockFrames), nameof(MegablockFrames.Misfit))]
  public void Of_nested_reach_entries_the_nearest_holds() {
    using var files = new PlantedFiles();
    string shape = files.Write("shapes/arm.json", Arm);

    string? misfit = MegablockFrames.Misfit(
      shape,
      0,
      Principal(2),
      0,
      reaches: [Reach("arm", 1), Reach("arm/tip", 16)]
    );

    Assert.Equal(
      "reach 'arm' for cell (2, 0, 0) is unused: no element under it reaches that cell past the overhang",
      misfit
    );
  }

  [Theory]
  [InlineData(0)]
  [InlineData(90)]
  [InlineData(180)]
  [InlineData(270)]
  public void A_shape_and_footprint_turned_together_fit(int angle) {
    using var files = new PlantedFiles();
    string shape = files.Write("shapes/long.json", Shape);

    Assert.Null(MegablockFrames.Misfit(shape, angle, Principal(3), angle));
  }

  private static ExBlockDef Long(string code, int spinOffset) =>
    ExBlockDef
      .Create("test", code)
      .Class("ExFilledMegastructure")
      .SideVariant()
      .FillerOffsets([new(1, 0, 0), new(2, 0, 0)])
      .ShapeSpunPerOrientation("test:" + code, spinOffset);

  private static BlockLaws.Law Run(
    PlantedFiles files,
    string? reaches,
    params ExBlockDef[] defs
  ) =>
    MegablockFrames.Run(
      "test",
      defs,
      [typeof(BlockStructureFiller).Assembly],
      reaches,
      shapeFile: shapeBase =>
        files.Path("shapes/" + shapeBase.Split(':')[1] + ".json")
    );

  // Fails when a variant is measured with the north shape or the north footprint, not its own.
  [Fact]
  [PlantedDefect(typeof(MegablockFrames), nameof(MegablockFrames.Run))]
  public void A_variant_whose_shape_spin_leaves_its_footprint_angle_is_named_by_its_code() {
    using var files = new PlantedFiles();
    files.Write("shapes/fits.json", Shape);
    files.Write("shapes/skews.json", Shape);

    BlockLaws.Law law = Run(files, null, Long("fits", 0), Long("skews", 90));

    Assert.Equal(2, law.Blocks);
    Assert.Equal(8, law.Cases);
    Assert.All(law.Findings, f => Assert.StartsWith("test:skews-", f));
    Assert.Equal(
      ["e", "n", "s", "w"],
      law.Findings.Select(f => f.Split(' ')[0][^1..])
        .Distinct()
        .Order(System.StringComparer.Ordinal)
    );
  }

  // Fails when the reaches file is read per variant instead of by shape base, or its leftovers go unreported.
  [Fact]
  [PlantedDefect(typeof(MegablockFrames), nameof(MegablockFrames.Run))]
  public void Reach_entries_of_a_shape_base_are_read_from_the_file_and_judged_across_its_variants() {
    using var files = new PlantedFiles();
    files.Write("shapes/skews.json", Shape);
    string reaches = files.Write(
      "reaches.json",
      """
      {
        "test:skews": [
          { "element": "body", "cell": [2, 0, 0], "max": 32, "reason": "planted" },
          { "element": "ghost", "cell": [1, 0, 0], "max": 1, "reason": "planted" },
          { "element": "body", "cell": [0, 3, 0], "max": 1, "reason": "planted" }
        ],
        "test:nobody": [
          { "element": "body", "cell": [2, 0, 0], "max": 1, "reason": "planted" }
        ]
      }
      """
    );

    BlockLaws.Law law = Run(files, reaches, Long("skews", 90));

    Assert.Contains(
      "test:skews reach 'ghost' for cell (1, 0, 0) names no element of the shape",
      law.Findings
    );
    Assert.Contains(
      "test:skews reach 'body' for cell (0, 3, 0) is unused: no element under it reaches that cell past the overhang",
      law.Findings
    );
    Assert.Contains(
      "test:nobody has reach entries and no loaded variant draws it",
      law.Findings
    );
    Assert.Equal(
      4,
      law.Findings.Where(f =>
          f.StartsWith("test:skews-", System.StringComparison.Ordinal)
        )
        .Select(f => f.Split(' ')[0])
        .Distinct()
        .Count()
    );
  }

  [Fact]
  public void Reaches_are_read_by_shape_base() {
    using var files = new PlantedFiles();
    string path = files.Write(
      "reaches.json",
      """
      { "iiex:a/b": [ { "element": "x/y", "cell": [1, 2, -3], "max": 6.5, "reason": "why" } ] }
      """
    );

    IReadOnlyList<MegablockReach> read = MegablockFrames.ReadReaches(path)[
      "iiex:a/b"
    ];

    Assert.Equal(
      [new MegablockReach("x/y", new Vec3i(1, 2, -3), 6.5f, "why")],
      read
    );
  }
}
