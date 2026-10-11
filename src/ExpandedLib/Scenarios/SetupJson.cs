using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Scenarios;

/// <summary>A setup file that cannot load: the message names the file, where in it, and the
/// key or value at fault.</summary>
internal sealed class SetupFileException(string message) : Exception(message);

/// <summary>Reads the JSON of a setup file as the game reads its assets: comments and
/// unquoted keys allowed, a key twice refused, strings never read as dates.</summary>
internal static class SetupJson {
  private static readonly Regex IdPattern = new(
    "^[a-z0-9]+(-[a-z0-9]+)*$",
    RegexOptions.CultureInvariant
  );

  /// <summary>The root object of <paramref name="text"/>.</summary>
  /// <exception cref="SetupFileException">The text is not JSON or its root is not an object.</exception>
  public static JObject ParseObject(string text, string file) {
    try {
      using var reader = new JsonTextReader(new StringReader(text)) {
        DateParseHandling = DateParseHandling.None,
      };
      JToken root = JToken.ReadFrom(
        reader,
        new JsonLoadSettings {
          CommentHandling = CommentHandling.Ignore,
          DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
        }
      );
      return root as JObject
        ?? throw new SetupFileException($"{file}: the root is not an object.");
    } catch (JsonReaderException e) {
      throw new SetupFileException($"{file}: {e.Message}");
    }
  }

  /// <summary>Whether <paramref name="id"/> is lower case words joined by <c>-</c>.</summary>
  public static bool IsId(string id) => IdPattern.IsMatch(id);

  /// <summary>Where a value sits, for messages: the file and a path such as
  /// <c>steps[2] (set)</c>.</summary>
  internal readonly record struct Where(string File, string Path) {
    public Where In(string key) =>
      new(File, Path.Length == 0 ? key : Path + "." + key);

    public Where At(int index) => new(File, $"{Path}[{index}]");

    public SetupFileException Fail(string message) =>
      new(
        Path.Length == 0 ? $"{File}: {message}" : $"{File}: {Path}: {message}"
      );

    public override string ToString() => Path;
  }

  /// <summary>Refuses every key of <paramref name="node"/> outside <paramref name="known"/>.</summary>
  public static void RefuseUnknown(
    JObject node,
    Where where,
    params string[] known
  ) {
    foreach (JProperty p in node.Properties())
      if (!known.Contains(p.Name))
        throw where.Fail($"unknown key \"{p.Name}\".");
  }

  public static JObject Object(JToken? token, Where where) =>
    token as JObject ?? throw where.Fail("expected an object.");

  public static JArray Array(JToken? token, Where where) =>
    token as JArray ?? throw where.Fail("expected an array.");

  public static string String(JToken? token, Where where) =>
    token is JValue { Type: JTokenType.String } v
      ? (string)v!
      : throw where.Fail("expected a string.");

  public static int Integer(JToken? token, Where where) =>
    token is JValue { Type: JTokenType.Integer } v
      ? (int)v
      : throw where.Fail("expected a whole number.");

  public static double Number(JToken? token, Where where) =>
    token is JValue { Type: JTokenType.Integer or JTokenType.Float } v
      ? (double)v
      : throw where.Fail("expected a number.");

  public static bool Bool(JToken? token, Where where) =>
    token is JValue { Type: JTokenType.Boolean } v
      ? (bool)v
      : throw where.Fail("expected true or false.");

  /// <summary>A literal's value: a string, a bool, or a number as a float.</summary>
  public static object Literal(JToken? token, Where where) =>
    token switch {
      JValue { Type: JTokenType.String } s => (string)s!,
      JValue { Type: JTokenType.Boolean } b => (bool)b,
      JValue { Type: JTokenType.Integer or JTokenType.Float } n => (float)n,
      _ => throw where.Fail("expected a string, a number or true or false."),
    };

  public static IReadOnlyList<string> Strings(JToken? token, Where where) =>
    [.. Array(token, where).Select((t, i) => String(t, where.At(i)))];
}
