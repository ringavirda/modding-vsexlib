using ExpandedLib.Config;

namespace ExpandedLib.Tests;

/// <summary>
/// A minimal <c>[ExConfigRegister]</c> config, top-level for <c>ExConfigGenerator</c> to emit an
/// accessor. Gives <see cref="ExModSystemTests"/> a real generated accessor to find.
/// </summary>
[ExConfigRegister("exmodsystemtest.json", "exlib-modsystem-test")]
public class ExModSystemTestConfig : IExVersionedConfig {
  public string? ConfigVersion { get; set; }

  public int Tunable { get; set; } = 42;
}
