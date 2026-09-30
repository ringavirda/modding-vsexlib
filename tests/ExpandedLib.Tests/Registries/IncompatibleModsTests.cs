using System.IO;
using System.IO.Compression;
using System.Linq;
using ExpandedLib.Registries;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests.Registries;

public class IncompatibleModsTests {
  private static IModLoader LoaderWith(
    params (string Id, string Version)[] loaded
  ) {
    var loader = Substitute.For<IModLoader>();
    loader
      .IsModEnabled(Arg.Any<string>())
      .Returns(call => loaded.Any(m => m.Id == call.Arg<string>()));
    foreach (var (id, version) in loaded) {
      var mod = Substitute.For<Mod>();
      typeof(Mod)
        .GetProperty("Info")!
        .SetValue(mod, new ModInfo { ModID = id, Version = version });
      loader.GetMod(id).Returns(mod);
    }
    return loader;
  }

  // One folder mod, or one zip mod when fileName ends in .zip, declaring modid and version.
  private static void PutOnDisk(
    string root,
    string fileName,
    string modId,
    string version
  ) {
    string json = $$"""{"modid":"{{modId}}","version":"{{version}}"}""";
    string path = Path.Combine(root, fileName);
    if (!fileName.EndsWith(".zip")) {
      Directory.CreateDirectory(path);
      File.WriteAllText(Path.Combine(path, "modinfo.json"), json);
      return;
    }
    using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
    using var writer = new StreamWriter(zip.CreateEntry("modinfo.json").Open());
    writer.Write(json);
  }

  private static string TempRoot() =>
    Directory.CreateTempSubdirectory("exlib-incompatible-mods-test-").FullName;

  [Fact]
  public void Nothing_enabled_means_no_message() =>
    Assert.Null(
      IncompatibleMods.Message(LoaderWith(("iiex", "0.1.0")), "0.8.0", [])
    );

  [Fact]
  public void Both_old_mods_are_named_with_the_versions_to_install() {
    string? msg = IncompatibleMods.Message(
      LoaderWith(("smex", "0.9.8"), ("ppex", "0.6.8")),
      "0.8.0",
      []
    );
    Assert.NotNull(msg);
    Assert.StartsWith("exlib 0.8.0 needs", msg);
    Assert.Contains("Steelmaking Expanded 0.10.1 or later", msg);
    Assert.Contains("Pipes and Power Expanded 0.7.1 or later", msg);
    Assert.Contains("exlib 0.7.2", msg);
  }

  // Fails when the ppex floor stays at 0.7.0: the posted 0.7.0 build is accepted.
  [Fact]
  public void The_posted_ppex_and_smex_are_named_and_told_to_keep_exlib_0_8_3() {
    string? msg = IncompatibleMods.Message(
      LoaderWith(("ppex", "0.7.0"), ("smex", "0.10.0")),
      "0.8.4",
      []
    );
    Assert.NotNull(msg);
    Assert.Contains("Pipes and Power Expanded 0.7.1 or later", msg);
    Assert.Contains("Steelmaking Expanded 0.10.1 or later", msg);
    Assert.Contains("keep exlib 0.8.3", msg);
  }

  // Fails when the smex floor stays at 0.10.0, with ppex current.
  [Fact]
  public void The_posted_smex_alone_is_named() {
    string? msg = IncompatibleMods.Message(
      LoaderWith(("ppex", "0.7.1"), ("smex", "0.10.0")),
      "0.8.4",
      []
    );
    Assert.Contains("Steelmaking Expanded 0.10.1 or later", msg);
    Assert.DoesNotContain("Pipes", msg);
  }

  // Fails when the kept version is not chosen per line: ppex 0.6.9 is told 0.8.3.
  [Fact]
  public void A_mod_from_the_older_line_is_told_to_keep_exlib_0_7_2() {
    string? msg = IncompatibleMods.Message(
      LoaderWith(("ppex", "0.6.9")),
      "0.8.4",
      []
    );
    Assert.Contains("keep exlib 0.7.2", msg);
  }

  // Fails when one mod on the prior line decides for all: smex 0.9.8 needs 0.7.2 beside ppex 0.7.0.
  [Fact]
  public void One_mod_from_the_older_line_among_posted_ones_keeps_exlib_0_7_2() {
    string? msg = IncompatibleMods.Message(
      LoaderWith(("ppex", "0.7.0"), ("smex", "0.9.8")),
      "0.8.4",
      []
    );
    Assert.Contains("keep exlib 0.7.2", msg);
  }

  // Fails when the disk copy's version is not read for the kept version: a zip of 0.7.0 is told 0.7.2.
  [Fact]
  public void The_posted_ppex_on_disk_only_is_told_to_keep_exlib_0_8_3() {
    string root = TempRoot();
    try {
      PutOnDisk(root, "ppex_0.7.0.zip", "ppex", "0.7.0");

      string? msg = IncompatibleMods.Message(LoaderWith(), "0.8.4", [root]);
      Assert.Contains("keep exlib 0.8.3", msg);
    } finally {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public void One_old_mod_is_named_alone() {
    string? msg = IncompatibleMods.Message(
      LoaderWith(("ppex", "0.6.8"), ("smex", "0.10.1")),
      "0.8.0",
      []
    );
    Assert.Contains("Pipes and Power Expanded", msg);
    Assert.DoesNotContain("Steelmaking", msg);
    Assert.DoesNotContain(" them", msg);
  }

  // Fails when the version is ignored: every loaded ppex or smex is refused.
  [Theory]
  [InlineData("ppex", "0.7.1")]
  [InlineData("smex", "0.10.1")]
  public void A_ported_version_loaded_is_accepted(
    string modId,
    string version
  ) =>
    Assert.Null(
      IncompatibleMods.Message(LoaderWith((modId, version)), "0.8.0", [])
    );

  // A failed load is absent from the loader; its copy on disk still names the version to install.
  [Theory]
  [InlineData(
    "ppex",
    "ppex",
    "0.6.8",
    "Pipes and Power Expanded 0.7.1 or later"
  )]
  [InlineData("smex", "smex", "0.9.8", "Steelmaking Expanded 0.10.1 or later")]
  [InlineData(
    "ppex",
    "ppex_0.6.8.zip",
    "0.6.8",
    "Pipes and Power Expanded 0.7.1 or later"
  )]
  [InlineData(
    "smex",
    "smex_0.9.8.zip",
    "0.9.8",
    "Steelmaking Expanded 0.10.1 or later"
  )]
  public void An_old_version_on_disk_only_is_refused(
    string modId,
    string fileName,
    string version,
    string named
  ) {
    string root = TempRoot();
    try {
      PutOnDisk(root, fileName, modId, version);

      string? msg = IncompatibleMods.Message(LoaderWith(), "0.8.0", [root]);
      Assert.NotNull(msg);
      Assert.Contains(named, msg);
    } finally {
      Directory.Delete(root, recursive: true);
    }
  }

  // Fails when the disk is read before the loaded version: the old zip would refuse the mod.
  [Theory]
  [InlineData("ppex", "0.6.8", "0.7.1")]
  [InlineData("smex", "0.9.8", "0.10.1")]
  public void An_old_zip_beside_a_loaded_ported_version_is_accepted(
    string modId,
    string oldVersion,
    string loadedVersion
  ) {
    string root = TempRoot();
    try {
      PutOnDisk(root, $"{modId}_{oldVersion}.zip", modId, oldVersion);

      Assert.Null(
        IncompatibleMods.Message(
          LoaderWith((modId, loadedVersion)),
          "0.8.0",
          [root]
        )
      );
    } finally {
      Directory.Delete(root, recursive: true);
    }
  }

  // Fails when any old copy on disk refuses the mod rather than the newest.
  [Fact]
  public void A_ported_copy_on_disk_beside_an_old_one_is_accepted() {
    string root = TempRoot();
    try {
      PutOnDisk(root, "ppex_0.6.8.zip", "ppex", "0.6.8");
      PutOnDisk(root, "ppex_0.7.1.zip", "ppex", "0.7.1");

      Assert.Null(IncompatibleMods.Message(LoaderWith(), "0.8.0", [root]));
    } finally {
      Directory.Delete(root, recursive: true);
    }
  }

  // Fails when a missing version reaches GameVersion, which throws on an empty string.
  [Fact]
  public void A_loaded_mod_without_a_version_is_refused() =>
    Assert.NotNull(
      IncompatibleMods.Message(LoaderWith(("ppex", "")), "0.8.0", [])
    );

  [Fact]
  public void An_unrelated_mod_folder_is_ignored() {
    string root = TempRoot();
    try {
      PutOnDisk(root, "iiex", "iiex", "0.1.0");

      Assert.Null(IncompatibleMods.Message(LoaderWith(), "0.8.0", [root]));
    } finally {
      Directory.Delete(root, recursive: true);
    }
  }
}
