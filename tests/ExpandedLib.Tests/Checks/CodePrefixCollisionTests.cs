using System.Collections.Generic;
using System.Reflection;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="CodePrefixCollision.Collisions"/> over two planted block families: one whose
/// base code is a prefix of another's at a <c>-</c>, one whose codes only share letters.</summary>
public class CodePrefixCollisionTests {
  public CodePrefixCollisionTests() => TestModDomain.Register();

  private const string Colliding = "plantedprefix";
  private const string Distinct = "plantedprefixclean";
  private static readonly Assembly Here =
    typeof(CodePrefixCollisionTests).Assembly;

  /// <summary>Declares its blocks only under <see cref="Colliding"/> and <see cref="Distinct"/>, so
  /// other scans of this assembly never see them.</summary>
  private sealed class Rods : IExBlockDefProvider {
    public static IEnumerable<ExBlockDef> Definitions(string domain) =>
      domain switch {
        Colliding =>
        [
          ExBlockDef.Create(domain, "rod"),
          ExBlockDef.Create(domain, "rod-long"),
        ],
        Distinct =>
        [
          ExBlockDef.Create(domain, "rod"),
          ExBlockDef.Create(domain, "rodlong"),
        ],
        _ => [],
      };
  }

  [Fact]
  [PlantedDefect(
    typeof(CodePrefixCollision),
    nameof(CodePrefixCollision.Collisions)
  )]
  public void A_base_code_that_prefixes_another_at_a_dash_is_reported() {
    string finding = Assert.Single(
      CodePrefixCollision.Collisions(Colliding, Here)
    );

    Assert.StartsWith(
      "'plantedprefix:rod' is a prefix of 'plantedprefix:rod-long'",
      finding
    );
  }

  [Fact]
  public void Codes_sharing_letters_without_a_dash_boundary_pass() {
    Assert.Empty(CodePrefixCollision.Collisions(Distinct, Here));
  }
}
