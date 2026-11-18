using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace ExpandedLib.Assembling;

/// <summary>Adds the cap-fitting gesture to the held help of every item a hand assembly spec names as its cap.</summary>
internal sealed class CollectibleBehaviorCapHelp(
  CollectibleObject collObj,
  ItemStack[] tools
) : CollectibleBehavior(collObj) {
  /// <summary>The held-help line naming the sneak hold, with the spec's tool shown for the off hand.</summary>
  public override WorldInteraction[] GetHeldInteractionHelp(
    ItemSlot inSlot,
    ref EnumHandling handling
  ) =>
    [
      new WorldInteraction
      {
        ActionLangCode = ExlibLang.BlockhelpHandassemblyCap,
        MouseButton = EnumMouseButton.Right,
        HotKeyCode = "shift",
        Itemstacks = tools,
      },
    ];

  /// <summary>Gives every item named as a cap by a loaded block's hand assembly spec a <see cref="CollectibleBehaviorCapHelp"/> listing the tools of the specs that name it. Run once the blocks and items are registered, from <c>AssetsFinalize</c>. The help is client-only and has no registered class name, so the server appends nothing: its item packets would name an unregistered behaviour class, and the client adds the help to its own items.</summary>
  /// <returns>How many items gained the behaviour; 0 on the server.</returns>
  internal static int Attach(ICoreAPI api) =>
    api.Side == EnumAppSide.Client ? AttachTo(api) : 0;

  /// <summary>Appends the behaviour to every cap item of <paramref name="api"/>'s world whatever the side.</summary>
  /// <returns>How many items gained the behaviour.</returns>
  internal static int AttachTo(ICoreAPI api) {
    List<HandAssemblySpec> specs =
    [
      .. api
        .World.Blocks.Where(b => b is { Code: not null })
        .Select(HandAssemblySpec.Of)
        .OfType<HandAssemblySpec>()
        .Where(s => s.Cap != null)
        .DistinctBy(s => (s.Cap, s.Tool)),
    ];
    int attached = 0;
    foreach (Item item in api.World.Items) {
      if (
        item is not { Code: not null }
        || item.GetCollectibleBehavior<CollectibleBehaviorCapHelp>(false)
          != null
      )
        continue;
      string code = item.Code.ToString();
      ItemStack[] tools =
      [
        .. specs
          .Where(s => WildcardUtil.Match(s.Cap!, code))
          .SelectMany(s => ToolStacks(api, s.Tool))
          .DistinctBy(s => s.Collectible.Id),
      ];
      if (!specs.Any(s => WildcardUtil.Match(s.Cap!, code)))
        continue;
      var help = new CollectibleBehaviorCapHelp(item, tools);
      help.Initialize(new JsonObject(new Newtonsoft.Json.Linq.JObject()));
      item.CollectibleBehaviors = [.. item.CollectibleBehaviors, help];
      attached++;
    }
    return attached;
  }

  private static IEnumerable<ItemStack> ToolStacks(
    ICoreAPI api,
    string pattern
  ) =>
    api
      .World.Items.Where(i =>
        i is { Code: not null }
        && WildcardUtil.Match(pattern, i.Code.ToString())
      )
      .Select(i => new ItemStack(i));
}
