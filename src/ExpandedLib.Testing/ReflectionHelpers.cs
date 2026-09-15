using System;
using System.Reflection;

namespace ExpandedLib.Testing;

/// <summary>
/// Reflection shims for reading and priming production members that are not publicly accessible.
/// </summary>
public static class ReflectionHelpers {
  /// <summary>Sets a property's value through its (possibly non-public) setter.</summary>
  public static void SetProperty(
    object target,
    string propertyName,
    object? value
  ) {
    PropertyInfo prop =
      target
        .GetType()
        .GetProperty(
          propertyName,
          BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        )
      ?? throw new InvalidOperationException(
        $"Property '{propertyName}' not found on {target.GetType().Name}."
      );

    MethodInfo setter =
      prop.GetSetMethod(nonPublic: true)
      ?? throw new InvalidOperationException(
        $"Property '{propertyName}' on {target.GetType().Name} has no setter."
      );

    setter.Invoke(target, [value]);
  }

  /// <summary>Reads a (possibly non-public) instance property; the getter counterpart of
  /// <see cref="SetProperty"/>.</summary>
  public static object? GetProperty(object target, string propertyName) {
    PropertyInfo prop =
      target
        .GetType()
        .GetProperty(
          propertyName,
          BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        )
      ?? throw new InvalidOperationException(
        $"Property '{propertyName}' not found on {target.GetType().Name}."
      );

    MethodInfo getter =
      prop.GetGetMethod(nonPublic: true)
      ?? throw new InvalidOperationException(
        $"Property '{propertyName}' on {target.GetType().Name} has no getter."
      );

    return getter.Invoke(target, []);
  }

  /// <summary>Sets a (possibly non-public) instance field, walking up the type hierarchy.</summary>
  public static void SetField(object target, string fieldName, object? value) =>
    FindField(target.GetType(), fieldName).SetValue(target, value);

  /// <summary>Sets a (possibly non-public) static field on <paramref name="type"/>; the static
  /// counterpart of <see cref="SetField"/>.</summary>
  public static void SetStaticField(
    Type type,
    string fieldName,
    object? value
  ) =>
    (
      type.GetField(
        fieldName,
        BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static
      )
      ?? throw new InvalidOperationException(
        $"Static field '{fieldName}' not found on {type.Name}."
      )
    ).SetValue(null, value);

  /// <summary>Reads a (possibly non-public) instance field, walking up the type hierarchy.</summary>
  public static object? GetField(object target, string fieldName) =>
    FindField(target.GetType(), fieldName).GetValue(target);

  /// <summary>Reads a (possibly non-public) instance field if it exists, walking up the type
  /// hierarchy; returns false when none is found.</summary>
  public static bool TryGetField(
    object target,
    string fieldName,
    out object? value
  ) {
    for (Type? t = target.GetType(); t != null; t = t.BaseType) {
      FieldInfo? f = t.GetField(
        fieldName,
        BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
      );
      if (f != null) {
        value = f.GetValue(target);
        return true;
      }
    }
    value = null;
    return false;
  }

  /// <summary>Invokes a (possibly non-public) instance method, walking up the type hierarchy.</summary>
  public static object? Invoke(
    object target,
    string methodName,
    params object?[] args
  ) {
    for (Type? t = target.GetType(); t != null; t = t.BaseType) {
      MethodInfo? m = t.GetMethod(
        methodName,
        BindingFlags.Public
          | BindingFlags.NonPublic
          | BindingFlags.Instance
          | BindingFlags.DeclaredOnly
      );
      if (m != null)
        return m.Invoke(target, args);
    }
    throw new InvalidOperationException(
      $"Method '{methodName}' not found on {target.GetType().Name}."
    );
  }

  private static FieldInfo FindField(Type type, string fieldName) {
    for (Type? t = type; t != null; t = t.BaseType) {
      FieldInfo? f = t.GetField(
        fieldName,
        BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
      );
      if (f != null)
        return f;
    }
    throw new InvalidOperationException(
      $"Field '{fieldName}' not found on {type.Name}."
    );
  }
}
