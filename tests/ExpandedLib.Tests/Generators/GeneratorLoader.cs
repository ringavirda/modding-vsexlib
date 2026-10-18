using System;
using System.IO;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace ExpandedLib.Tests;

/// <summary>Loads the <c>ExpandedLib.Generators.dll</c> copied into the test bin, and
/// instantiates one of its generator types by name.</summary>
internal static class GeneratorLoader {
  /// <summary>The path of the generators dll <see cref="Load"/> reads.</summary>
  public static string DllPath { get; } =
    Path.Combine(AppContext.BaseDirectory, "ExpandedLib.Generators.dll");

  public static IIncrementalGenerator Load(string generatorTypeName) {
    string path = DllPath;
    Assembly generators = Assembly.LoadFrom(path);
    Type generatorType =
      generators.GetType($"ExpandedLib.Generators.{generatorTypeName}")
      ?? throw new InvalidOperationException(
        $"{path} carries no ExpandedLib.Generators.{generatorTypeName} type."
      );
    return (IIncrementalGenerator)Activator.CreateInstance(generatorType)!;
  }
}
