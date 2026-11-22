using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// <see cref="IExBlockDefDecorator"/>: runs on every path a block def reaches.
/// </summary>
[Collection("ExDefinitions")]
public class BlockDefDecoratorTests : IDisposable {
  public BlockDefDecoratorTests() {
    TestModDomain.Register();
    ExDefinitions.Clear();
  }

  public void Dispose() => ExDefinitions.Clear();

  private sealed class Provider : IExBlockDefProvider {
    public static IEnumerable<ExBlockDef> Definitions(string domain) =>
      [ExBlockDef.Create(domain, "bdec-provided")];
  }

  private sealed class Decorator : IExBlockDefDecorator {
    public void Decorate(ExBlockDef def) {
      if (def.Code.StartsWith("bdec-", StringComparison.Ordinal))
        def.Attribute("decorated", true);
    }
  }

  private static bool Decorated(ExBlockDef def) =>
    (bool?)def.ToJson()["attributes"]?["decorated"] == true;

  // Fails when the registry path skips the decorators, or a def registered before its decorator was
  // found is left as it was.
  [Fact]
  public void Decorator_runs_on_register_and_on_late_discovery() {
    ExDefinitions.DiscoverDecorators(typeof(Decorator).Assembly);
    ExDefinitions.DiscoverAndRegister("test", typeof(Provider).Assembly);
    Assert.True(
      Decorated(ExDefinitions.Blocks.Single(d => d.Code == "bdec-provided"))
    );

    ExDefinitions.Clear();
    ExDefinitions.RegisterBlock(ExBlockDef.Create("test", "bdec-early"));
    ExDefinitions.DiscoverDecorators(typeof(Decorator).Assembly);
    Assert.True(
      Decorated(ExDefinitions.Blocks.Single(d => d.Code == "bdec-early"))
    );
  }

  // Fails when Collect leaves a def undecorated, so a golden and the game would see two defs.
  [Fact]
  public void Decorator_runs_on_collected_defs() {
    IReadOnlyList<IExDef> collected = DefinitionGoldens.Collect(
      "test",
      typeof(Provider).Assembly
    );
    Assert.True(
      Decorated(
        collected.OfType<ExBlockDef>().Single(d => d.Code == "bdec-provided")
      )
    );
  }

  // Fails when a def the decorator does not own is changed.
  [Fact]
  public void Decorator_leaves_other_defs_alone() {
    ExDefinitions.DiscoverDecorators(typeof(Decorator).Assembly);
    ExDefinitions.RegisterBlock(ExBlockDef.Create("test", "other-block"));
    Assert.False(
      Decorated(ExDefinitions.Blocks.Single(d => d.Code == "other-block"))
    );
  }

  // Fails when a move drops the source tab's selectors, leaves the source key, replaces rather than
  // appends to a target already listed, or touches a tab it was not asked to.
  [Fact]
  public void MoveCreativeTab_moves_and_appends() {
    ExBlockDef block = ExBlockDef
      .Create("test", "mv")
      .CreativeTab("general", "*")
      .CreativeTab("test", "a")
      .CreativeTab("parts", "b");
    block.MoveCreativeTab("test", "parts");
    var tabs = (Newtonsoft.Json.Linq.JObject)
      block.ToJson()["creativeinventory"]!;
    Assert.Null(tabs["test"]);
    Assert.Equal(["b", "a"], tabs["parts"]!.Select(t => (string)t!));
    Assert.Equal(["*"], tabs["general"]!.Select(t => (string)t!));

    ExItemDef item = ExItemDef
      .Create("test", "mv")
      .CreativeTab("test", "x")
      .CreativeTab("parts", "y");
    item.MoveCreativeTab("test", "parts").MoveCreativeTab("absent", "parts");
    var itemTabs = (Newtonsoft.Json.Linq.JObject)
      item.ToJson()["creativeinventory"]!;
    Assert.Null(itemTabs["test"]);
    Assert.Equal(["y", "x"], itemTabs["parts"]!.Select(t => (string)t!));

    Assert.Null(
      ExItemDef.Create("test", "bare").MoveCreativeTab("a", "b").ToJson()[
        "creativeinventory"
      ]
    );
  }
}
