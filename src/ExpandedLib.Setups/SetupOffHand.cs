#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace ExpandedLib.Scenarios;

/// <summary>The stacks the game's off-hand slot takes, and the list of exact codes a mod's mechanic
/// still puts there though it would refuse them.</summary>
internal static class SetupOffHand
{
  /// <summary>The file, in a mod's <c>tests/live</c> folder, that lists the allowed codes.</summary>
  public const string FileName = "offhand-allow.txt";

  /// <summary>Whether the game's off-hand slot takes <paramref name="stack"/>: its storage flags
  /// include <see cref="EnumItemStorageFlags.Offhand"/>.</summary>
  public static bool Takes(ItemStack stack) =>
    (stack.Collectible.GetStorageFlags(stack) & EnumItemStorageFlags.Offhand)
    != 0;

  /// <summary>Reads an allow list: one <c>&lt;exact code&gt; &lt;mechanic&gt;</c> entry per line,
  /// split at the first whitespace; blank lines and lines starting <c>#</c> are skipped.</summary>
  /// <param name="text">The file's text.</param>
  /// <param name="file">The file's path, named in a refusal.</param>
  /// <returns>Each code with its mechanic, compared ordinally.</returns>
  /// <exception cref="SetupFileException">A code contains <c>*</c>, has no <c>:</c> or is listed twice,
  /// or an entry has no mechanic.</exception>
  public static IReadOnlyDictionary<string, string> Parse(
    string text,
    string file
  )
  {
    var allowed = new Dictionary<string, string>(StringComparer.Ordinal);
    string[] lines = text.Split('\n');
    for (int i = 0; i < lines.Length; i++)
    {
      string line = lines[i].Trim();
      if (line.Length == 0 || line[0] == '#')
        continue;
      string where = $"{file}, line {i + 1}";
      int cut = line.IndexOfAny([' ', '\t']);
      string code = cut < 0 ? line : line[..cut];
      string mechanic = cut < 0 ? "" : line[(cut + 1)..].Trim();
      if (code.Contains('*'))
        throw new SetupFileException(
          $"{where}: {code} is a pattern; an entry is one exact code."
        );
      if (!code.Contains(':'))
        throw new SetupFileException(
          $"{where}: {code} has no domain; an entry is a full code such as game:coke."
        );
      if (mechanic.Length == 0)
        throw new SetupFileException(
          $"{where}: {code} names no mechanic that puts it in the off hand."
        );
      if (!allowed.TryAdd(code, mechanic))
        throw new SetupFileException($"{where}: {code} is listed twice.");
    }
    return allowed;
  }

  /// <summary>Why <paramref name="stack"/> may not go in the off-hand slot.</summary>
  /// <param name="stack">What would be put there; null is an empty slot.</param>
  /// <param name="allowed">The codes a mechanic still puts there, as <see cref="Parse"/> reads them.</param>
  /// <returns>Null for a null stack, a stack the slot takes or a listed code; else the refusal.</returns>
  public static string? Refusal(
    ItemStack? stack,
    IReadOnlyDictionary<string, string> allowed
  )
  {
    if (stack == null || Takes(stack))
      return null;
    string code = stack.Collectible.Code.ToString();
    if (allowed.ContainsKey(code))
      return null;
    int flags = (int)stack.Collectible.GetStorageFlags(stack);
    return $"{code} cannot go in the off-hand slot: the game's off-hand slot takes only a stack whose storage flags include Offhand (256), and {code} has {flags}. A mechanic that still puts it there is listed by its exact code in tests/live/offhand-allow.txt.";
  }
}
#endif
