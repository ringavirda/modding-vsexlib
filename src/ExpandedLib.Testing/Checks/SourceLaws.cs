using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ExpandedLib.Testing;

/// <summary>
/// Source laws for recurring bugs: a renderer left on the old facing after an exchange, a rotor on
/// its own clock beside a mechanical network, a letter facing read by <c>BlockFacing.FromCode</c>,
/// and an unchecked read of a <c>SearchBlocks</c> result.
/// </summary>
/// <remarks>Every law reads source text with comments and string literals blanked. A type's parts
/// are merged by name across the files given, partial declarations included.</remarks>
public static class SourceLaws {
  private static readonly Regex TypeDeclaration = new(
    @"\b(?:class|record)\s+(?<name>\w+)(?:\s*<[^<>{;]*>)?(?:\s*\([^(){;]*\))?"
      + @"(?:\s*:\s*(?<base>[\w.]+))?",
    RegexOptions.Compiled
  );

  private static readonly Regex BlockEntityBase = new(
    @"^(?:\w*BlockEntity(?!Behavior)\w*|BE(?!Behavior)[A-Z]\w*)$",
    RegexOptions.Compiled
  );

  private static readonly Regex ExchangeSensitive = new(
    @"\.\s*(?<what>InitializeAnimator|RegisterRenderer)\s*\("
      + @"|\bnew\s+(?<what>ToggleAnimator|ConstructedAnimator)\s*\("
      + @"|\b(?<what>ExMeshCache)\s*\.\s*GetOrCreate\w*\s*\(",
    RegexOptions.Compiled
  );

  private static readonly Regex TesselationMethod = new(
    @"\bOnTesselation\s*\(",
    RegexOptions.Compiled
  );

  private static readonly Regex ExchangeOverride = new(
    @"\boverride\s+void\s+OnExchanged\s*\(",
    RegexOptions.Compiled
  );

  private static readonly Regex BaseExchange = new(
    @"\bbase\s*\.\s*OnExchanged\s*\(",
    RegexOptions.Compiled
  );

  private static readonly Regex MechanicalReference = new(
    @"\bBEBehaviorMP\w*|\bIMechanicalPowerDevice\b|\bMechanicalNetwork\b"
      + @"|\bExpandedLib\s*\.\s*Industry\s*\.\s*MechanicalPower\b"
      + @"|\bVintagestory\s*\.\s*GameContent\s*\.\s*Mechanics\b",
    RegexOptions.Compiled
  );

  private static readonly Regex AnimationSpeedWrite = new(
    @"\bAnimationSpeed\s*=(?![=>])\s*(?<value>[^,;}\n]+)",
    RegexOptions.Compiled
  );

  private static readonly Regex NumberLiteral = new(
    @"^-?\d+(?:\.\d+)?[fFdDmM]?$",
    RegexOptions.Compiled
  );

  private static readonly Regex NetworkAngle = new(
    @"\b(?:FrameFromAngle|LockFrameToAngle|AdvanceFrame|DrivenAngleRad)\b",
    RegexOptions.Compiled
  );

  private static readonly Regex FromCodeCall = new(
    @"\bBlockFacing\s*\.\s*FromCode\s*\(",
    RegexOptions.Compiled
  );

  private static readonly Regex LetterFallback = new(
    @"^\s*\?\?\s*BlockFacing\s*\.\s*FromFirstLetter\b",
    RegexOptions.Compiled
  );

  private static readonly Regex SearchCall = new(
    @"\bSearchBlocks\s*\(",
    RegexOptions.Compiled
  );

  private const string ElementRead =
    @"\s*[?!]?\s*(?:\[|\.\s*(?:First|Single|Last|ElementAt)\s*\()";

  /// <summary>Every block entity type in <paramref name="sourceFiles"/> that builds an animator
  /// (<c>InitializeAnimator</c>, a <c>ToggleAnimator</c> or <c>ConstructedAnimator</c>), registers
  /// a renderer, or takes an <c>ExMeshCache</c> mesh outside <c>OnTesselation</c>, and declares no
  /// <c>OnExchanged</c> override; and every <c>OnExchanged</c> override that never calls
  /// <c>base.OnExchanged</c>.</summary>
  /// <remarks>An exchange keeps the entity, so what it built in <c>Initialize</c> keeps the old
  /// block's facing. A type is a block entity when a base any part lists, followed through the
  /// files given, is named <c>*BlockEntity*</c> or <c>BE*</c>, behaviours excepted. An override on
  /// a base type does not count for a type that builds its own.</remarks>
  /// <param name="sourceFiles">C# files to read; each is read whole.</param>
  /// <returns>One line per type, <c>file:line: Type; reason</c>; <see cref="Key"/> keys it. Empty
  /// when clean.</returns>
  /// <exception cref="IOException">A file cannot be read, or does not exist.</exception>
  /// <exception cref="UnauthorizedAccessException">A file may not be read.</exception>
  public static IReadOnlyList<string> StaleOnExchange(
    IEnumerable<string> sourceFiles
  ) {
    Dictionary<string, List<TypePart>> types = Types(sourceFiles);
    var findings = new List<string>();
    foreach ((string name, List<TypePart> parts) in types) {
      if (!IsBlockEntity(name, types, []))
        continue;
      (TypePart Part, Match Hit)? built = parts
        .SelectMany(p => Hits(p, ExchangeSensitive).Select(m => (p, m)))
        .Where(h =>
          h.m.Groups["what"].Value != "ExMeshCache"
          || !InTesselation(h.p, h.m.Index)
        )
        .Select(h => ((TypePart, Match)?)h)
        .FirstOrDefault();
      (TypePart Part, Match Hit)? exchange = parts
        .SelectMany(p => Hits(p, ExchangeOverride).Select(m => (p, m)))
        .Select(h => ((TypePart, Match)?)h)
        .FirstOrDefault();
      if (exchange is { } o) {
        (int start, int end) = HarnessUse.BodySpan(
          o.Part.Code,
          o.Hit.Index + o.Hit.Length
        );
        if (!BaseExchange.IsMatch(o.Part.Code[start..end]))
          findings.Add(
            Finding(
              o.Part,
              o.Hit.Index,
              name,
              "OnExchanged never calls base.OnExchanged, so Block stays the old block"
            )
          );
      } else if (built is { } b)
        findings.Add(
          Finding(
            b.Part,
            b.Hit.Index,
            name,
            $"{b.Hit.Groups["what"].Value} with no OnExchanged override, so an exchange "
              + "keeps the old facing"
          )
        );
    }
    return findings;
  }

  /// <summary>Every type in <paramref name="sourceFiles"/> whose file names the mechanical-power
  /// world (a <c>BEBehaviorMP*</c> type, <c>IMechanicalPowerDevice</c>, <c>MechanicalNetwork</c>,
  /// or either mechanical-power namespace) and that sets an <c>AnimationSpeed</c> other than a
  /// number literal, but never names <c>MPAnim.FrameFromAngle</c>, <c>LockFrameToAngle</c>,
  /// <c>AdvanceFrame</c> or <c>BEBehaviorMPFillerPort.DrivenAngleRad</c>.</summary>
  /// <remarks>A part turned by its own clock drifts out of phase with the axle beside it; its frame
  /// comes from the network angle.</remarks>
  /// <param name="sourceFiles">C# files to read; each is read whole.</param>
  /// <returns>One line per type, <c>file:line: Type; reason</c>; <see cref="Key"/> keys it. Empty
  /// when clean.</returns>
  /// <exception cref="IOException">A file cannot be read, or does not exist.</exception>
  /// <exception cref="UnauthorizedAccessException">A file may not be read.</exception>
  public static IReadOnlyList<string> UndrivenRotor(
    IEnumerable<string> sourceFiles
  ) {
    var findings = new List<string>();
    foreach ((string name, List<TypePart> parts) in Types(sourceFiles)) {
      if (
        !parts.Any(p => MechanicalReference.IsMatch(p.Code))
        || parts.Any(p => Hits(p, NetworkAngle).Any())
      )
        continue;
      (TypePart Part, Match Hit)? speed = parts
        .SelectMany(p => Hits(p, AnimationSpeedWrite).Select(m => (p, m)))
        .Where(h => !NumberLiteral.IsMatch(h.m.Groups["value"].Value.Trim()))
        .Select(h => ((TypePart, Match)?)h)
        .FirstOrDefault();
      if (speed is { } s)
        findings.Add(
          Finding(
            s.Part,
            s.Hit.Index,
            name,
            $"animation speed {Squeeze(s.Hit.Groups["value"].Value)} runs on its own clock "
              + "beside a mechanical network; take the frame from MPAnim.FrameFromAngle"
          )
        );
    }
    return findings;
  }

  /// <summary>Every <c>BlockFacing.FromCode</c> call in <paramref name="sourceFiles"/> not followed
  /// by <c>?? BlockFacing.FromFirstLetter</c>.</summary>
  /// <remarks>The shipped <c>side</c> states are single letters, on which <c>FromCode</c> returns
  /// null. A call whose argument is always a full word is the guard's to allow, with what writes
  /// it.</remarks>
  /// <param name="sourceFiles">C# files to read; each is read whole.</param>
  /// <returns>One line per call, <c>file:line: call; reason</c>; <see cref="Key"/> keys it. Empty
  /// when clean.</returns>
  /// <exception cref="IOException">A file cannot be read, or does not exist.</exception>
  /// <exception cref="UnauthorizedAccessException">A file may not be read.</exception>
  public static IReadOnlyList<string> LetterFacing(
    IEnumerable<string> sourceFiles
  ) {
    var findings = new List<string>();
    foreach (string file in sourceFiles) {
      string text = File.ReadAllText(file);
      string code = HarnessUse.CodeOnly(text);
      foreach (Match call in FromCodeCall.Matches(code)) {
        int close = HarnessUse.Close(code, call.Index + call.Length, ')');
        if (LetterFallback.IsMatch(code[close..]))
          continue;
        findings.Add(
          Finding(
            file,
            code,
            call.Index,
            Squeeze(text[call.Index..close]),
            "returns null on a single-letter side state; fall back to BlockFacing.FromFirstLetter"
          )
        );
      }
    }
    return findings;
  }

  /// <summary>Every element read (<c>[i]</c>, <c>First()</c>, <c>Single()</c>, <c>Last()</c>,
  /// <c>ElementAt()</c>) in <paramref name="sourceFiles"/> taken straight off a
  /// <c>SearchBlocks(...)</c> call, or off the local it is assigned to before the enclosing block
  /// checks that local's <c>Length</c>, <c>Count</c> or <c>Any()</c>, or matches it with
  /// <c>is {</c> or <c>is [</c>.</summary>
  /// <remarks>A code that matches nothing returns an empty array, and the read throws.</remarks>
  /// <param name="sourceFiles">C# files to read; each is read whole.</param>
  /// <returns>One line per read, <c>file:line: read; reason</c>; <see cref="Key"/> keys it. Empty
  /// when clean.</returns>
  /// <exception cref="IOException">A file cannot be read, or does not exist.</exception>
  /// <exception cref="UnauthorizedAccessException">A file may not be read.</exception>
  public static IReadOnlyList<string> UnguardedSearch(
    IEnumerable<string> sourceFiles
  ) {
    const string Reason =
      "reads a SearchBlocks result with no length check; a code matching nothing throws";
    var findings = new List<string>();
    foreach (string file in sourceFiles) {
      string text = File.ReadAllText(file);
      string code = HarnessUse.CodeOnly(text);
      foreach (Match call in SearchCall.Matches(code)) {
        int close = HarnessUse.Close(code, call.Index + call.Length, ')');
        Match direct = Regex.Match(code[close..], "^" + ElementRead);
        if (direct.Success) {
          findings.Add(
            Finding(
              file,
              code,
              call.Index,
              Squeeze(text[call.Index..ReadEnd(code, close + direct.Length)]),
              Reason
            )
          );
          continue;
        }
        int statement = code.LastIndexOfAny([';', '{', '}'], call.Index) + 1;
        Match local = Regex.Match(
          code[statement..call.Index],
          @"(?<local>\w+)\s*=(?![=>])[^=;]*$"
        );
        if (!local.Success)
          continue;
        string name = Regex.Escape(local.Groups["local"].Value);
        int end = HarnessUse.Close(code, close, '}');
        string scope = code[close..end];
        Regex check = new(
          @"\b" + name + @"\s*(?:\??\.\s*(?:Length|Count|Any)\b|is\s*[{\[])"
        );
        foreach (Match read in Regex.Matches(scope, @"\b" + name + ElementRead)) {
          if (check.IsMatch(scope[..read.Index]))
            continue;
          findings.Add(
            Finding(
              file,
              code,
              close + read.Index,
              Squeeze(
                text[
                  (close + read.Index)..ReadEnd(
                    code,
                    close + read.Index + read.Length
                  )
                ]
              ),
              Reason
            )
          );
        }
      }
    }
    return findings;
  }

  /// <summary>The key of one finding: its file and subject, <c>{file}: {subject}</c>, without the
  /// line or the reason.</summary>
  [CheckHelper("keys a finding by file and subject for a guard's lists")]
  public static string Key(string finding) {
    int line = finding.IndexOf(':');
    int subject = finding.IndexOf(": ", line + 1, StringComparison.Ordinal) + 2;
    int reason = finding.IndexOf("; ", subject, StringComparison.Ordinal);
    return finding[..line] + ": " + finding[subject..reason];
  }

  private sealed record TypePart(string File, string Code, int Start, int End) {
    public string? Base { get; init; }
  }

  // Every class or record declared in the files, by name, with the span of each body; parts are
  // in path order, so a type's first hit is the same on every machine.
  private static Dictionary<string, List<TypePart>> Types(
    IEnumerable<string> sourceFiles
  ) {
    var types = new Dictionary<string, List<TypePart>>(StringComparer.Ordinal);
    foreach (string file in sourceFiles.OrderBy(f => f, StringComparer.Ordinal)) {
      string code = HarnessUse.CodeOnly(File.ReadAllText(file));
      foreach (Match type in TypeDeclaration.Matches(code)) {
        int open = code.IndexOfAny(['{', ';'], type.Index + type.Length);
        if (open < 0 || code[open] == ';')
          continue;
        string? baseName = type.Groups["base"].Success
          ? type.Groups["base"].Value.Split('.')[^1]
          : null;
        var part = new TypePart(
          file,
          code,
          open,
          HarnessUse.Close(code, open + 1, '}')
        ) {
          Base = baseName,
        };
        string name = type.Groups["name"].Value;
        if (!types.TryGetValue(name, out List<TypePart>? parts))
          types[name] = parts = [];
        parts.Add(part);
      }
    }
    return types;
  }

  private static bool IsBlockEntity(
    string name,
    Dictionary<string, List<TypePart>> types,
    HashSet<string> seen
  ) {
    if (!seen.Add(name))
      return false;
    return types[name]
      .Select(p => p.Base)
      .OfType<string>()
      .Any(b =>
        types.ContainsKey(b)
          ? IsBlockEntity(b, types, seen)
          : BlockEntityBase.IsMatch(b)
      );
  }

  private static IEnumerable<Match> Hits(TypePart part, Regex rule) =>
    rule.Matches(part.Code[..part.End], part.Start);

  private static bool InTesselation(TypePart part, int at) =>
    Hits(part, TesselationMethod)
      .Select(m => HarnessUse.BodySpan(part.Code, m.Index + m.Length))
      .Any(span => at >= span.Start && at < span.End);

  private static string Finding(
    TypePart part,
    int at,
    string subject,
    string reason
  ) => Finding(part.File, part.Code, at, subject, reason);

  private static string Finding(
    string file,
    string code,
    int at,
    string subject,
    string reason
  ) =>
    $"{Path.GetFileName(file)}:{HarnessUse.LineOf(code, at)}: {subject}; {reason}";

  // The index past the bracket or parenthesis an element read opened just before position from.
  private static int ReadEnd(string code, int from) =>
    HarnessUse.Close(code, from, code[from - 1] == '[' ? ']' : ')');

  private static string Squeeze(string text) =>
    Regex.Replace(
      Regex.Replace(text.Trim(), @"\s+", " "),
      @"(?<=[(\[]) | (?=[)\]])",
      ""
    );
}
