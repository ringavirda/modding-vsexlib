using NSubstitute.Core;

namespace ExpandedLib.Testing;

/// <summary>Runs <paramref name="inner"/> until <see cref="Release"/>, then lets every call fall
/// through with <see cref="RouteAction.Continue"/>. NSubstitute offers no way to remove a custom
/// call handler from a substitute, and a proxy type's cache keeps the substitute's router alive;
/// releasing drops the handler's references.</summary>
internal sealed class ReleasableCallHandler(ICallHandler inner) : ICallHandler {
  private ICallHandler? _inner = inner;

  public RouteAction Handle(ICall call) =>
    _inner?.Handle(call) ?? RouteAction.Continue();

  /// <summary>Stops forwarding and drops the wrapped handler.</summary>
  public void Release() => _inner = null;
}
