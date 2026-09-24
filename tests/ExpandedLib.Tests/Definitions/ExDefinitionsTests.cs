using System.Reflection;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Pins the re-registration notification on <see cref="ExDefinitions.Logger"/>: silent from the
/// same assembly, a Notification from a different one.
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
  public void Re_registering_from_a_different_assembly_logs_a_notification() {
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

    Assert.Equal(EnumLogType.Notification, Assert.Single(logger.Entries).Type);
  }

  [Fact]
  public void Re_registering_an_item_from_a_different_assembly_logs_a_notification() {
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

    Assert.Equal(EnumLogType.Notification, Assert.Single(logger.Entries).Type);
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
}
