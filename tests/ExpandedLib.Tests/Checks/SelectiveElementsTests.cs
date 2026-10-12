using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="SelectiveElements"/> over a planted shape and definitions.</summary>
public class SelectiveElementsTests {
  private const string Shape = """
    { "elements": [
      { "name": "Root", "children": [
        { "name": "Base", "children": [ { "name": "Plate" } ] },
        { "name": "AxleA" }, { "name": "AxleB" }, { "name": "Lid" }
      ] }
    ] }
    """;

  private static SelectiveElements.Result Run(params string[] paths) {
    using var files = new PlantedFiles();
    files.Write("shapes/machine.json", Shape);
    return SelectiveElements.Check(
      [
        ExBlockDef
          .Create("plantedshape", "stack")
          .Shape("plantedshape:machine")
          .ShapeSelectiveElements(paths),
      ],
      "plantedshape",
      files.Path("shapes")
    );
  }

  [Theory]
  [InlineData(
    "Root/Base",
    "Base has children and an exact name drops them; write Root/Base/*"
  )]
  [InlineData("Root/Basement/*", "no element Basement under Root")]
  [InlineData("Root/Plate/*", "no element Plate under Root")]
  [InlineData("Root/Gear*", "no element starts with Gear")]
  [InlineData("Root/*/Plate", "* before the last segment")]
  [PlantedDefect(typeof(SelectiveElements), nameof(SelectiveElements.Check))]
  public void A_path_that_misses_or_drops_children_is_reported(
    string path,
    string reason
  ) {
    SelectiveElements.Result result = Run(path);

    Assert.Equal(1, result.Lists);
    Assert.Equal(
      [$"plantedshape/blocktypes/stack.json: {path}: {reason}"],
      result.Findings
    );
  }

  [Theory]
  [InlineData("Root/Base/*")]
  [InlineData("Root/Lid")]
  [InlineData("Root/Axle*")]
  [InlineData("Root/*")]
  [InlineData("*")]
  public void The_subtree_form_and_a_leaf_pass(string path) {
    SelectiveElements.Result result = Run(path);

    Assert.Equal(1, result.Paths);
    Assert.Empty(result.Findings);
  }

  // Fails when the list spread into a shapebytype entry is not read, or the base-less original is judged.
  [Fact]
  public void A_list_inside_a_shapebytype_entry_is_read() {
    using var files = new PlantedFiles();
    files.Write("shapes/machine.json", Shape);
    var def = ExBlockDef
      .Create("plantedshape", "stack")
      .VariantGroup("side", "n");
    def.ShapeByType("*-n", "plantedshape:machine");
    SelectiveElements.Result result = SelectiveElements.Check(
      [def.ShapeSelectiveElements("Root/Base")],
      "plantedshape",
      files.Path("shapes")
    );

    Assert.Equal(1, result.Lists);
    Assert.Equal(
      [
        "plantedshape/blocktypes/stack.json: Root/Base: Base has children and an exact name drops them; write Root/Base/*",
      ],
      result.Findings
    );
  }

  // Fails when a missing shape file is skipped instead of reported.
  [Fact]
  public void A_shape_that_does_not_exist_is_reported() {
    using var files = new PlantedFiles();
    SelectiveElements.Result result = SelectiveElements.Check(
      [
        ExBlockDef
          .Create("plantedshape", "stack")
          .Shape("plantedshape:gone")
          .ShapeSelectiveElements("Root/*"),
      ],
      "plantedshape",
      files.Path("shapes")
    );

    Assert.Equal(
      ["plantedshape/blocktypes/stack.json: Root/*: gone.json does not exist"],
      result.Findings
    );
  }

  // Fails when a shape of another domain is read against this mod's folder.
  [Fact]
  public void A_shape_of_another_domain_is_not_read() {
    using var files = new PlantedFiles();
    SelectiveElements.Result result = SelectiveElements.Check(
      [
        ExBlockDef
          .Create("plantedshape", "stack")
          .Shape("game:block/x")
          .ShapeSelectiveElements("Root"),
      ],
      "plantedshape",
      files.Path("shapes")
    );

    Assert.Equal(1, result.Paths);
    Assert.Empty(result.Findings);
  }

  // Fails when a list beside no base and no shapebytype entry is skipped.
  [Fact]
  public void A_list_with_no_base_anywhere_is_reported() {
    using var files = new PlantedFiles();
    SelectiveElements.Result result = SelectiveElements.Check(
      [
        ExBlockDef
          .Create("plantedshape", "stack")
          .ShapeSelectiveElements("Root/*"),
      ],
      "plantedshape",
      files.Path("shapes")
    );

    Assert.Equal(
      ["plantedshape/blocktypes/stack.json: Root/*: has no shape base to read"],
      result.Findings
    );
  }
}
