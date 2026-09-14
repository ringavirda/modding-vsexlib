using System.IO;
using System.Linq;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ExpandedLib.Tests.Invariants;

/// <summary>Every template under templates/content parses, names itself by convention and is
/// cut clean of the sample it came from.</summary>
public class TemplateGuards {
  private static readonly string Root = Path.Combine(RepoPaths.Root, "templates", "content");
  private static readonly string[] Kinds = [
    "block", "item", "recipe", "megablock", "multiblock", "node",
    "blockbehavior", "entitybehavior", "config", "migration", "command", "tests",
  ];

  [Fact]
  public void Every_kind_has_a_manifest_named_by_convention() {
    foreach (string kind in Kinds) {
      string path = Path.Combine(Root, $"exlib-{kind}", ".template.config", "template.json");
      Assert.True(File.Exists(path), path);
      JObject manifest = JObject.Parse(File.ReadAllText(path));
      Assert.Equal($"exlib-{kind}", (string?)manifest["shortName"]);
      Assert.StartsWith("ExpandedLib.Templates.", (string?)manifest["identity"]);
    }
  }

  [Fact]
  public void No_template_file_names_the_sample_it_was_cut_from() {
    string[] files = Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
      .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
               && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
      .ToArray();
    Assert.NotEmpty(files);
    foreach (string file in files)
      foreach (string literal in new[] { "HandMill", "handmill", "Grains", "grains" })
        Assert.DoesNotContain(literal, File.ReadAllText(file));
  }
}
