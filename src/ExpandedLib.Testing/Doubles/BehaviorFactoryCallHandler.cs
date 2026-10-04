using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NSubstitute;
using NSubstitute.Core;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing;

/// <summary>Forwards every <see cref="IClassRegistryAPI"/> call to a real registry, except that a
/// block-entity behaviour named in <c>factories</c> is built by its factory and its class reads as
/// the registry's, or <see cref="BlockEntityBehavior"/> when the registry holds none.</summary>
internal sealed class BehaviorFactoryCallHandler(
  IClassRegistryAPI registry,
  IReadOnlyDictionary<string, System.Func<BlockEntity, BlockEntityBehavior>> factories
) : ICallHandler {
  /// <summary>A substitute registry answering through this handler; <paramref name="factories"/> is
  /// read on each call, so a factory added later takes effect.</summary>
  public static IClassRegistryAPI Over(
    IClassRegistryAPI registry,
    IReadOnlyDictionary<string, System.Func<BlockEntity, BlockEntityBehavior>> factories
  ) {
    var substitute = Substitute.For<IClassRegistryAPI>();
    SubstitutionContext
      .Current.GetCallRouterFor(substitute)
      .RegisterCustomCallHandlerFactory(_ => new BehaviorFactoryCallHandler(
        registry,
        factories
      ));
    return substitute;
  }

  public RouteAction Handle(ICall call) {
    MethodInfo method = call.GetMethodInfo();
    object?[] args = call.GetArguments();
    switch (method.Name, args) {
      case (
        nameof(IClassRegistryAPI.CreateBlockEntityBehavior),
        [BlockEntity be, string name]
      ) when factories.TryGetValue(name, out var factory):
        return RouteAction.Return(factory(be));
      case (nameof(IClassRegistryAPI.GetBlockEntityBehaviorClass), [string name])
        when factories.ContainsKey(name):
        return RouteAction.Return(
          registry.GetBlockEntityBehaviorClass(name)
            ?? typeof(BlockEntityBehavior)
        );
    }
    try {
      return RouteAction.Return(method.Invoke(registry, args));
    } catch (TargetInvocationException e) when (e.InnerException != null) {
      ExceptionDispatchInfo.Capture(e.InnerException).Throw();
      throw;
    }
  }
}
