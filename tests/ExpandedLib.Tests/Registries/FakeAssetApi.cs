using System.Collections.Generic;
using System.Linq;
using System.Text;
using ExpandedLib.Definitions;
using NSubstitute;
using Vintagestory.API.Common;

namespace ExpandedLib.Tests;

/// <summary>
/// An <see cref="ICoreAPI"/> substitute whose <c>Assets.GetMany</c> answers from an in-memory map of
/// path prefix to files, each a real engine <see cref="Asset"/> via <see cref="ExSyntheticAsset"/>.
/// </summary>
internal static class FakeAssetApi {
  /// <summary>Builds an api whose <c>Assets.GetMany(path)</c> returns <paramref name="files"/> for
  /// <paramref name="path"/> and an empty list for any other path.</summary>
  public static ICoreAPI Create(
    string path,
    params (string Location, string Json)[] files
  ) {
    var manager = Substitute.For<IAssetManager>();
    List<IAsset> assets =
    [
      .. files.Select(f =>
        ExSyntheticAsset.Create(
          new AssetLocation(f.Location),
          Encoding.UTF8.GetBytes(f.Json),
          new ExDefinitionOrigin()
        )
      ),
    ];
    manager
      .GetMany(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
      .Returns(call => (string)call[0] == path ? assets : new List<IAsset>());

    var api = Substitute.For<ICoreAPI>();
    api.Assets.Returns(manager);
    return api;
  }
}
