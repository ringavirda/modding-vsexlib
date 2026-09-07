using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ExpandedLib.Testing;
using Microsoft.CodeAnalysis;

namespace ExpandedLib.Tests;

/// <summary>Loads whichever build of <c>ExpandedLib.Generators.dll</c> ran most recently, and
/// instantiates one of its generator types by name - the generator ships no runtime assembly (it
/// is analyzer-only, referenced by every mod project at compile time only), so its already-built
/// analyzer DLL is loaded by reflection rather than by adding a compile reference.</summary>
internal static class GeneratorLoader {
  public static IIncrementalGenerator Load(string generatorTypeName) {
    string binRoot = Path.Combine(RepoPaths.Root, "generators", "bin");
    string path = Directory
      .EnumerateFiles(binRoot, "ExpandedLib.Generators.dll", SearchOption.AllDirectories)
      .OrderByDescending(File.GetLastWriteTimeUtc)
      .FirstOrDefault()
      ?? throw new InvalidOperationException(
        $"No built ExpandedLib.Generators.dll found under {binRoot}."
      );

    Assembly generators = Assembly.LoadFrom(path);
    Type generatorType =
      generators.GetType($"ExpandedLib.Generators.{generatorTypeName}")
      ?? throw new InvalidOperationException(
        $"{path} carries no ExpandedLib.Generators.{generatorTypeName} type."
      );
    return (IIncrementalGenerator)Activator.CreateInstance(generatorType)!;
  }
}
