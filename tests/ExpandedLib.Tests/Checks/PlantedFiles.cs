using System;
using System.IO;

namespace ExpandedLib.Tests;

/// <summary>A temporary directory for a planted fixture's files, deleted with everything in it on
/// dispose.</summary>
internal sealed class PlantedFiles : IDisposable {
  public PlantedFiles() {
    Root = System.IO.Path.Combine(
      System.IO.Path.GetTempPath(),
      "exlib_planted_" + Guid.NewGuid().ToString("N")
    );
    Directory.CreateDirectory(Root);
  }

  /// <summary>The directory's absolute path.</summary>
  public string Root { get; }

  /// <summary>The absolute path of <paramref name="relative"/> ('/'-separated) under
  /// <see cref="Root"/>.</summary>
  public string Path(string relative) =>
    System.IO.Path.Combine(
      Root,
      relative.Replace('/', System.IO.Path.DirectorySeparatorChar)
    );

  /// <summary>Writes <paramref name="text"/> to <paramref name="relative"/>, creating its folders,
  /// and returns the file's absolute path.</summary>
  public string Write(string relative, string text) {
    string file = Path(relative);
    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
    File.WriteAllText(file, text);
    return file;
  }

  public void Dispose() => Directory.Delete(Root, recursive: true);
}
