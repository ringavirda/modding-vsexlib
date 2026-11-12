using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NSubstitute.Core;

namespace ExpandedLib.Testing;

/// <summary>The received-call record of a substitute that keeps at least its newest
/// <see cref="Kept"/> calls and at most twice that, dropping the oldest as calls arrive. A long
/// run then holds a bounded record where NSubstitute's own keeps every call until cleared.</summary>
/// <remarks>Thread-safe. <c>Received</c>, <c>DidNotReceive</c> and <c>ReceivedCalls</c> see only the
/// kept calls.</remarks>
internal sealed class BoundedCallCollection : ICallCollection {
  /// <summary>The fewest newest calls a substitute keeps.</summary>
  internal const int Kept = 250_000;

  private readonly List<ICall> _calls = [];

  public void Add(ICall call) {
    lock (_calls) {
      if (_calls.Count >= 2 * Kept)
        _calls.RemoveRange(0, _calls.Count - Kept + 1);
      _calls.Add(call);
    }
  }

  /// <summary>Removes <paramref name="call"/>; nothing when it was dropped already.</summary>
  public void Delete(ICall call) {
    lock (_calls) {
      int at = _calls.LastIndexOf(call);
      if (at >= 0)
        _calls.RemoveAt(at);
    }
  }

  public IEnumerable<ICall> AllCalls() {
    lock (_calls)
      return _calls.ToArray();
  }

  public void Clear() {
    lock (_calls)
      _calls.Clear();
  }

  /// <summary>Gives <paramref name="substitute"/> a new <see cref="BoundedCallCollection"/> as its
  /// received-call record, for recording, asserting, clearing and configuring a return on the last
  /// call alike. Calls recorded before are dropped.</summary>
  /// <param name="substitute">An NSubstitute substitute.</param>
  /// <exception cref="InvalidOperationException">NSubstitute's internals no longer hold the record
  /// where this looks for it.</exception>
  internal static void Install(object substitute) {
    var bounded = new BoundedCallCollection();
    ICallRouter router = SubstitutionContext.Current.GetCallRouterFor(
      substitute
    );
    var state = (ISubstituteState)
      FieldOf<ISubstituteState>(router).GetValue(router)!;
    FieldOf<ICallCollection>(state).SetValue(state, bounded);
    object getSpec = FieldOf<IGetCallSpec>(state.ConfigureCall)
      .GetValue(state.ConfigureCall)!;
    FieldOf<ICallCollection>(getSpec).SetValue(getSpec, bounded);
  }

  /// <summary>The one instance field of type <typeparamref name="T"/> on <paramref name="holder"/>,
  /// found by type because NSubstitute names it differently between versions.</summary>
  /// <exception cref="InvalidOperationException"><paramref name="holder"/> has no such field.</exception>
  internal static FieldInfo FieldOf<T>(object holder) =>
    holder
      .GetType()
      .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
      .SingleOrDefault(f => f.FieldType == typeof(T))
    ?? throw new InvalidOperationException(
      $"{holder.GetType().FullName} holds no {typeof(T).Name} field."
    );
}
