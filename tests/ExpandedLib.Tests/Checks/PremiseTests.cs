using System;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="Premise"/> over planted corpora and exlib's own goldens.</summary>
public class PremiseTests {
  // Fails when NotEmpty passes an empty corpus.
  [Fact]
  [PlantedDefect(typeof(Premise), nameof(Premise.NotEmpty))]
  public void An_empty_corpus_fails_naming_it() {
    var ex = Assert.Throws<InvalidOperationException>(() =>
      Premise.NotEmpty(Array.Empty<string>(), "shape files")
    );

    Assert.Contains("no shape files found", ex.Message);
    Assert.Equal(["a"], Premise.NotEmpty(new[] { "a" }, "shape files"));
  }

  // Fails when Covers passes a read set missing a census entry, or an empty census.
  [Fact]
  [PlantedDefect(typeof(Premise), nameof(Premise.Covers))]
  public void A_census_entry_not_read_fails_naming_it() {
    var ex = Assert.Throws<InvalidOperationException>(() =>
      Premise.Covers(["a", "c"], ["a", "b", "c"], "blocks")
    );

    Assert.StartsWith("1 of 3 blocks not read: b", ex.Message);
    Assert.Throws<InvalidOperationException>(() =>
      Premise.Covers(["a"], [], "blocks")
    );
    Premise.Covers(["a", "b", "extra"], ["a", "b"], "blocks");
  }

  // Fails when the goldens form of Covers stops reading the domain's golden blocktypes, or keys
  // them other than as DefinitionGoldens.RelativePath.
  [Fact]
  [PlantedDefect(typeof(Premise), nameof(Premise.Covers))]
  public void The_goldens_census_is_every_golden_blocktype_of_the_domain() {
    string[] defs =
    [
      .. DefinitionGoldens
        .Collect("exlib", typeof(ExBlockDef).Assembly)
        .Select(DefinitionGoldens.RelativePath),
    ];

    Premise.Covers(defs, "exlib");
    var ex = Assert.Throws<InvalidOperationException>(() =>
      Premise.Covers(defs.Where(d => !d.Contains("structurefiller")), "exlib")
    );
    Assert.Contains("exlib/blocktypes/", ex.Message);
    Assert.Throws<InvalidOperationException>(() =>
      Premise.Covers(defs, "nosuchdomain")
    );
  }
}
