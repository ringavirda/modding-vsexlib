using System;
using System.Runtime.CompilerServices;
using ExpandedLib.Testing;
using NSubstitute;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The first substitute of a type does not keep its configured returns reachable for the
/// process through Castle's proxy-type cache.</summary>
public class ProxyDecoyTests {
  public interface IHeld {
    object Value { get; }
  }

  public class HeldClass {
    public virtual object Value => new();
  }

  // Fails under the mutation "Factory.Create calls inner without the decoy": the first
  // IHeld substitute's router stays in Castle's cache and holds the returned value.
  [Fact]
  public void First_interface_substitute_does_not_pin_its_returns() {
    SubstituteLogRouting.Install();

    WeakReference value = MakeInterface();

    ReplaceLastCall();
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    Assert.False(value.IsAlive);
  }

  // Fails under the mutation "Factory.Create calls inner without the decoy" and under "decoy
  // only for interfaces": the first HeldClass substitute keeps its router and the returned value.
  [Fact]
  public void First_class_substitute_does_not_pin_its_returns() {
    SubstituteLogRouting.Install();

    WeakReference value = MakeClass();

    ReplaceLastCall();
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    Assert.False(value.IsAlive);
  }

  // NSubstitute keeps each thread's last call, which reaches the substitute that took it.
  [MethodImpl(MethodImplOptions.NoInlining)]
  private static void ReplaceLastCall() =>
    Substitute.For<IDisposable>().Dispose();

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static WeakReference MakeInterface() {
    var held = Substitute.For<IHeld>();
    var value = new object();
    held.Value.Returns(value);
    _ = held.Value;
    return new WeakReference(value);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static WeakReference MakeClass() {
    var held = Substitute.For<HeldClass>();
    var value = new object();
    held.Value.Returns(value);
    _ = held.Value;
    return new WeakReference(value);
  }
}
