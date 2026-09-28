using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ExpandedLib.Registries;

/// <summary>
/// A <c>/exmod &lt;name&gt;</c> sub-command over one keyed registry: no argument lists every code, a
/// code alone shows that entry, and further words go to <see cref="Set"/>.
/// </summary>
public abstract class RegistrySubCommand<T> : IExSubCommand
  where T : class {
  private readonly System.Func<IEnumerable<string>> _codes;
  private readonly System.Func<string, T?> _resolve;
  private readonly string _descriptionKey;

  /// <param name="name">The sub-command word (e.g. <c>"config"</c>), attached under <c>exmod</c>.</param>
  /// <param name="descriptionKey">Lang key for <c>WithDescription</c>.</param>
  /// <param name="codes">Every registered code, for the no-argument list.</param>
  /// <param name="resolve">Looks up one entry by code; null for an unregistered code.</param>
  protected RegistrySubCommand(
    string name,
    string descriptionKey,
    System.Func<IEnumerable<string>> codes,
    System.Func<string, T?> resolve
  ) {
    Name = name;
    _descriptionKey = descriptionKey;
    _codes = codes;
    _resolve = resolve;
  }

  /// <summary>The sub-command word.</summary>
  public string Name { get; }

  public string ParentName => "exmod";

  /// <summary>The api <see cref="Register"/> was called with, for a <see cref="Set"/> override that
  /// needs more than the registry (e.g. broadcasting a change to connected clients).</summary>
  protected ICoreAPI Api { get; private set; } = null!;

  /// <summary>One line describing <paramref name="entry"/>, used for the no-argument list.</summary>
  protected abstract string Describe(T entry);

  /// <summary>Handles a resolved <paramref name="entry"/> against the words typed following its code: empty for "show this entry", one or more to set something.</summary>
  /// <remarks>The reply reaches the caller as written; word it with <c>Lang.GetL</c> in
  /// <see cref="CallerLanguage"/>.</remarks>
  /// <param name="entry">The entry the typed code resolved to.</param>
  /// <param name="args">The words after the code, split on spaces; empty for the bare code.</param>
  protected abstract TextCommandResult Set(T entry, string[] args);

  private string? _callerLanguage;

  /// <summary>The language of the caller whose command <see cref="Set"/> is answering: the player's
  /// language, or the server's own for the console, as
  /// <see cref="TextCommandCallingArgs.LanguageCode"/> gives it. Valid only while
  /// <see cref="Set"/> runs for a dispatched command.</summary>
  /// <exception cref="InvalidOperationException">Read outside a call of <see cref="Set"/> by
  /// this command's dispatch.</exception>
  protected string CallerLanguage =>
    _callerLanguage
    ?? throw new InvalidOperationException(
      $"{GetType().Name}.CallerLanguage is read outside Set; it names the caller only while Set "
        + "answers a command."
    );

  /// <summary>Lang key for "no code was given and nothing is registered".</summary>
  protected abstract string NoneRegisteredKey { get; }

  /// <summary>Lang key for the list's header line, printed above one <see cref="Describe"/> line per
  /// registered code.</summary>
  protected abstract string ListHeaderKey { get; }

  /// <summary>Lang key for "that code is not registered": <c>{0}</c> the code typed, <c>{1}</c> the
  /// known codes joined with <c>", "</c>.</summary>
  protected abstract string UnknownCodeKey { get; }

  public void Register(ICoreAPI api, Mod mod, IChatCommand parent) {
    Api = api;
    var parsers = api.ChatCommands.Parsers;

    parent
      .BeginSubCommand(Name)
      .WithDescription(Lang.Get(_descriptionKey))
      .WithArgs(parsers.OptionalWord("code"), parsers.OptionalAll("rest"))
      .HandleWith(OnCommand)
      .EndSubCommand();
  }

  private TextCommandResult OnCommand(TextCommandCallingArgs args) =>
    Dispatch(args[0] as string, args[1] as string, args.LanguageCode);

  /// <summary>The command's logic with fluent arg parsing stripped: <c>code</c> is the first word (null for the bare command), <c>rest</c> the remaining text; the reply is worded in <c>languageCode</c>.</summary>
  internal TextCommandResult Dispatch(
    string? code,
    string? rest,
    string languageCode
  ) {
    if (code is not string c)
      return TextCommandResult.Success(ListEntries(languageCode));

    T? entry = _resolve(c);
    if (entry == null)
      return TextCommandResult.Error(
        Lang.GetL(languageCode, UnknownCodeKey, c, KnownCodes())
      );

    string trimmed = rest?.Trim() ?? "";
    string[] tail =
      trimmed.Length == 0
        ? []
        : trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    _callerLanguage = languageCode;
    try {
      return Set(entry, tail);
    } finally {
      _callerLanguage = null;
    }
  }

  private string ListEntries(string languageCode) {
    string[] codes = _codes().OrderBy(c => c).ToArray();
    if (codes.Length == 0)
      return Lang.GetL(languageCode, NoneRegisteredKey);

    var lines = codes.Select(c =>
      _resolve(c) is { } entry ? $"  {Describe(entry)}" : c
    );
    return Lang.GetL(languageCode, ListHeaderKey)
      + "\n"
      + string.Join("\n", lines);
  }

  private string KnownCodes() => string.Join(", ", _codes().OrderBy(c => c));
}
