using System.Collections.Generic;
using System.Reflection;
using ExpandedLib.Registries;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// <see cref="EntityRegistry.DomainOf"/>'s fallback path: an assembly declaring no
/// <c>[assembly: ExDomain]</c> resolves to the caller's own domain; <see cref="EntityRegistry.Logger"/>
/// names the assembly in a warning.
/// </summary>
public class EntityRegistryTests {
  private sealed class PlainClass;

  // This assembly starts unregistered, as in a world no mod of it has started in yet.
  public EntityRegistryTests() {
    EntityRegistry.ResetForWorld();
    (
      (IDictionary<Assembly, string>)
        typeof(EntityRegistry)
          .GetField(
            "_domainByAssembly",
            BindingFlags.NonPublic | BindingFlags.Static
          )!
          .GetValue(null)!
    ).Remove(typeof(EntityRegistryTests).Assembly);
    EntityRegistry.Logger = null;
  }

  [Fact]
  public void A_domain_fallback_logs_a_warning_naming_the_assembly() {
    var logger = new RecordingLogger();
    EntityRegistry.Logger = logger;
    logger.Expect(EnumLogType.Warning, "declares no [assembly: ExDomain]");

    // This assembly declares no [assembly: ExDomain].
    string domain = EntityRegistry.DomainOf(
      typeof(PlainClass).Assembly,
      "iiex"
    );

    Assert.Equal("iiex", domain);
    Assert.Contains(
      typeof(PlainClass).Assembly.GetName().Name!,
      Assert.Single(logger.Warnings)
    );
  }

  [Fact]
  public void No_logger_wired_is_a_silent_no_op() {
    EntityRegistry.Logger = null;
    // Guards against a null-reference in the warning path when Logger is unset.
    string domain = EntityRegistry.DomainOf(
      typeof(PlainClass).Assembly,
      "iiex"
    );
    Assert.Equal("iiex", domain);
  }
}
