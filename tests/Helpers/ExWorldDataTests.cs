using System;
using ExpandedLib.Helpers;
using NSubstitute;
using Vintagestory.API.Server;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// N1: the per-world side-band data rung over <see cref="ISaveGame.GetData{T}"/>/
/// <see cref="ISaveGame.StoreData{T}"/>, keyed <c>{domain}:{key}</c> so two mods' unqualified keys
/// never collide.
/// </summary>
public class ExWorldDataTests {
  private static ICoreServerAPI FakeApi(out ISaveGame saveGame) {
    saveGame = Substitute.For<ISaveGame>();
    var worldManager = Substitute.For<IWorldManagerAPI>();
    worldManager.SaveGame.Returns(saveGame);
    var api = Substitute.For<ICoreServerAPI>();
    api.WorldManager.Returns(worldManager);
    return api;
  }

  [Fact]
  public void Get_reads_the_domain_prefixed_key() {
    ICoreServerAPI api = FakeApi(out ISaveGame saveGame);
    saveGame.GetData("mymod:counter", 5).Returns(9);

    int value = ExWorldData.Get(api, "mymod", "counter", 5);

    Assert.Equal(9, value);
  }

  [Fact]
  public void Set_writes_the_domain_prefixed_key() {
    ICoreServerAPI api = FakeApi(out ISaveGame saveGame);

    ExWorldData.Set(api, "mymod", "counter", 9);

    saveGame.Received(1).StoreData("mymod:counter", 9);
  }

  [Fact]
  public void OnSave_subscribes_to_GameWorldSave() {
    var events = Substitute.For<IServerEventAPI>();
    var api = Substitute.For<ICoreServerAPI>();
    api.Event.Returns(events);
    bool fired = false;

    ExWorldData.OnSave(api, () => fired = true);
    events.GameWorldSave += Raise.Event<Action>();

    Assert.True(fired);
  }
}
