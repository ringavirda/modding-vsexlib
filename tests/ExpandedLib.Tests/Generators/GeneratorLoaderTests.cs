using System;
using System.IO;
using Xunit;

namespace ExpandedLib.Tests;

public class GeneratorLoaderTests {
  // Fails when GeneratorLoader loads the dll from RepoPaths.Root's generators bin instead of the test bin.
  [Fact]
  public void LoadsTheGeneratorsDllFromTheTestBin() {
    var generator = GeneratorLoader.Load("ExConfigGenerator");

    string location = generator.GetType().Assembly.Location;
    Assert.Equal(
      Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "ExpandedLib.Generators.dll")
      ),
      Path.GetFullPath(location)
    );
  }
}
