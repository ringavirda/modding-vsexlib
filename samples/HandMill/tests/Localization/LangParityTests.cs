using System.IO;
using ExpandedLib.Testing;
using Xunit;

namespace HandMill.Tests;

/// <summary>
/// The lang-parity rule (<see cref="LangParity"/>) over the sample's own shipped lang tree.
/// </summary>
public class LangParityTests {
  [Fact]
  public void Handmills_locales_carry_exactly_the_english_key_set_and_placeholders() {
    var offenders = LangParity.Check(
      Path.Combine(RepoPaths.Assets("handmill"), "lang")
    );
    Assert.True(offenders.Count == 0, string.Join("\n", offenders));
  }
}
