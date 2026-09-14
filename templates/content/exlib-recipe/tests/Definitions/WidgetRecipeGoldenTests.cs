using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using WidgetNamespace.Recipes;
using Xunit;

namespace WidgetNamespace.Tests.Definitions;

/// <summary>
/// Golden-file parity for the widget recipe's code-first definition, pinned through the harness's
/// own comparison engine. The domain here is a fixture id, not the mod's real <c>--Domain</c> - the
/// def's shape is what is pinned, independent of which mod scaffolds it.
/// </summary>
public class WidgetRecipeGoldenTests {
  private static string GoldenPath([CallerFilePath] string here = "") =>
    Path.Combine(Path.GetDirectoryName(here)!, "..", "goldens", "WidgetRecipe.json");

  [Fact]
  public void Definition_reproduces_its_golden() {
    ExRecipeDef def = WidgetRecipes.Definitions("fixture").Single();
    string file = GoldenPath();
    Assert.True(File.Exists(file), file);

    Newtonsoft.Json.Linq.JToken expected = Newtonsoft.Json.Linq.JToken.Parse(
      File.ReadAllText(file)
    );
    Assert.True(
      DefinitionParity.Equal(expected, def.ToJson(), out string normalized),
      $"widget recipe def diverged from its golden:\n{normalized}"
    );
  }
}
