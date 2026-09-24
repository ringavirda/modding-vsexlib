using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NSubstitute.Core;
using NSubstitute.Core.DependencyInjection;
using NSubstitute.Routing.AutoValues;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing;

/// <summary>Makes every <see cref="ILogger"/> NSubstitute creates log into a
/// <see cref="RecordingLogger"/> of its own: a <c>Substitute.For</c> whose types include
/// <see cref="ILogger"/>, in any form, and the value NSubstitute makes up for an unset
/// <see cref="ILogger"/> property such as an API or world substitute's <c>Logger</c>. The
/// substitute still records its calls for <c>Received</c>.</summary>
/// <remarks>Replaces <see cref="SubstitutionContext.Current"/> once for the process, keeping the
/// running context's thread state, so a substitute made before the swap stays configurable. The
/// <see cref="RecordingLogger"/> is created at the substitute's first logging call.</remarks>
internal static class SubstituteLogRouting {
  private static readonly object Gate = new();
  private static bool _installed;

  private static readonly MethodInfo[] LoggerMethods =
  [
    .. typeof(ILogger)
      .GetInterfaces()
      .Prepend(typeof(ILogger))
      .SelectMany(t => t.GetMethods())
      .Where(m => m.ReturnType == typeof(void) && !m.IsSpecialName),
  ];

  /// <summary>Routes substitute loggers from now on; a second call does nothing.</summary>
  internal static void Install() {
    lock (Gate) {
      if (_installed)
        return;
      SubstitutionContext.Current = Routed(SubstitutionContext.Current);
      _installed = true;
    }
  }

  /// <summary>A context that routes substitute loggers and shares <paramref name="running"/>'s
  /// thread state.</summary>
  internal static ISubstitutionContext Routed(ISubstitutionContext running) =>
    NSubstituteDefaultFactory
      .DefaultContainer.Customize()
      .RegisterPerScope<IThreadLocalContext>(_ => running.ThreadContext)
      .Decorate<ISubstituteFactory>(
        (inner, r) => new Factory(inner, r.Resolve<ICallRouterResolver>())
      )
      .Decorate<IAutoValueProvidersFactory>(
        (inner, r) => new Providers(inner, r.Resolve<ICallRouterResolver>())
      )
      .Resolve<ISubstitutionContext>();

  // The substitute factory the auto-value providers make values through is the undecorated one,
  // so they get a routing factory of their own.
  private sealed class Providers(
    IAutoValueProvidersFactory inner,
    ICallRouterResolver routers
  ) : IAutoValueProvidersFactory {
    public IReadOnlyCollection<IAutoValueProvider> CreateProviders(
      ISubstituteFactory substituteFactory
    ) => inner.CreateProviders(new Factory(substituteFactory, routers));
  }

  private sealed class Factory(
    ISubstituteFactory inner,
    ICallRouterResolver routers
  ) : ISubstituteFactory {
    public object Create(Type[] typesToProxy, object?[] constructorArguments) =>
      Route(inner.Create(typesToProxy, constructorArguments!), typesToProxy);

    public object CreatePartial(
      Type[] typesToProxy,
      object?[] constructorArguments
    ) =>
      Route(
        inner.CreatePartial(typesToProxy, constructorArguments!),
        typesToProxy
      );

    private object Route(object substitute, Type[] types) {
      if (types.Any(t => typeof(ILogger).IsAssignableFrom(t))) {
        var forward = new Forward();
        routers
          .ResolveFor(substitute)
          .RegisterCustomCallHandlerFactory(_ => forward);
      }
      return substitute;
    }
  }

  // Replays each logging call on the substitute's RecordingLogger and lets NSubstitute go on.
  private sealed class Forward : ICallHandler {
    private RecordingLogger? _log;

    public RouteAction Handle(ICall call) {
      MethodInfo called = call.GetMethodInfo();
      Type[] parameters =
      [
        .. called.GetParameters().Select(p => p.ParameterType),
      ];
      MethodInfo? target = LoggerMethods.FirstOrDefault(m =>
        m.Name == called.Name
        && m.GetParameters()
          .Select(p => p.ParameterType)
          .SequenceEqual(parameters)
      );
      if (target != null) {
        _log ??= new RecordingLogger();
        try {
          target.Invoke(_log, call.GetOriginalArguments());
        } catch (TargetInvocationException e) when (e.InnerException != null) {
          ExceptionDispatchInfo.Capture(e.InnerException).Throw();
        }
      }
      return RouteAction.Continue();
    }
  }
}
