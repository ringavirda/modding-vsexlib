using System.Reflection;
using NSubstitute.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

/// <summary>Answers <c>IBlockAccessor.GetBlockEntity&lt;T&gt;(pos)</c> with what
/// <paramref name="read"/> returns for the cell, when that is a <c>T</c>, and null otherwise. Every
/// other call falls through with <see cref="RouteAction.Continue"/>.</summary>
internal sealed class TypedBlockEntityCallHandler(
  System.Func<BlockPos, BlockEntity?> read
) : ICallHandler {
  public RouteAction Handle(ICall call) {
    MethodInfo method = call.GetMethodInfo();
    if (
      method.Name != nameof(IBlockAccessor.GetBlockEntity)
      || !method.IsGenericMethod
      || call.GetArguments() is not [BlockPos pos]
    )
      return RouteAction.Continue();
    BlockEntity? be = read(pos);
    return RouteAction.Return(
      method.GetGenericArguments()[0].IsInstanceOfType(be) ? be : null
    );
  }
}
