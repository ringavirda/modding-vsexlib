using ExpandedLib.Registries;
using NSubstitute;
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

  public EntityRegistryTests() => EntityRegistry.Logger = null;

  [Fact]
  public void A_domain_fallback_logs_a_warning_naming_the_assembly() {
    var logger = Substitute.For<ILogger>();
    EntityRegistry.Logger = logger;

    // This assembly declares no [assembly: ExDomain] and is never passed to RegisterAll.
    string domain = EntityRegistry.DomainOf(
      typeof(PlainClass).Assembly,
      "iiex"
    );

    Assert.Equal("iiex", domain);
    logger.Received(1).Warning(Arg.Any<string>(), Arg.Any<object[]>());
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
