using System;
using System.IO;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace WasherPunch.Tests;

/// <summary>The sample loaded as a game once for the collection; the load empties and refills the
/// process-wide job registry, so the classes that read it share this one.</summary>
public sealed class LoadedWorld : IDisposable {
  public LoadedWorld() {
    World = new TestWorld();
    World.LoadAsGame([Path.Combine(RepoPaths.Root, "samples", "WasherPunch")]);
    World.World.Side.Returns(EnumAppSide.Server);
  }

  public TestWorld World { get; }

  public void Dispose() => World.Dispose();
}

[CollectionDefinition(Name)]
public sealed class WorldCollection : ICollectionFixture<LoadedWorld> {
  public const string Name = "washerpunch-world";
}
