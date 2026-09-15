using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Industry.Helpers;
using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.Pipes;

/// <summary>
/// Pipe-network vent strategy: a vanilla chimney capping the top connector of an
/// <see cref="IChimneyVentable"/> node draws gas out of the run and puffs smoke. The per-chimney draw
/// rate (L/s) is supplied by the mod that registers the "pipe" network.
/// </summary>
public sealed class ChimneyVent : IPipeVentStrategy {
  // Fire-loop restart interval (ms), just under the 9.26 s clip.
  private const long ChimneyFireLoopMs = 9000;

  // Last loop start (world ms) per drawing chimney.
  private readonly Dictionary<BlockPos, long> _chimneyFireMs = new();

  private readonly Func<float> _drawRatePerChimney;

  /// <param name="drawRatePerChimney">Gas (L/s) one chimney draws from the network, read live from
  /// the owning mod's config.</param>
  public ChimneyVent(Func<float> drawRatePerChimney) =>
    _drawRatePerChimney = drawRatePerChimney;

  /// <summary>True when <paramref name="block"/> is a chimney, matched on the code path (accepts
  /// modded variants too).</summary>
  public static bool IsChimney(Block? block) =>
    block?.Code?.Path?.Contains("chimney") == true;

  /// <inheritdoc/>
  public bool TryClassifyVent(
    IBlockAccessor blockAccessor,
    BlockNetworkNode node,
    BlockPos pos,
    BlockFacing face,
    Block neighbour,
    out BlockPos ventPos
  ) {
    // Only nodes that opt into chimney venting (IChimneyVentable), drawn on their top by a chimney.
    if (
      node is IChimneyVentable
      && face == BlockFacing.UP
      && IsChimney(neighbour)
    ) {
      ventPos = pos.AddCopy(face);
      return true;
    }
    ventPos = pos;
    return false;
  }

  /// <inheritdoc/>
  public float Vent(
    IReadOnlyList<BlockPos> vents,
    PipeNetworkState state,
    bool liquid,
    BlockNetworkModSystem manager
  ) {
    // Chimney draw (gas only): drawRate L/s per chimney-capped top connector.
    float vented = 0f;
    if (!liquid && vents.Count > 0 && state.Volume > 0) {
      vented = Math.Min(state.Volume, vents.Count * _drawRatePerChimney());
      state.Volume -= vented;

      foreach (BlockPos chimneyPos in vents) {
        if (manager.ServerWorld is { } smokeWorld)
          ExParticles.ChimneySmoke(smokeWorld, chimneyPos, state.MediumType);
        // Continuous fire loop while the chimney pulls the network's draught.
        if (manager.ServerWorld is { } w) {
          long last = _chimneyFireMs.GetValueOrDefault(chimneyPos);
          ExSounds.PlayLoop(
            w,
            chimneyPos,
            ExSounds.Fire,
            ref last,
            ChimneyFireLoopMs,
            volume: 0.3f,
            range: 20f
          );
          _chimneyFireMs[chimneyPos] = last;
        }
      }
    }

    // Drops sound-throttle stamps for chimneys no longer venting this network.
    if (_chimneyFireMs.Count > vents.Count) {
      List<BlockPos>? stale = null;
      foreach (var key in _chimneyFireMs.Keys)
        if (!vents.Contains(key))
          (stale ??= []).Add(key);
      if (stale != null)
        foreach (var key in stale)
          _chimneyFireMs.Remove(key);
    }

    return vented;
  }
}
