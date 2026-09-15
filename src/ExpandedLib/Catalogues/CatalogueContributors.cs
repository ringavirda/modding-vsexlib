using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace ExpandedLib.Catalogues;

/// <summary>Code contributions to one catalogue, re-invoked after every load.</summary>
public sealed class CatalogueContributors {
  private readonly List<Action<ICoreAPI>> _contributors = [];

  /// <summary>Registers a contributor, called with the api on every future
  /// <see cref="Invoke"/>.</summary>
  public void Register(Action<ICoreAPI> contributor) {
    if (contributor != null)
      _contributors.Add(contributor);
  }

  /// <summary>Drops every registered contributor. For test isolation only.</summary>
  public void Clear() => _contributors.Clear();

  /// <summary>How many contributors are registered.</summary>
  public int Count => _contributors.Count;

  /// <summary>Runs every registered contributor against <paramref name="api"/>, in registration
  /// order.</summary>
  public void Invoke(ICoreAPI api, ILogger logger) {
    foreach (Action<ICoreAPI> contributor in _contributors) {
      try {
        contributor(api);
      } catch (Exception e) {
        logger.Error(
          "[exlib] catalogue contributor {0} threw: {1}",
          contributor.Method.DeclaringType?.FullName ?? contributor.Method.Name,
          e.Message
        );
      }
    }
  }
}
