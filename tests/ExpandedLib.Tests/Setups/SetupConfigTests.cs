using System;
using System.Collections.Generic;
using ExpandedLib.Config;
using ExpandedLib.Scenarios;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>A case's <c>config</c> written through the registers and set back (setups.md, Checks
/// and Variants).</summary>
[Collection("Setups")]
public class SetupConfigTests {
  private const string Mod = "setupconfigmod";

  private static ExConfigRegister<CaseConfig> Store() {
    var store = new ExConfigRegister<CaseConfig>("setupconfig.json", Mod);
    ExConfigProfiles.Register(store);
    return store;
  }

  private static SetupConfigValue Value(string key, int value) =>
    new(Mod, key, new JValue(value));

  // Fails when Restore leaves the written values in the register.
  [Fact]
  public void Restore_sets_the_registers_old_value_back() {
    ExConfigRegister<CaseConfig> store = Store();
    var config = new SetupConfig("run.json");

    config.Apply([Value("Level", 7)]);
    Assert.Equal(7, store.Config.Level);
    config.Restore();

    Assert.Equal(1, store.Config.Level);
  }

  // Fails when Restore sets the values back first to last.
  [Fact]
  public void Restore_sets_values_back_last_first() {
    ExConfigRegister<CaseConfig> store = Store();
    var config = new SetupConfig("run.json");

    config.Apply([Value("Level", 7), Value("Level", 9)]);
    Assert.Equal(9, store.Config.Level);
    config.Restore();

    Assert.Equal(1, store.Config.Level);
  }

  // Fails when a false from the register's TryGet is ignored, since the message then comes from Set.
  [Fact]
  public void A_key_the_register_does_not_hold_fails_naming_it() {
    Store();
    var config = new SetupConfig("run.json");

    InvalidOperationException error = Assert.Throws<InvalidOperationException>(
      () =>
        config.Apply([Value("Nothing", 1)])
    );

    Assert.Equal(
      "run.json: config.setupconfigmod.Nothing: the register holds no value \"Nothing\".",
      error.Message
    );
  }
}
