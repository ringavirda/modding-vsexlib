using System.IO;
using System.IO.Compression;
using System.Linq;
using ExpandedLib.Registries;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests.Registries;

public class IncompatibleModsTests {
  private static IModLoader LoaderWith(params string[] enabled) {
    var loader = Substitute.For<IModLoader>();
    loader
      .IsModEnabled(Arg.Any<string>())
      .Returns(call => enabled.Contains(call.Arg<string>()));
    return loader;
  }

  [Fact]
  public void Nothing_enabled_means_no_message() =>
    Assert.Null(IncompatibleMods.Message(LoaderWith("iiex"), "0.8.0"));

  [Fact]
  public void Both_old_mods_are_named_with_the_fix() {
    string? msg = IncompatibleMods.Message(LoaderWith("smex", "ppex"), "0.8.0");
    Assert.NotNull(msg);
    Assert.StartsWith("exlib 0.8.0 does not work with", msg);
    Assert.Contains("Steelmaking Expanded", msg);
    Assert.Contains("Pipes and Power Expanded", msg);
    Assert.Contains("exlib 0.7.2", msg);
    Assert.Contains("Iron Industry Expanded", msg);
  }

  [Fact]
  public void One_old_mod_is_named_alone() {
    string? msg = IncompatibleMods.Message(LoaderWith("ppex"), "0.8.0");
    Assert.Contains("Pipes and Power Expanded", msg);
    Assert.DoesNotContain("Steelmaking", msg);
  }

  // A known mod's assembly fails to load beside this exlib (that is the whole point of this
  // class), and a boot against real ppex/smex 1.22 zips shows the loader then drops it from both
  // IModLoader.Mods and IsModEnabled - so a loader that reports nothing enabled must not silence
  // the message when the mod is still sitting in a Mods folder.
  [Fact]
  public void A_mod_the_loader_no_longer_reports_enabled_is_still_found_on_disk() {
    string root = Directory.CreateTempSubdirectory("exlib-incompatible-mods-test-").FullName;
    try {
      Directory.CreateDirectory(Path.Combine(root, "ppex"));
      File.WriteAllText(
        Path.Combine(root, "ppex", "modinfo.json"),
        """{"modid":"ppex","version":"0.6.8"}"""
      );

      string? msg = IncompatibleMods.Message(LoaderWith(), "0.8.0", [root]);
      Assert.NotNull(msg);
      Assert.Contains("Pipes and Power Expanded", msg);
    } finally {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public void A_zipped_mod_is_also_found_on_disk() {
    string root = Directory.CreateTempSubdirectory("exlib-incompatible-mods-test-").FullName;
    try {
      string zipPath = Path.Combine(root, "smex_0.9.8.zip");
      using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create)) {
        var entry = zip.CreateEntry("modinfo.json");
        using var writer = new StreamWriter(entry.Open());
        writer.Write("""{"modid":"smex","version":"0.9.8"}""");
      }

      string? msg = IncompatibleMods.Message(LoaderWith(), "0.8.0", [root]);
      Assert.NotNull(msg);
      Assert.Contains("Steelmaking Expanded", msg);
    } finally {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public void An_unrelated_mod_folder_is_ignored() {
    string root = Directory.CreateTempSubdirectory("exlib-incompatible-mods-test-").FullName;
    try {
      Directory.CreateDirectory(Path.Combine(root, "iiex"));
      File.WriteAllText(
        Path.Combine(root, "iiex", "modinfo.json"),
        """{"modid":"iiex","version":"0.1.0"}"""
      );

      Assert.Null(IncompatibleMods.Message(LoaderWith(), "0.8.0", [root]));
    } finally {
      Directory.Delete(root, recursive: true);
    }
  }
}
