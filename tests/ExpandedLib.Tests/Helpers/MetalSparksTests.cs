using System.Linq;
using ExpandedLib.Industry.Helpers;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="ExParticles.MetalSparks"/>: glow and speed by metal temperature, through
/// the <see cref="ExParticles.Spawn(IWorldAccessor, int, Vec3d, Vec3d, Vec3f, Vec3f, float, float, float, float, float, float, EnumParticleModel, EvolvingNatFloat?, EvolvingNatFloat?, bool, byte)"/> flags overload.</summary>
public class MetalSparksTests {
  private static SimpleParticleProperties Sparks(float temperatureC) {
    IWorldAccessor world = Substitute.For<IWorldAccessor>();
    ExParticles.MetalSparks(world, new Vec3d(1, 2, 3), temperatureC);
    return world
      .ReceivedCalls()
      .Where(c =>
        c.GetMethodInfo().Name == nameof(IWorldAccessor.SpawnParticles)
      )
      .Select(c => c.GetArguments()[0])
      .OfType<SimpleParticleProperties>()
      .Single();
  }

  // Fails when Spawn drops the vertex flags it is given.
  [Theory]
  [InlineData(1000f, 128)]
  [InlineData(800f, 48)]
  public void MetalSparks_GlowByTemperature(float temperatureC, int flags) =>
    Assert.Equal(flags, Sparks(temperatureC).VertexFlags);

  // Fails when the speed is fixed at 1 instead of scaling with temperature.
  [Fact]
  public void MetalSparks_VelocityByTemperature() {
    Assert.Equal(0.3f, Sparks(300f).MinVelocity.Y, 3);
    Assert.Equal(7f, Sparks(1000f).AddVelocity.Y, 3);
  }

  // Fails when the 0.8.4 overload stops forwarding with unlit flags.
  [Fact]
  public void Spawn_LegacyOverloadIsUnlit() {
    IWorldAccessor world = Substitute.For<IWorldAccessor>();
    ExParticles.Spawn(
      world,
      ExParticles.Smoke,
      new Vec3d(),
      new Vec3d(),
      new Vec3f(),
      new Vec3f(),
      1,
      1,
      1f,
      0f,
      0.1f,
      0.1f
    );
    var props = world
      .ReceivedCalls()
      .Select(c => c.GetArguments()[0])
      .OfType<SimpleParticleProperties>()
      .Single();
    Assert.Equal(0, props.VertexFlags);
  }
}
