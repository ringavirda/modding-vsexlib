using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ExpandedLib.Testing;

/// <summary>Points the process's Cairo and fontconfig at the game's own fonts, once, before the first
/// surface is made.</summary>
internal static class CairoNative {
  private static readonly object Gate = new();
  private static bool _ready;

  [DllImport("libc", EntryPoint = "setenv")]
  private static extern int SetEnv(string name, string value, int overwrite);

  /// <summary>Makes the game's Cairo binding load the system Cairo and sets <c>FONTCONFIG_FILE</c> to
  /// a configuration that lists only the game's <c>assets/game/fonts</c>. A process whose fontconfig
  /// already started keeps the fonts it found.</summary>
  /// <exception cref="InvalidOperationException">No game install is provisioned, or it has no fonts
  /// folder.</exception>
  internal static void Ensure() {
    lock (Gate) {
      if (_ready)
        return;
      string install =
        VsAssemblyResolver.InstallPath
        ?? throw new InvalidOperationException(
          "a window render needs a provisioned game install for its fonts"
        );
      string fonts = Path.Combine(install, "assets", "game", "fonts");
      if (!Directory.Exists(fonts))
        throw new InvalidOperationException(
          $"the game install has no fonts folder at {fonts}"
        );
      if (!OperatingSystem.IsWindows()) {
        string dir = Path.Combine(
          Path.GetTempPath(),
          "exlib-window-fonts-" + Environment.UserName
        );
        Directory.CreateDirectory(dir);
        string conf = Path.Combine(dir, "fonts.conf");
        File.WriteAllText(
          conf,
          "<?xml version=\"1.0\"?>\n"
            + "<!DOCTYPE fontconfig SYSTEM \"fonts.dtd\">\n"
            + $"<fontconfig><dir>{fonts}</dir><cachedir>{Path.Combine(dir, "cache")}</cachedir></fontconfig>\n"
        );
        SetEnv("FONTCONFIG_FILE", conf, 1);
        NativeLibrary.SetDllImportResolver(
          typeof(Cairo.Context).Assembly,
          Resolve
        );
      }
      _ready = true;
    }
  }

  private static IntPtr Resolve(
    string name,
    Assembly assembly,
    DllImportSearchPath? path
  ) {
    if (!name.StartsWith("libcairo", StringComparison.Ordinal))
      return IntPtr.Zero;
    string[] candidates = OperatingSystem.IsMacOS()
      ? ["libcairo.2.dylib", "libcairo.dylib"]
      : ["libcairo.so.2", "libcairo.so"];
    foreach (string c in candidates)
      if (NativeLibrary.TryLoad(c, out IntPtr handle))
        return handle;
    return IntPtr.Zero;
  }
}
