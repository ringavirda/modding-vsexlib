using System.Reflection;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Pins the re-registration warning on <see cref="ExDefinitions.Logger"/>: silent from the
/// same assembly, a Warning from a different one.
/// </summary>
[Collection("ExDefinitions")]
public class ExDefinitionsTests {
  public ExDefinitionsTests() {
    ExDefinitions.Clear();
    ExDefinitions.Logger = null;
  }

  [Fact]
  public void Re_registering_from_the_same_assembly_logs_nothing() {
    var logger = new RecordingLogger();
    ExDefinitions.Logger = logger;
    Assembly asm = Assembly.GetExecutingAssembly();

    ExDefinitions.RegisterBlock(ExBlockDef.Create("d", "c"), asm);
    ExDefinitions.RegisterBlock(
      ExBlockDef.Create("d", "c").Resistance(9f),
      asm
    );

    Assert.Empty(logger.Entries);
  }

  [Fact]
  public void Re_registering_from_a_different_assembly_logs_a_warning() {
    var logger = new RecordingLogger();
    ExDefinitions.Logger = logger;

    ExDefinitions.RegisterBlock(
      ExBlockDef.Create("d", "c"),
      typeof(ExDefinitionsTests).Assembly
    );
    ExDefinitions.RegisterBlock(
      ExBlockDef.Create("d", "c"),
      typeof(ExDefinitions).Assembly
    );

    logger.Expect(EnumLogType.Warning, "re-registered");
    Assert.Equal(EnumLogType.Warning, Assert.Single(logger.Entries).Type);
    string warning = Assert.Single(logger.Warnings);
    Assert.Contains("re-registered by exlib (was ExpandedLib.Tests)", warning);
  }

  [Fact]
  public void Re_registering_an_item_from_a_different_assembly_logs_a_warning() {
    var logger = new RecordingLogger();
    ExDefinitions.Logger = logger;

    ExDefinitions.RegisterItem(
      ExItemDef.Create("d", "c"),
      typeof(ExDefinitionsTests).Assembly
    );
    ExDefinitions.RegisterItem(
      ExItemDef.Create("d", "c"),
      typeof(ExDefinitions).Assembly
    );

    logger.Expect(EnumLogType.Warning, "re-registered");
    Assert.Equal(EnumLogType.Warning, Assert.Single(logger.Entries).Type);
    string warning = Assert.Single(logger.Warnings);
    Assert.Contains("re-registered by exlib (was ExpandedLib.Tests)", warning);
  }

  [Fact]
  public void No_logger_wired_is_a_silent_no_op() {
    ExDefinitions.Logger = null;
    // A null Logger must not throw.
    ExDefinitions.RegisterBlock(
      ExBlockDef.Create("d", "c"),
      typeof(ExDefinitionsTests).Assembly
    );
    ExDefinitions.RegisterBlock(
      ExBlockDef.Create("d", "c"),
      typeof(ExDefinitions).Assembly
    );
  }

  [Fact]
  public void Views_writes_the_item_views_attribute() {
    var item = new Item {
      Attributes = new JsonObject(
        ExItemDef.Create("d", "c").Views(ItemViewKind.Longtool, "+y").ToJson()[
          "attributes"
        ]!.DeepClone()
      ),
    };

    Assert.Equal("longtool", item.Attributes["itemViews"]["kind"].AsString());
    Assert.Equal("+y", item.Attributes["itemViews"]["tip"].AsString());
    Assert.Equal(ItemViewKind.Longtool, CollectibleViews.KindOf(null!, item));
  }

  [Fact]
  public void A_block_view_kind_is_written_without_a_tip() {
    var block = ExBlockDef
      .Create("d", "c")
      .Views(ItemViewKind.Partial)
      .ToJson();

    Assert.Equal(
      "partial",
      (string?)block["attributes"]!["itemViews"]!["kind"]
    );
    Assert.Null(block["attributes"]!["itemViews"]!["tip"]);
  }
}
