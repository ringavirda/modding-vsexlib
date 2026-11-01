#if GAME_GE_1_22
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.ServerMods;

namespace ExpandedLib.WorldGen;

/// <summary>A rock block a deposit replaces and the value its wildcard matched, or null when the
/// host code has no wildcard.</summary>
internal sealed record HostRock(Block Block, string? Value);

/// <summary>The blocks a deposit's <c>inBlock</c> matches and the codes that follow from each.</summary>
internal sealed class HostRocks
{
  private readonly ICoreServerAPI _api;
  private readonly string _fromFile;
  private readonly string? _key;
  private readonly HashSet<string> _missing = [];

  /// <summary>The hosts by block id.</summary>
  public Dictionary<int, HostRock> ById { get; } = [];

  /// <summary>Searches the world for the blocks <paramref name="inBlock"/> names.</summary>
  /// <param name="api">The server api.</param>
  /// <param name="inBlock">The host code, with its allowed variants.</param>
  /// <param name="fromFile">The deposit file, named in log lines.</param>
  public HostRocks(ICoreServerAPI api, DepositBlock inBlock, string fromFile)
  {
    _api = api;
    _fromFile = fromFile;
    _key = inBlock.Name;
    foreach (Block block in api.World.SearchBlocks(inBlock.Code))
    {
      if (
        inBlock.AllowedVariants != null
        && !WildcardUtil.Match(
          inBlock.Code,
          block.Code,
          inBlock.AllowedVariants
        )
      )
      {
        continue;
      }
      if (
        inBlock.AllowedVariantsByInBlock != null
        && !inBlock.AllowedVariantsByInBlock.ContainsKey(block.Code)
      )
      {
        continue;
      }
      ById[block.BlockId] = new HostRock(
        block,
        WildcardUtil.GetWildcardValue(inBlock.Code, block.Code)
      );
    }
  }

  /// <summary>
  /// The first block matching <paramref name="code"/> once <c>{name}</c> is filled from the host and
  /// <c>{grade}</c> from <paramref name="grade"/>. Logs a warning, once per filled code, and returns
  /// null when none matches.
  /// </summary>
  public Block? Find(HostRock host, string code, string? grade = null)
  {
    string path = code;
    if (_key != null && host.Value != null)
    {
      path = path.Replace("{" + _key + "}", host.Value);
    }
    if (grade != null)
    {
      path = path.Replace("{grade}", grade);
    }
    Block[] found = _api.World.SearchBlocks(new AssetLocation(path));
    if (found.Length == 0)
    {
      if (_missing.Add(path))
      {
        _api.Logger.Warning(
          "Deposit {0}: no block with code/wildcard '{1}' was found; {2} keeps its rock there.",
          _fromFile,
          path,
          host.Block.Code
        );
      }
      return null;
    }
    return found[0];
  }

  /// <summary>Records the pair in both blocks' attributes as the handbook reads them.</summary>
  public static void AddHandbook(Block host, Block placed)
  {
    host.Attributes ??= new JsonObject(JToken.Parse("{}"));
    placed.Attributes ??= new JsonObject(JToken.Parse("{}"));
    Record(host, "hostRockFor", placed.BlockId);
    Record(placed, "hostRock", host.BlockId);
  }

  private static void Record(Block block, string key, int id)
  {
    int[] ids = block.Attributes[key].AsArray(System.Array.Empty<int>());
    if (!ids.Contains(id))
    {
      block.Attributes.Token[key] = JToken.FromObject((int[])[.. ids, id]);
    }
  }
}
#endif
