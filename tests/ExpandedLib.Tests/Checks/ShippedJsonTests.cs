using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="ShippedJson.Check"/> over a planted asset tree holding each defect it names,
/// and a clean tree.</summary>
public class ShippedJsonTests {
  private const string Patch = """
    [
      { "op": "add", "file": "game:blocktypes/stone.json", "side": "Server" },
      { "op": "add", "file": "game:blocktypes/clay.json" },
      { "op": "add", "file": "game:recipes/grid/bowl.json", "side": "Client" },
      { "op": "add", "file": "game:config/handbook.json", "side": "Client" }
    ]
    """;

  // Each line's path is repo-relative, so only its file name is compared.
  private static IReadOnlyList<string> Check(PlantedFiles files) =>
    [
      .. ShippedJson
        .Check(files.Path("assets"))
        .Select(f => f[(f.LastIndexOf('/') + 1)..]),
    ];

  // Fails when Check stops naming a control character, invalid JSON, a patch entry with no side, or
  // a server-only target patched on another side.
  [Fact]
  [PlantedDefect(typeof(ShippedJson), nameof(ShippedJson.Check))]
  public void Each_planted_defect_is_reported() {
    using var files = new PlantedFiles();
    files.Write(
      "assets/planted/blocktypes/bell.json",
      "/* \u0007 */ { \"code\": \"bell\" }"
    );
    files.Write("assets/planted/blocktypes/torn.json", "{ \"code\": ");
    files.Write("assets/planted/patches/vanilla.json", Patch);

    IReadOnlyList<string> findings = Check(files);

    Assert.Equal(4, findings.Count);
    Assert.Contains(
      "bell.json contains 1 control character(s): 0x07 at offset 3",
      findings
    );
    Assert.Contains(
      findings,
      f => f.StartsWith("torn.json is not valid JSON: ")
    );
    Assert.Contains("vanilla.json [1] declares no side", findings);
    Assert.Contains(
      "vanilla.json [2] targets a server-only category but declares \"Client\"",
      findings
    );
  }

  [Fact]
  public void A_clean_tree_passes() {
    using var files = new PlantedFiles();
    files.Write(
      "assets/planted/blocktypes/bell.json",
      "{ \"code\": \"bell\", }"
    );
    files.Write(
      "assets/planted/patches/vanilla.json",
      """[{ "op": "add", "file": "game:blocktypes/stone.json", "side": "Server" }]"""
    );

    Assert.Empty(Check(files));
    Assert.Single(ShippedJson.PatchFiles(files.Path("assets")));
  }
}
