using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Catalogues;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.Casting;

/// <summary>How a station's pattern is moulded.</summary>
public enum MouldKind {
  /// <summary>Pressed into one bed of sand, its top face open.</summary>
  Open,

  /// <summary>Pressed into the drag and closed under a cope; only at a station whose kind lists it.</summary>
  Closed,
}

/// <summary>The order the pieces of a filling take metal.</summary>
public enum FillOrder {
  /// <summary>One piece at a time in pressing order, each fed through the one before.</summary>
  Chain,

  /// <summary>One row fed by a main runner: pairs of pieces in pressing order, outward from the drop.</summary>
  Pairs,

  /// <summary>Two rows, the back row behind the front: the front pieces take metal in pairs outward from the drop, each back piece after its front piece through the gate at the sand top.</summary>
  Rows,
}

/// <summary>The spec a mold pattern carries for one station, parsed from the pattern item's <c>mold</c> attribute.</summary>
/// <param name="Schema">Schema version of the declaration.</param>
/// <param name="Station">The code of the <see cref="CastingStationKind"/> this spec is for.</param>
/// <param name="Shape">The filling-shape asset shown while the impression is present.</param>
/// <param name="Pieces">How many castings one pour at this station yields.</param>
/// <param name="Capacity">Units of metal the impression holds with every piece pressed: <paramref name="Pieces"/> times the pattern's <c>units</c>.</param>
/// <param name="Cavity">Molten-renderer fill boxes in 16-space, one per piece, in pressing order.</param>
/// <param name="Fill">The order the pressed pieces take metal.</param>
/// <param name="Output">What a full piece yields on shake-out; its quantity is <paramref name="Pieces"/>, the most a pour casts.</param>
/// <param name="MinPourTemp">Pour temperature (C) below which a fill is a misrun; 0 disables the check.</param>
/// <param name="Mould">How the pattern is moulded; <see cref="MouldKind.Open"/> unless declared.</param>
/// <param name="Share">Units of metal the pour's head holds beyond the pieces, returned as scrap on shake-out; 0 unless declared.</param>
public sealed record MoldSpec(
  int Schema,
  string Station,
  string Shape,
  int Pieces,
  int Capacity,
  Cuboidf[] Cavity,
  FillOrder Fill,
  JsonItemStack Output,
  float MinPourTemp,
  MouldKind Mould = MouldKind.Open,
  int Share = 0
) {
  /// <summary>The attribute key a pattern carries its spec under.</summary>
  public const string AttributeKey = "mold";

  /// <summary>Units of metal one piece holds.</summary>
  public int UnitsPerPiece => Capacity / Pieces;

  /// <summary>The schema this parser writes and reads up to. Raise it only alongside the fallback that
  /// reads the form it replaces (<see cref="SpecSchema"/>).</summary>
  public const int CurrentSchema = SpecSchema.First;

  /// <summary>
  /// Parses a pattern's <c>mold</c> attribute and returns the spec for station <paramref name="station"/>.
  /// Returns false with a human-readable <paramref name="error"/> when the attribute is malformed or
  /// the pattern is not cast at <paramref name="station"/>.
  /// </summary>
  /// <param name="mold">The pattern's <c>mold</c> attribute; null or absent is an error.</param>
  /// <param name="station">A station code, compared ignoring case.</param>
  /// <param name="stations">The kinds a station entry may name; <see cref="CastingStationRegistry.Shared"/> when null.</param>
  public static bool TryParse(
    JsonObject? mold,
    string station,
    out MoldSpec? spec,
    out string? error,
    CastingStationRegistry? stations = null
  ) {
    spec = null;
    if (
      !TryParseAll(mold, out IReadOnlyList<MoldSpec> specs, out error, stations)
    )
      return false;
    spec = specs.FirstOrDefault(s =>
      string.Equals(s.Station, station, StringComparison.OrdinalIgnoreCase)
    );
    if (spec is null)
      error = $"not cast at {station.ToLowerInvariant()}";
    return spec is not null;
  }

  /// <summary>
  /// Parses and validates a pattern's <c>mold</c> attribute and returns one spec per station entry, in
  /// the order declared. Returns false with a human-readable <paramref name="error"/> naming the
  /// malformed field, or the station code that is not registered; <paramref name="specs"/> is then empty.
  /// </summary>
  /// <param name="mold">The pattern's <c>mold</c> attribute; null or absent is an error.</param>
  /// <param name="stations">The kinds a station entry may name; <see cref="CastingStationRegistry.Shared"/> when null.</param>
  public static bool TryParseAll(
    JsonObject? mold,
    out IReadOnlyList<MoldSpec> specs,
    out string? error,
    CastingStationRegistry? stations = null
  ) {
    specs = [];
    error = null;
    stations ??= CastingStationRegistry.Shared;

    if (mold is not { Exists: true }) {
      error = $"missing '{AttributeKey}' attribute";
      return false;
    }

    if (!SpecSchema.TryRead(mold, CurrentSchema, out int schema, out error))
      return false;

    JsonItemStack? output = mold["output"].AsObject<JsonItemStack>(null);
    if (output?.Code is null) {
      error = "missing 'output' stack";
      return false;
    }

    int units = mold["units"].AsInt(0);
    if (units <= 0) {
      error = $"units must be > 0 (was {units})";
      return false;
    }

    float minPourTemp = mold["minPourTemp"].AsFloat(0f);

    JsonObject[]? stationNodes = mold["stations"].AsArray();
    if (stationNodes is not { Length: > 0 }) {
      error = "missing 'stations'";
      return false;
    }

    var parsed = new List<MoldSpec>(stationNodes.Length);
    foreach (JsonObject node in stationNodes) {
      if (
        !TryParseStation(
          node,
          schema,
          stations,
          units,
          minPourTemp,
          output,
          out MoldSpec? station,
          out error
        )
      )
        return false;
      if (parsed.Any(s => s.Station == station!.Station)) {
        error = $"station '{node["size"].AsString()}' is listed twice";
        return false;
      }
      parsed.Add(station!);
    }

    specs = parsed;
    return true;
  }

  internal static bool TryParseMould(string name, out MouldKind mould) {
    switch (name) {
      case "open":
        mould = MouldKind.Open;
        return true;
      case "closed":
        mould = MouldKind.Closed;
        return true;
      default:
        mould = default;
        return false;
    }
  }

  private static bool TryParseStation(
    JsonObject node,
    int schema,
    CastingStationRegistry stations,
    int units,
    float minPourTemp,
    JsonItemStack output,
    out MoldSpec? spec,
    out string? error
  ) {
    spec = null;

    string sizeStr = node["size"].AsString("");
    if (!stations.TryGet(sizeStr, out CastingStationKind? kind)) {
      string known = string.Join(", ", stations.All.Select(k => $"'{k.Code}'"));
      error =
        $"invalid size '{sizeStr}' (expected one of {(known.Length > 0 ? known : "none registered")})";
      return false;
    }

    string shape = node["shape"].AsString("");
    if (string.IsNullOrWhiteSpace(shape)) {
      error = "missing 'shape'";
      return false;
    }

    int pieces = node["pieces"].AsInt(0);
    if (pieces < 1) {
      error = $"pieces must be >= 1 (was {pieces})";
      return false;
    }

    JsonObject[]? cavityNodes = node["cavity"].AsArray();
    if (cavityNodes is not { Length: > 0 }) {
      error = "missing 'cavity' boxes";
      return false;
    }
    var boxes = new List<Cuboidf>(cavityNodes.Length);
    foreach (JsonObject box in cavityNodes) {
      Cuboidf? cuboid = box.AsObject<Cuboidf>(null);
      if (
        cuboid is null
        || cuboid.X2 <= cuboid.X1
        || cuboid.Y2 <= cuboid.Y1
        || cuboid.Z2 <= cuboid.Z1
      ) {
        error = "a 'cavity' box is malformed (need x1<x2, y1<y2, z1<z2)";
        return false;
      }
      boxes.Add(cuboid);
    }

    string fillStr = node["fill"].AsString("chain");
    FillOrder? fill = fillStr switch {
      "chain" => FillOrder.Chain,
      "pairs" => FillOrder.Pairs,
      "rows" => FillOrder.Rows,
      _ => null,
    };
    if (fill is null) {
      error = $"invalid fill '{fillStr}' (expected 'chain', 'pairs' or 'rows')";
      return false;
    }
    if (fill == FillOrder.Rows && pieces % 2 != 0) {
      error = $"fill 'rows' needs an even number of pieces (was {pieces})";
      return false;
    }

    string mouldStr = node["mould"].AsString("open");
    if (!TryParseMould(mouldStr, out MouldKind mould)) {
      error = $"invalid mould '{mouldStr}' (expected 'open' or 'closed')";
      return false;
    }
    if (!kind.Takes(mould)) {
      error =
        $"mould '{mouldStr}' is not taken by '{kind.Code}' (takes {string.Join(", ", kind.Moulds.Select(m => $"'{m.ToString().ToLowerInvariant()}'"))})";
      return false;
    }

    int share = node["share"].AsInt(0);
    if (share < 0) {
      error = $"share must be >= 0 (was {share})";
      return false;
    }

    JsonItemStack stationOutput = output.Clone();
    stationOutput.Quantity = pieces;

    error = null;
    spec = new MoldSpec(
      schema,
      kind.Code,
      shape,
      pieces,
      pieces * units,
      boxes.ToArray(),
      fill.Value,
      stationOutput,
      minPourTemp,
      mould,
      share
    );
    return true;
  }
}
