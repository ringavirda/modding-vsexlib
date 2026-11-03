using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace ExpandedLib.Testing.Live;

/// <summary>A probe run of prospecting pick density readings: loads the column under each block
/// position, asks the game's own pick for its reading there and writes
/// <c>&lt;dataPath&gt;/probe/readings-&lt;seed&gt;-&lt;time&gt;.json</c>.</summary>
internal sealed class ProbeReadings {
  private const int ChunkSize = 32;
  private static readonly MethodInfo GenProbeResults =
    typeof(ItemProspectingPick).GetMethod(
      "GenProbeResults",
      BindingFlags.Instance | BindingFlags.NonPublic
    )!;

  private readonly ICoreServerAPI _api;
  private readonly IReadOnlyList<(int X, int Z)> _positions;
  private readonly ItemProspectingPick _pick;
  private readonly JArray _readings = [];
  private readonly long _listener;
  private int _next;
  private int _loaded;

  /// <summary>Whether positions are still waiting; false once the file is written.</summary>
  public bool Active { get; private set; } = true;

  /// <summary>Parses <c>&lt;x&gt; &lt;z&gt; ...</c> pairs of block coordinates. Returns null with the
  /// reason in <paramref name="error"/> when a word is not a whole number, the count is odd or no pair
  /// is given.</summary>
  public static IReadOnlyList<(int X, int Z)>? ParsePositions(
    string text,
    out string error
  ) {
    string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    error = "";
    var numbers = new List<int>();
    foreach (string word in words) {
      if (
        !int.TryParse(
          word,
          NumberStyles.Integer,
          CultureInfo.InvariantCulture,
          out int n
        )
      ) {
        error = $"'{word}' is not a whole number.";
        return null;
      }
      numbers.Add(n);
    }
    if (numbers.Count == 0 || numbers.Count % 2 != 0) {
      error = "Usage: reading <x> <z> [<x> <z>...]";
      return null;
    }
    return Enumerable
      .Range(0, numbers.Count / 2)
      .Select(i => (numbers[2 * i], numbers[2 * i + 1]))
      .ToArray();
  }

  /// <summary>Starts the run on the main thread. Fails with an exception message when the world has no
  /// prospecting pick item.</summary>
  public ProbeReadings(
    ICoreServerAPI api,
    IReadOnlyList<(int X, int Z)> positions
  ) {
    _api = api;
    _positions = positions;
    _pick =
      api.World.Items.OfType<ItemProspectingPick>().FirstOrDefault()
      ?? throw new InvalidOperationException(
        "The world has no prospecting pick."
      );
    _listener = api.Event.RegisterGameTickListener(Tick, 20);
    LoadNext();
  }

  private void LoadNext() {
    (int x, int z) = _positions[_next];
    _loaded = 0;
    _api.WorldManager.LoadChunkColumnPriority(
      x / ChunkSize,
      z / ChunkSize,
      x / ChunkSize,
      z / ChunkSize,
      new ChunkLoadOptions {
        KeepLoaded = true,
        OnLoaded = () => System.Threading.Volatile.Write(ref _loaded, 1),
      }
    );
  }

  private void Tick(float dt) {
    if (!Active || System.Threading.Volatile.Read(ref _loaded) == 0)
      return;
    System.Threading.Volatile.Write(ref _loaded, 0);
    (int x, int z) = _positions[_next];
    var reading = (PropickReading?)
      GenProbeResults.Invoke(_pick, [_api.World, new BlockPos(x, 0, z)]);
    _readings.Add(
      new JObject {
        ["x"] = x,
        ["z"] = z,
        ["codes"] = new JObject(
          (reading?.OreReadings ?? [])
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new JProperty(
              p.Key,
              new JObject {
                ["totalFactor"] = p.Value.TotalFactor,
                ["ppt"] = p.Value.PartsPerThousand,
              }
            ))
        ),
      }
    );
    _api.WorldManager.UnloadChunkColumn(x / ChunkSize, z / ChunkSize);
    _next++;
    if (_next < _positions.Count) {
      LoadNext();
      return;
    }
    Finish();
  }

  private void Finish() {
    Active = false;
    _api.Event.UnregisterGameTickListener(_listener);
    string dir = Path.Combine(GamePaths.DataPath, "probe");
    Directory.CreateDirectory(dir);
    string path = Path.Combine(
      dir,
      $"readings-{_api.WorldManager.Seed}-{DateTime.UtcNow:yyyyMMddHHmmss}.json"
    );
    File.WriteAllText(
      path,
      new JObject {
        ["seed"] = _api.WorldManager.Seed,
        ["readings"] = _readings,
      }.ToString(Formatting.Indented)
    );
    _api.Logger.Notification("[probe] readings done: {0}", path);
  }
}
