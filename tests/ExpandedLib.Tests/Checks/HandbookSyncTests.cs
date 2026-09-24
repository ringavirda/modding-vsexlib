using System;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="HandbookSync.Problems"/> and <see cref="HandbookSync.Check"/> over a planted
/// one-page handbook in a temporary repository.</summary>
public class HandbookSyncTests {
  private const string Domain = "plantedhandbook";
  private const string Source =
    "mods/plantedhandbook/docs/handbook/01-intro.html";
  private const string Descriptor =
    "mods/plantedhandbook/assets/plantedhandbook/config/handbook/01-intro.json";
  private const string EnglishLang =
    "mods/plantedhandbook/assets/plantedhandbook/lang/en.json";

  /// <summary>A repository holding one wired page, made the process's repo root until
  /// disposed.</summary>
  private sealed class Handbook : IDisposable {
    private readonly string? _previousRoot = DefinitionGoldens.RepoRootOverride;

    public Handbook() {
      Files.Write(Source, "<p>Hello\n  world</p>\n");
      Files.Write(
        Descriptor,
        """{ "text": "plantedhandbook:handbook-intro" }"""
      );
      Files.Write(
        EnglishLang,
        """{ "handbook-intro": "<p>Hello world</p>" }"""
      );
      DefinitionGoldens.RepoRootOverride = Files.Root;
    }

    public PlantedFiles Files { get; } = new();

    public HandbookSync.Page Page => Assert.Single(HandbookSync.Pages(Domain));

    public void Dispose() {
      DefinitionGoldens.RepoRootOverride = _previousRoot;
      Files.Dispose();
    }
  }

  #region Problems

  [Fact]
  [PlantedDefect(typeof(HandbookSync), nameof(HandbookSync.Problems))]
  public void A_shipped_page_with_no_authoring_source_is_reported() {
    using var handbook = new Handbook();
    System.IO.File.Delete(handbook.Files.Path(Source));

    Assert.Equal(
      [
        "plantedhandbook: shipped page 01-intro.json has no authoring source "
          + "mods/plantedhandbook/docs/handbook/01-*.html",
      ],
      HandbookSync.Problems(Domain)
    );
  }

  [Fact]
  [PlantedDefect(typeof(HandbookSync), nameof(HandbookSync.Problems))]
  public void A_page_descriptor_with_no_text_key_is_reported() {
    using var handbook = new Handbook();
    handbook.Files.Write(Descriptor, "{ }");

    Assert.Equal(
      [
        "plantedhandbook: page descriptor 01-intro.json declares no 'text' lang key",
      ],
      HandbookSync.Problems(Domain)
    );
  }

  [Fact]
  [PlantedDefect(typeof(HandbookSync), nameof(HandbookSync.Problems))]
  public void A_page_pointing_at_an_undefined_lang_key_is_reported() {
    using var handbook = new Handbook();
    handbook.Files.Write(EnglishLang, "{ }");

    Assert.Equal(
      [
        "plantedhandbook: page 01-intro.json points at 'handbook-intro', which "
          + "mods/plantedhandbook/assets/plantedhandbook/lang/en.json does not define",
      ],
      HandbookSync.Problems(Domain)
    );
  }

  [Fact]
  [PlantedDefect(typeof(HandbookSync), nameof(HandbookSync.Problems))]
  public void An_authoring_source_that_ships_nowhere_is_reported() {
    using var handbook = new Handbook();
    handbook.Files.Write(
      "mods/plantedhandbook/docs/handbook/02-extra.html",
      "<p/>"
    );

    Assert.Equal(
      [
        "plantedhandbook: authoring source 02-extra.html ships nowhere - no "
          + "mods/plantedhandbook/assets/plantedhandbook/config/handbook/02-*.json",
      ],
      HandbookSync.Problems(Domain)
    );
  }

  [Fact]
  public void A_wired_page_has_no_problems() {
    using var handbook = new Handbook();

    Assert.Empty(HandbookSync.Problems(Domain));
  }

  #endregion

  #region Check

  [Fact]
  [PlantedDefect(typeof(HandbookSync), nameof(HandbookSync.Check))]
  public void Shipped_text_that_differs_from_its_source_is_reported() {
    using var handbook = new Handbook();
    handbook.Files.Write(
      EnglishLang,
      """{ "handbook-intro": "<p>Hello there</p>" }"""
    );

    var (ok, message) = HandbookSync.Check(handbook.Page);

    Assert.False(ok);
    Assert.StartsWith(
      "plantedhandbook/01 [handbook-intro]: shipped text (18 chars) differs from "
        + "mods/plantedhandbook/docs/handbook/01-intro.html (18 chars)\n"
        + "  first difference at char 9",
      message
    );
  }

  [Fact]
  public void Shipped_text_equal_to_its_source_up_to_whitespace_passes() {
    using var handbook = new Handbook();

    Assert.Equal((true, ""), HandbookSync.Check(handbook.Page));
  }

  #endregion
}
