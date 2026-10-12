using ExpandedLib.Registries;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The shared case-insensitive keyed store backing the config/recipe/(metal/liquid)
/// registries.</summary>
public class ExKeyedRegistryTests {
  private sealed record Item(string Code, int Value);

  [Fact]
  public void Registers_and_looks_up_by_derived_code() {
    var reg = new ExKeyedRegistry<Item>(i => i.Code);
    reg.Register(new Item("iiex", 1));

    Assert.True(reg.TryGet("iiex", out var found));
    Assert.Equal(1, found.Value);
  }

  [Fact]
  public void Lookup_is_case_insensitive() {
    var reg = new ExKeyedRegistry<Item>(i => i.Code);
    reg.Register(new Item("Smex", 7));

    Assert.True(reg.TryGet("SMEX", out var found));
    Assert.Equal(7, found.Value);
  }

  [Fact]
  public void Re_registering_a_code_replaces_the_item_and_warns_naming_both() {
    var log = new RecordingLogger();
    log.Expect(EnumLogType.Warning, "planted table");
    var reg = new ExKeyedRegistry<Item>(
      i => i.Code,
      "planted table",
      i => $"owner {i.Value}"
    );
    ILogger? was = RegistryLog.Logger;
    RegistryLog.Logger = log;
    try {
      reg.Register(new Item("iiex", 1));
      reg.Register(new Item("iiex", 2));
    } finally {
      RegistryLog.Logger = was;
    }

    Assert.True(reg.TryGet("iiex", out var found));
    Assert.Equal(2, found.Value);
    Assert.Single(reg.Codes);
    string warning = Assert.Single(log.Warnings);
    Assert.Contains("planted table", warning);
    Assert.Contains("'iiex'", warning);
    Assert.Contains("owner 1", warning);
    Assert.Contains("owner 2", warning);
  }

  [Fact]
  public void Re_registering_an_item_described_alike_logs_nothing() {
    var log = new RecordingLogger();
    var reg = new ExKeyedRegistry<Item>(i => i.Code);
    ILogger? was = RegistryLog.Logger;
    RegistryLog.Logger = log;
    try {
      reg.Register(new Item("iiex", 1));
      reg.Register(new Item("iiex", 1));
    } finally {
      RegistryLog.Logger = was;
    }

    Assert.Empty(log.Warnings);
  }

  [Fact]
  public void Update_replaces_the_item_without_a_warning() {
    var log = new RecordingLogger();
    var reg = new ExKeyedRegistry<Item>(i => i.Code);
    ILogger? was = RegistryLog.Logger;
    RegistryLog.Logger = log;
    try {
      reg.Register(new Item("iiex", 1));
      reg.Update(new Item("iiex", 2));
    } finally {
      RegistryLog.Logger = was;
    }

    Assert.True(reg.TryGet("iiex", out var found));
    Assert.Equal(2, found.Value);
    Assert.Empty(log.Warnings);
  }

  [Fact]
  public void A_quiet_registry_replaces_without_a_warning() {
    var log = new RecordingLogger();
    var reg = new ExKeyedRegistry<Item>(i => i.Code) { Quiet = true };
    ILogger? was = RegistryLog.Logger;
    RegistryLog.Logger = log;
    try {
      reg.Register(new Item("iiex", 1));
      reg.Register(new Item("iiex", 2));
    } finally {
      RegistryLog.Logger = was;
    }

    Assert.Empty(log.Warnings);
  }

  [Fact]
  public void Missing_code_returns_false() {
    var reg = new ExKeyedRegistry<Item>(i => i.Code);
    Assert.False(reg.TryGet("nope", out _));
  }

  [Fact]
  public void Codes_and_values_enumerate_the_registered_set() {
    var reg = new ExKeyedRegistry<Item>(i => i.Code);
    reg.Register(new Item("a", 1));
    reg.Register(new Item("b", 2));

    Assert.Equal(2, reg.Codes.Count);
    Assert.Contains("a", reg.Codes);
    Assert.Contains("b", reg.Codes);
    Assert.Equal(3, System.Linq.Enumerable.Sum(reg.Values, i => i.Value));
  }
}
