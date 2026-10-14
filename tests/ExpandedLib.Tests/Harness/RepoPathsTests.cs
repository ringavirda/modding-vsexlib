using System;
using System.IO;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="RepoPaths"/>'s domain lookup: a known domain resolves to its declared mod,
/// an unknown one falls back to a folder of its own name, and <see cref="RepoPaths.Register"/>
/// declares a new domain.</summary>
public class RepoPathsTests {
  [Fact]
  public void Assets_of_an_unknown_domain_falls_back_to_a_same_named_mod_folder() {
    string path = RepoPaths.Assets("nosuchdomain");
    Assert.Equal(
      Path.Combine(
        RepoPaths.Root,
        "mods",
        "nosuchdomain",
        "assets",
        "nosuchdomain"
      ),
      path
    );
  }

  [Fact]
  public void Register_makes_a_new_domain_resolve_to_its_declared_mod() {
    RepoPaths.Register("harnesstest-domain", "harnesstest-mod");
    string path = RepoPaths.Assets("harnesstest-domain");
    Assert.Equal(
      Path.Combine(
        RepoPaths.Root,
        "mods",
        "harnesstest-mod",
        "assets",
        "harnesstest-domain"
      ),
      path
    );
  }

  [Fact]
  public void Register_replaces_an_earlier_registration_for_the_same_domain() {
    RepoPaths.Register("harnesstest-replace", "harnesstest-first");
    RepoPaths.Register("harnesstest-replace", "harnesstest-second");
    string path = RepoPaths.Assets("harnesstest-replace");
    Assert.Equal(
      Path.Combine(
        RepoPaths.Root,
        "mods",
        "harnesstest-second",
        "assets",
        "harnesstest-replace"
      ),
      path
    );
  }
}

/// <summary><see cref="RepoPaths.DocsRoot"/>, <see cref="RepoPaths.Wiki"/> and
/// <see cref="RepoPaths.Docs"/> over a planted repository beside a planted docs checkout.</summary>
[Collection(RepoRootCollection.Name)]
public class RepoPathsDocsRootTests {
  /// <summary>A workspace holding <c>repo/exmod.json</c> with <paramref name="manifest"/>, the repo
  /// made the process's repo root until disposed.</summary>
  private sealed class Workspace : IDisposable {
    private readonly string? _previousRoot = DefinitionGoldens.RepoRootOverride;
    private readonly string? _previousDocsRoot =
      Environment.GetEnvironmentVariable("EXDOCS_ROOT");

    public Workspace(string manifest) {
      Environment.SetEnvironmentVariable("EXDOCS_ROOT", null);
      Files.Write("repo/exmod.json", manifest);
      DefinitionGoldens.RepoRootOverride = Files.Path("repo");
    }

    public PlantedFiles Files { get; } = new();

    public void Dispose() {
      DefinitionGoldens.RepoRootOverride = _previousRoot;
      Environment.SetEnvironmentVariable("EXDOCS_ROOT", _previousDocsRoot);
      Files.Dispose();
    }
  }

  private const string WithDocs =
    """{ "mods": { "iiex": { "path": "mods/iiex" } }, "docs": "../exdocs/exmods" }""";

  // Fails when Docs keeps mods/<id>/docs while exmod.json names a docs root.
  [Fact]
  public void A_docs_entry_roots_the_wiki_and_each_mods_docs_beside_the_repository() {
    using var workspace = new Workspace(WithDocs);
    string docsRoot = workspace.Files.Path("exdocs/exmods");
    Directory.CreateDirectory(docsRoot);

    Assert.Equal(docsRoot, RepoPaths.DocsRoot);
    Assert.Equal(Path.Combine(docsRoot, "wiki"), RepoPaths.Wiki);
    Assert.Equal(Path.Combine(docsRoot, "iiex"), RepoPaths.Docs("iiex"));
  }

  // Fails when EXDOCS_ROOT is ignored, or when the whole entry rather than its last folder is
  // appended to it.
  [Fact]
  public void EXDOCS_ROOT_points_the_docs_root_at_the_last_folder_of_the_entry_inside_it() {
    using var workspace = new Workspace(WithDocs);
    string checkout = workspace.Files.Path("lane/exdocs-foo");
    string docsRoot = Path.Combine(checkout, "exmods");
    Directory.CreateDirectory(docsRoot);
    Environment.SetEnvironmentVariable("EXDOCS_ROOT", checkout);

    Assert.Equal(docsRoot, RepoPaths.DocsRoot);
    Assert.Equal(Path.Combine(docsRoot, "wiki"), RepoPaths.Wiki);
    Assert.Equal(Path.Combine(docsRoot, "iiex"), RepoPaths.Docs("iiex"));
  }

  // Fails when an empty EXDOCS_ROOT replaces the manifest's entry.
  [Fact]
  public void An_empty_EXDOCS_ROOT_keeps_the_manifests_entry() {
    using var workspace = new Workspace(WithDocs);
    string docsRoot = workspace.Files.Path("exdocs/exmods");
    Directory.CreateDirectory(docsRoot);
    Environment.SetEnvironmentVariable("EXDOCS_ROOT", "");

    Assert.Equal(docsRoot, RepoPaths.DocsRoot);
  }

  // Fails when a missing folder under EXDOCS_ROOT falls back to the manifest's entry.
  [Fact]
  public void EXDOCS_ROOT_without_the_entrys_folder_throws_naming_the_path() {
    using var workspace = new Workspace(WithDocs);
    Directory.CreateDirectory(workspace.Files.Path("exdocs/exmods"));
    string checkout = workspace.Files.Path("lane/exdocs-foo");
    Environment.SetEnvironmentVariable("EXDOCS_ROOT", checkout);

    var ex = Assert.Throws<DirectoryNotFoundException>(() => RepoPaths.Wiki);
    Assert.Contains(Path.Combine(checkout, "exmods"), ex.Message);
  }

  // Fails when a manifest without a docs entry resolves a docs root anyway.
  [Fact]
  public void Without_a_docs_entry_the_docs_stay_in_the_mod_folder() {
    using var workspace = new Workspace(
      """{ "mods": { "iiex": { "path": "mods/iiex" } } }"""
    );

    Assert.Null(RepoPaths.DocsRoot);
    Assert.Equal(workspace.Files.Path("repo/wiki"), RepoPaths.Wiki);
    Assert.Equal(
      workspace.Files.Path("repo/mods/iiex/docs"),
      RepoPaths.Docs("iiex")
    );
  }

  // Fails when a docs root that does not exist resolves instead of throwing.
  [Fact]
  public void A_docs_entry_naming_a_missing_folder_throws_naming_the_path() {
    using var workspace = new Workspace(WithDocs);
    string docsRoot = workspace.Files.Path("exdocs/exmods");

    var ex = Assert.Throws<DirectoryNotFoundException>(() =>
      RepoPaths.Docs("iiex")
    );
    Assert.Contains(docsRoot, ex.Message, StringComparison.Ordinal);
    Assert.Contains("Clone exdocs", ex.Message, StringComparison.Ordinal);
    Assert.Throws<DirectoryNotFoundException>(() => RepoPaths.Wiki);
  }
}
