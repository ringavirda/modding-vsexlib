using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ExpandedLib.Blocks;
using ExpandedLib.Config;
using ExpandedLib.Industry.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace ExpandedLib.Testing;

/// <summary>
/// Source laws for recurring bugs: a renderer kept past an exchange, a rotor on its own clock, a
/// letter facing read by <c>FromCode</c>, an unchecked <c>SearchBlocks</c> read, a live tunable
/// copied at load or read only by the text, an unhandled container dialog, inline particles.
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

  private static readonly Regex MeshField = new(
    @"\bMeshData\s*\??\s+(?<name>[A-Za-z_]\w*)\s*(?:[;{]|=(?![=>]))",
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

  private static readonly Regex TypeKeyword = new(
    @"\b(?:class|struct|record|interface|enum)\b",
    RegexOptions.Compiled
  );

  private static readonly Regex MethodName = new(
    @"(?<name>\w+)\s*(?:<[^<>()]*>)?\s*\($",
    RegexOptions.Compiled
  );

  private static readonly Regex LoadMethod = new(
    @"^(?:Initialize|OnLoaded)$",
    RegexOptions.Compiled
  );

  private static readonly Regex StartMethod = new(
    @"^(?:Start|StartPre|StartServerSide|StartClientSide|AssetsLoaded|AssetsFinalize)$",
    RegexOptions.Compiled
  );

  private static readonly Regex BareCall = new(
    @"(?:(?<![.\w])|\bthis\s*\.\s*)(?<name>[A-Za-z_]\w*)\s*\(",
    RegexOptions.Compiled
  );

  private static readonly Regex Assignment = new(
    @"(?<![\w.])(?:this\s*\.\s*)?(?<target>[A-Za-z_][\w.]*)\s*(?:[-+*/%]|\?\?)?=(?![=>])",
    RegexOptions.Compiled
  );

  private static readonly Regex LangCall = new(
    @"\bLang\s*\.\s*Get\w*\s*\(",
    RegexOptions.Compiled
  );

  private static readonly Regex NameofCall = new(
    @"\bnameof\s*\(",
    RegexOptions.Compiled
  );

  private static readonly Regex AliasHead = new(
    @"(?<![\w.])(?<alias>[A-Za-z_]\w*)\s*=>\s*$",
    RegexOptions.Compiled
  );

  private static readonly Regex OverrideKeyword = new(
    @"\boverride\b",
    RegexOptions.Compiled
  );

  private static readonly Regex AliasTail = new(
    @"\G\s*;",
    RegexOptions.Compiled
  );

  private static readonly Regex Construction = new(
    @"\bnew\s+(?:[\w.]+\.)?(?<type>\w+)\s*(?:<[^<>(){};]*>)?\s*\(",
    RegexOptions.Compiled
  );

  private static readonly Regex PacketOverride = new(
    @"\boverride\b[\w\s]*\bvoid\s+OnReceivedClientPacket\s*\(",
    RegexOptions.Compiled
  );

  private static readonly Regex ParticleConstruction = new(
    @"\bnew\s+(?:[\w.]+\.)?SimpleParticleProperties\s*[({]",
    RegexOptions.Compiled
  );

  private const string ParticleHome =
    "ExpandedLib.Industry/Helpers/ExParticles.cs";

  // Where a base type outside the files given is looked up: the block entity, dialog and mod
  // system types of the game's and exlib's assemblies, by simple name.
  private static readonly Lazy<Dictionary<string, Type?>> LoadedTypes = new(
    () =>
      new[]
      {
        typeof(BlockEntity).Assembly,
        typeof(BlockEntityContainer).Assembly,
        typeof(BEBehaviorAnimatable).Assembly,
        typeof(ExBlockEntityContainer).Assembly,
        typeof(ExParticles).Assembly,
      }
        .Distinct()
        .SelectMany(LoadableTypes)
        .Where(t =>
          typeof(BlockEntity).IsAssignableFrom(t)
          || typeof(GuiDialog).IsAssignableFrom(t)
          || typeof(ModSystem).IsAssignableFrom(t)
        )
        .GroupBy(t => t.Name, StringComparer.Ordinal)
        .ToDictionary(
          g => g.Key,
          g => g.Count() == 1 ? g.First() : null,
          StringComparer.Ordinal
        )
  );

  /// <summary>Every block entity type in <paramref name="sourceFiles"/> that builds for its facing
  /// (<c>InitializeAnimator</c>, a <c>ToggleAnimator</c> or <c>ConstructedAnimator</c>, a renderer,
  /// an <c>ExMeshCache</c> mesh outside <c>OnTesselation</c>, a write to a <c>MeshData</c> field it
  /// declares) with no <c>OnExchanged</c> override; every override that never calls the base; and
  /// every such field an override neither assigns nor clears.</summary>
  /// <remarks>An exchange keeps the entity, so what it built keeps the old block's facing. A type
  /// is a block entity when a base any part lists, followed through the files, is named
  /// <c>*BlockEntity*</c> or <c>BE*</c>, behaviours excepted; an override on a base does not count
  /// for a type that builds its own. A field is written by <c>=</c>, <c>??=</c> or <c>out</c>, its
  /// initialiser aside; in the override <c>??=</c> is no clear.</remarks>
  /// <param name="sourceFiles">C# files to read; each is read whole.</param>
  /// <returns>One line per type, <c>file:line: Type; reason</c>, or per field an override keeps,
  /// <c>file:line: Type.field; reason</c>; <see cref="Key"/> keys it. Empty when clean.</returns>
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
      List<(string Field, TypePart Part, Match Hit)> writes = MeshWrites(parts);
      (TypePart Part, Match Hit, string What)? built = parts
        .SelectMany(p =>
          Hits(p, ExchangeSensitive)
            .Where(m =>
              m.Groups["what"].Value != "ExMeshCache"
              || !InTesselation(p, m.Index)
            )
            .Select(m => (p, m, m.Groups["what"].Value))
        )
        .Concat(
          writes.Select(w => (w.Part, w.Hit, $"MeshData field {w.Field}"))
        )
        .OrderBy(h => parts.IndexOf(h.Item1))
        .ThenBy(h => h.Item2.Index)
        .Select(h => ((TypePart, Match, string)?)h)
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
        foreach (string field in writes.Select(w => w.Field).Distinct())
          if (
            !writes.Any(w =>
              w.Field == field
              && w.Part == o.Part
              && w.Hit.Index >= start
              && w.Hit.Index < end
              && !w.Hit.Groups["coalesce"].Success
            )
          )
            findings.Add(
              Finding(
                o.Part,
                o.Hit.Index,
                $"{name}.{field}",
                $"OnExchanged never assigns or clears the MeshData field {field}, so the mesh "
                  + "it caches keeps the old facing"
              )
            );
      } else if (built is { } b)
        findings.Add(
          Finding(
            b.Part,
            b.Hit.Index,
            name,
            $"{b.What} with no OnExchanged override, so an exchange keeps the old facing"
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

  /// <summary>Every member of a type in <paramref name="sourceFiles"/> that holds a copy of a value
  /// of a manageable config store: a field or property initialiser, or an assignment in a
  /// constructor, <c>Initialize</c>, <c>OnLoaded</c>, a <c>ModSystem</c>'s <c>Start*</c>,
  /// <c>AssetsLoaded</c> or <c>AssetsFinalize</c>, or a method of the type one of those calls by
  /// name.</summary>
  /// <remarks><c>/exmod config</c> edits the store live, so a copy taken at load keeps the old
  /// value. A read in a lambda, into a local or behind <c>=&gt;</c> is live. A value is a public
  /// property of a config type, read as <c>{Accessor}.{Value}</c>.</remarks>
  /// <param name="sourceFiles">C# files to read; each is read whole.</param>
  /// <param name="configAssemblies">Assemblies whose config types registered with
  /// <see cref="ExConfigRegisterAttribute.Manageable"/> give the values.</param>
  /// <returns>One line per copy, <c>file:line: Type.member; reason</c>; <see cref="Key"/> keys it.
  /// Empty when clean.</returns>
  /// <exception cref="ArgumentException">The assemblies hold no manageable config type.</exception>
  /// <exception cref="IOException">A file cannot be read, or does not exist.</exception>
  /// <exception cref="UnauthorizedAccessException">A file may not be read.</exception>
  public static IReadOnlyList<string> CachedTunables(
    IEnumerable<string> sourceFiles,
    IEnumerable<Assembly> configAssemblies
  ) {
    Regex read = TunableRead(TunableStores(configAssemblies));
    Dictionary<string, List<TypePart>> types = Types(sourceFiles);
    var findings = new List<string>();
    foreach ((string name, List<TypePart> parts) in types) {
      bool modSystem = Derives(name, typeof(ModSystem), types, []);
      var bodies = parts.ToDictionary(p => p, Members);
      foreach (TypePart part in parts)
        foreach (Match hit in Hits(part, read)) {
          string? member = InitialisedMember(part, bodies[part], hit.Index);
          if (member != null)
            findings.Add(
              Finding(
                part,
                hit.Index,
                $"{name}.{member}",
                Copies(hit, "at construction")
              )
            );
        }
      List<(TypePart Part, MemberBlock Block, string Name)> methods =
      [
        .. parts.SelectMany(p =>
          bodies[p]
            .Blocks.Select(b => (p, b, MethodOf(b)))
            .Where(m => m.Item3 != null)
            .Select(m => (m.p, m.b, m.Item3!))
        ),
      ];
      List<(TypePart Part, MemberBlock Block, string Name)> entries =
      [
        .. methods.Where(m =>
          m.Name == name
          || LoadMethod.IsMatch(m.Name)
          || (modSystem && StartMethod.IsMatch(m.Name))
        ),
      ];
      var scanned = new HashSet<MemberBlock>(entries.Select(e => e.Block));
      foreach ((TypePart part, MemberBlock entry, string entryName) in entries) {
        bool constructor = entryName == name;
        findings.AddRange(
          BodyCopies(
            part,
            entry,
            name,
            read,
            constructor ? "at construction" : $"in {entryName}"
          )
        );
        string caller = constructor ? "the constructor" : entryName;
        List<(int Start, int End)> lambdas = Lambdas(part.Code, entry);
        foreach (
          Match call in BareCall
            .Matches(part.Code[..entry.End], entry.Open)
            .Where(c => !lambdas.Any(l => c.Index > l.Start && c.Index < l.End))
        )
          foreach (
            (
              TypePart helperPart,
              MemberBlock helper,
              string helperName
            ) in methods.Where(m =>
              m.Name == call.Groups["name"].Value && scanned.Add(m.Block)
            )
          )
            findings.AddRange(
              BodyCopies(
                helperPart,
                helper,
                name,
                read,
                $"in {helperName}, called from {caller}"
              )
            );
      }
    }
    return findings;
  }

  /// <summary>Every value of a manageable config store that <paramref name="sourceFiles"/> read,
  /// every read of which sits inside the argument list of a <c>Lang.Get</c> call
  /// (<c>Get</c>, <c>GetIfExists</c>, <c>GetMatching</c>, ...).</summary>
  /// <remarks>A value only the text reads is a mechanic the simulation lacks. A read is
  /// <c>{Accessor}.{Value}</c>, or an unassigned <c>.{Value}</c> on a config instance; a property
  /// whose whole expression body is the read stands for the value, each use of its name a read, and
  /// read once more by its base when it overrides; <c>nameof</c> is no read.</remarks>
  /// <param name="sourceFiles">C# files to read; each is read whole.</param>
  /// <param name="configAssemblies">Assemblies whose manageable config types give the values;
  /// a store without <c>Manageable</c> is skipped.</param>
  /// <returns>One line per value at its first read in path order,
  /// <c>file:line: Accessor.Value; reason</c>; <see cref="Key"/> keys it. Empty when
  /// clean.</returns>
  /// <exception cref="ArgumentException">The assemblies hold no manageable config type.</exception>
  /// <exception cref="IOException">A file cannot be read, or does not exist.</exception>
  /// <exception cref="UnauthorizedAccessException">A file may not be read.</exception>
  public static IReadOnlyList<string> DisplayOnlyTunables(
    IEnumerable<string> sourceFiles,
    IEnumerable<Assembly> configAssemblies
  ) {
    (
      SortedDictionary<string, List<(string File, int At, bool Shown)>> reads,
      Dictionary<string, string> code
    ) = TunableReads(sourceFiles, TunableStores(configAssemblies));
    var findings = new List<string>();
    foreach (
      (string value, List<(string File, int At, bool Shown)> all) in reads
    ) {
      if (!all.All(r => r.Shown))
        continue;
      (string file, int at, _) = all.OrderBy(
          r => r.File,
          StringComparer.Ordinal
        )
        .ThenBy(r => r.At)
        .First();
      findings.Add(
        Finding(
          file,
          code[file],
          at,
          value,
          "every read sits inside a Lang.Get argument list, so the value changes the text and "
            + "nothing else"
        )
      );
    }
    return findings;
  }

  /// <summary>Every value of a manageable config store that no file in
  /// <paramref name="sourceFiles"/> reads.</summary>
  /// <remarks>A setting no source reads changes nothing. A read is one
  /// <see cref="DisplayOnlyTunables"/> counts, a <c>Lang.Get</c> argument included; a test is no
  /// reader, so tests are not among the files.</remarks>
  /// <param name="sourceFiles">C# files to read; each is read whole. They declare every manageable
  /// config type of <paramref name="configAssemblies"/>.</param>
  /// <param name="configAssemblies">Assemblies whose manageable config types give the values;
  /// a store without <c>Manageable</c> is skipped.</param>
  /// <returns>One line per value at its property's declaration, else its config type's, in path
  /// and line order, <c>file:line: Accessor.Value; reason</c>; <see cref="Key"/> keys it. Empty
  /// when clean.</returns>
  /// <exception cref="ArgumentException">The assemblies hold no manageable config type, or the
  /// files do not declare one.</exception>
  /// <exception cref="IOException">A file cannot be read, or does not exist.</exception>
  /// <exception cref="UnauthorizedAccessException">A file may not be read.</exception>
  public static IReadOnlyList<string> UnreadTunables(
    IEnumerable<string> sourceFiles,
    IEnumerable<Assembly> configAssemblies
  ) {
    string[] files = [.. sourceFiles];
    List<TunableStore> stores = TunableStores(configAssemblies);
    SortedDictionary<string, List<(string File, int At, bool Shown)>> reads =
      TunableReads(files, stores).Reads;
    Dictionary<string, List<TypePart>> types = Types(files);
    var unread = new List<(TypePart Part, int At, string Value)>();
    foreach (TunableStore store in stores) {
      if (!types.TryGetValue(store.Config.Name, out List<TypePart>? parts))
        throw new ArgumentException(
          $"the files do not declare the config type {store.Config.Name}",
          nameof(sourceFiles)
        );
      foreach (string value in store.Values) {
        if (reads.ContainsKey($"{store.Accessor}.{value}"))
          continue;
        Regex declaration = new(@"(?<![\w.])" + value + @"\s*\{");
        (TypePart Part, Match Hit)? declared = parts
          .SelectMany(p => Hits(p, declaration).Select(m => (p, m)))
          .Select(h => ((TypePart, Match)?)h)
          .FirstOrDefault();
        unread.Add(
          declared is { } d
            ? (d.Part, d.Hit.Index, $"{store.Accessor}.{value}")
            : (parts[0], parts[0].Start, $"{store.Accessor}.{value}")
        );
      }
    }
    return
    [
      .. unread
        .OrderBy(u => u.Part.File, StringComparer.Ordinal)
        .ThenBy(u => u.At)
        .Select(u =>
          Finding(
            u.Part,
            u.At,
            u.Value,
            "no source reads it, so the setting changes nothing"
          )
        ),
    ];
  }

  /// <summary>Every type in <paramref name="sourceFiles"/> over <c>BlockEntityContainer</c> that
  /// constructs a <c>GuiDialogBlockEntity</c> subclass, when neither it nor a base type overrides
  /// <c>OnReceivedClientPacket</c>; and every such type whose base cannot be read.</summary>
  /// <remarks><c>BlockEntityContainer</c> handles no client packet, so the dialog's slot clicks
  /// never reach the server inventory and the two sides diverge. Bases are followed through the
  /// files given, then looked up by name in the game's and exlib's assemblies; a base found in
  /// neither is named.</remarks>
  /// <param name="sourceFiles">C# files to read; each is read whole.</param>
  /// <returns>One line per type, <c>file:line: Type; reason</c>; <see cref="Key"/> keys it. Empty
  /// when clean.</returns>
  /// <exception cref="IOException">A file cannot be read, or does not exist.</exception>
  /// <exception cref="UnauthorizedAccessException">A file may not be read.</exception>
  public static IReadOnlyList<string> ContainerDialogPackets(
    IEnumerable<string> sourceFiles
  ) {
    Dictionary<string, List<TypePart>> types = Types(sourceFiles);
    var findings = new List<string>();
    foreach ((string name, List<TypePart> parts) in types) {
      (TypePart Part, Match Hit)? opened = parts
        .SelectMany(p => Hits(p, Construction).Select(m => (p, m)))
        .Where(h =>
          Derives(
            h.m.Groups["type"].Value,
            typeof(GuiDialogBlockEntity),
            types,
            []
          )
        )
        .Select(h => ((TypePart, Match)?)h)
        .FirstOrDefault();
      if (opened is not { } o)
        continue;
      string dialog = o.Hit.Groups["type"].Value;
      bool handled = false;
      string? current = name;
      var seen = new HashSet<string>(StringComparer.Ordinal);
      while (
        current != null
        && types.TryGetValue(current, out List<TypePart>? line)
        && seen.Add(current)
      ) {
        handled |= line.Any(p => Hits(p, PacketOverride).Any());
        current = BaseOf(line, types);
      }
      if (current == null || types.ContainsKey(current))
        continue;
      Type? outside = Loaded(current);
      if (outside == null)
        findings.Add(
          Finding(
            o.Part,
            o.Hit.Index,
            name,
            $"opens {dialog}, but its base {current} is in neither the files nor the game or "
              + "exlib, so its packet handling cannot be read"
          )
        );
      else if (
        typeof(BlockEntityContainer).IsAssignableFrom(outside)
        && !handled
        && outside
          .GetMethod(
            nameof(BlockEntity.OnReceivedClientPacket),
            [typeof(IPlayer), typeof(int), typeof(byte[])]
          )
          ?.DeclaringType == typeof(BlockEntity)
      )
        findings.Add(
          Finding(
            o.Part,
            o.Hit.Index,
            name,
            $"opens {dialog} with no OnReceivedClientPacket override, so its slot clicks never "
              + "reach the server"
          )
        );
    }
    return findings;
  }

  /// <summary>Every <c>new SimpleParticleProperties</c> in <paramref name="sourceFiles"/> outside
  /// <c>ExpandedLib.Industry/Helpers/ExParticles.cs</c>.</summary>
  /// <remarks><c>ExParticles</c> is the family's shared catalogue of particle effects.</remarks>
  /// <param name="sourceFiles">C# files to read; each is read whole.</param>
  /// <returns>One line per construction, <c>file:line: Type; reason</c>, the type being the one
  /// that encloses it, or the file's name outside every type; <see cref="Key"/> keys it. Empty
  /// when clean.</returns>
  /// <exception cref="IOException">A file cannot be read, or does not exist.</exception>
  /// <exception cref="UnauthorizedAccessException">A file may not be read.</exception>
  public static IReadOnlyList<string> InlineParticles(
    IEnumerable<string> sourceFiles
  ) {
    var findings = new List<string>();
    foreach (string file in sourceFiles.OrderBy(f => f, StringComparer.Ordinal)) {
      if (
        file.Replace('\\', '/')
          .EndsWith("/" + ParticleHome, StringComparison.Ordinal)
      )
        continue;
      string code = HarnessUse.CodeOnly(File.ReadAllText(file));
      foreach (Match hit in ParticleConstruction.Matches(code))
        findings.Add(
          Finding(
            file,
            code,
            hit.Index,
            EnclosingType(code, hit.Index)
              ?? Path.GetFileNameWithoutExtension(file),
            "builds SimpleParticleProperties inline; take the effect from ExParticles"
          )
        );
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

  // Every class or record in the files by name, each part spanning its body or its closing ';';
  // parts are in path order, so a type's first hit is the same on every machine.
  private static Dictionary<string, List<TypePart>> Types(
    IEnumerable<string> sourceFiles
  ) {
    var types = new Dictionary<string, List<TypePart>>(StringComparer.Ordinal);
    foreach (string file in sourceFiles.OrderBy(f => f, StringComparer.Ordinal)) {
      string code = HarnessUse.CodeOnly(File.ReadAllText(file));
      foreach (Match type in TypeDeclaration.Matches(code)) {
        int open = code.IndexOfAny(['{', ';'], type.Index + type.Length);
        if (open < 0)
          continue;
        string? baseName = type.Groups["base"].Success
          ? type.Groups["base"].Value.Split('.')[^1]
          : null;
        var part = new TypePart(
          file,
          code,
          open,
          code[open] == ';' ? open + 1 : HarnessUse.Close(code, open + 1, '}')
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

  // Every write to a MeshData field or auto-property that parts declare directly in their bodies,
  // in part order: name =, name ??= (the group coalesce) or out name; initialisers are not writes.
  private static List<(string Field, TypePart Part, Match Hit)> MeshWrites(
    List<TypePart> parts
  ) {
    List<(TypePart Part, Match Hit)> declared =
    [
      .. parts.SelectMany(p =>
      {
        TypeBody body = Members(p);
        return Hits(p, MeshField)
          .Where(m =>
            !body.Blocks.Any(b => b.Open < m.Index && m.Index < b.End)
          )
          .Select(m => (p, m));
      }),
    ];
    var writes = new List<(string Field, TypePart Part, Match Hit)>();
    foreach (
      string field in declared
        .Select(d => d.Hit.Groups["name"].Value)
        .Distinct()
    ) {
      string name = Regex.Escape(field);
      Regex write = new(
        @"(?<![\w.])(?:this\s*\.\s*)?"
          + name
          + @"\s*(?<coalesce>\?\?)?=(?![=>])"
          + @"|\bout\s+(?:this\s*\.\s*)?"
          + name
          + @"\b"
      );
      foreach (TypePart part in parts)
        writes.AddRange(
          Hits(part, write)
            .Where(m =>
              !declared.Any(d =>
                d.Part == part
                && m.Index >= d.Hit.Index
                && m.Index < d.Hit.Index + d.Hit.Length
              )
            )
            .Select(m => (field, part, m))
        );
    }
    return writes;
  }

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

  // A block directly in a type's body, a member's or an initialiser's: the text before its '{'
  // back to the previous statement's start, the '{', and the index past its '}'.
  private sealed class MemberBlock(string header, int open, int end) {
    public string Header { get; } = header;
    public int Open { get; } = open;
    public int End { get; } = end;
  }

  // The blocks directly in a type part's body, and where each statement between them starts.
  private sealed record TypeBody(List<MemberBlock> Blocks, List<int> Starts);

  // The blocks and statement starts of part's body, in order. A property's accessor block does
  // not end its member, so an initialiser after it carries the property's name in its statement.
  private static TypeBody Members(TypePart part) {
    var body = new TypeBody([], [part.Start + 1]);
    string code = part.Code;
    int depth = 0;
    for (int i = part.Start + 1; i < part.End - 1; i++) {
      char c = code[i];
      if (c is '(' or '[')
        depth++;
      else if (c is ')' or ']')
        depth--;
      else if (depth == 0 && c == ';')
        body.Starts.Add(i + 1);
      else if (depth == 0 && c == '{') {
        int end = HarnessUse.Close(code, i + 1, '}');
        string header = code[body.Starts[^1]..i];
        body.Blocks.Add(new MemberBlock(header, i, end));
        (int assign, bool arrow) = TopLevelEquals(header);
        if (
          assign >= 0
          || arrow
          || OpenParen(header) >= 0
          || TypeKeyword.IsMatch(header)
        )
          body.Starts.Add(end);
        i = end - 1;
      }
    }
    return body;
  }

  // The member a read at position at initialises, named before the first top-level '=' of its
  // statement or block header; null when a top-level '=>' makes it live or no '=' is there.
  private static string? InitialisedMember(TypePart part, TypeBody body, int at) {
    MemberBlock? inside = body.Blocks.FirstOrDefault(b =>
      b.Open < at && at < b.End
    );
    string text =
      inside?.Header ?? part.Code[body.Starts.Last(s => s <= at)..at];
    (int eq, bool live) = TopLevelEquals(text);
    if (eq < 0 || live)
      return null;
    Match name = Regex.Match(Flatten(text[..eq]), @"(?<name>\w+)\s*$");
    return name.Success ? name.Groups["name"].Value : null;
  }

  // The first top-level assignment '=' in text, outside every bracket, and whether a top-level
  // '=>' appears anywhere; -1 when there is no assignment.
  private static (int Assign, bool Arrow) TopLevelEquals(string text) {
    int assign = -1;
    bool arrow = false;
    int depth = 0;
    for (int i = 0; i < text.Length; i++) {
      char c = text[i];
      if (c is '(' or '[' or '{')
        depth++;
      else if (c is ')' or ']' or '}')
        depth--;
      else if (depth == 0 && c == '=') {
        char before = i > 0 ? text[i - 1] : ' ';
        char after = i + 1 < text.Length ? text[i + 1] : ' ';
        if (after == '>')
          arrow = true;
        else if (
          after != '='
          && before is not ('=' or '!' or '<' or '>')
          && assign < 0
        )
          assign = i;
        if (after is '>' or '=')
          i++;
      }
    }
    return (assign, arrow);
  }

  // Text with every bracketed span removed.
  private static string Flatten(string text) {
    var kept = new System.Text.StringBuilder();
    int depth = 0;
    foreach (char c in text) {
      if (c is '(' or '[' or '{')
        depth++;
      else if (c is ')' or ']' or '}')
        depth--;
      else if (depth == 0)
        kept.Append(c);
    }
    return kept.ToString();
  }

  // The name of the method or constructor a block is the body of; null for any other block.
  private static string? MethodOf(MemberBlock block) {
    (int assign, bool arrow) = TopLevelEquals(block.Header);
    int open = OpenParen(block.Header);
    if (assign >= 0 || arrow || open < 0 || TypeKeyword.IsMatch(block.Header))
      return null;
    Match name = MethodName.Match(block.Header[..(open + 1)]);
    return name.Success ? name.Groups["name"].Value : null;
  }

  // The index of the first '(' outside brackets in header; -1 when none.
  private static int OpenParen(string header) {
    int depth = 0;
    for (int i = 0; i < header.Length; i++) {
      char c = header[i];
      if (depth == 0 && c == '(')
        return i;
      if (c is '[' or '{')
        depth++;
      else if (c is ']' or '}')
        depth--;
    }
    return -1;
  }

  // The copies into members a method body takes: an assignment whose right side holds a read,
  // outside every lambda, to a name the method does not declare as a local or parameter.
  private static IEnumerable<string> BodyCopies(
    TypePart part,
    MemberBlock method,
    string type,
    Regex read,
    string where
  ) {
    string code = part.Code;
    List<(int Start, int End)> lambdas = Lambdas(code, method);
    string scope = method.Header + code[method.Open..method.End];
    foreach (Match hit in read.Matches(code[..method.End], method.Open)) {
      if (lambdas.Any(l => hit.Index > l.Start && hit.Index < l.End))
        continue;
      int statement = code.LastIndexOfAny([';', '{', '}'], hit.Index - 1) + 1;
      Match? target = Assignment
        .Matches(code[statement..hit.Index])
        .LastOrDefault();
      if (target == null)
        continue;
      string member = target.Groups["target"].Value;
      string root = Regex.Escape(member.Split('.')[0]);
      if (
        Regex.IsMatch(
          scope,
          @"\b(?:var|bool|byte|char|decimal|double|float|int|long|short|string|uint|ulong"
            + @"|ushort|object|[A-Z]\w*(?:<[^;(){}=]*>)?)\??(?:\[\])?\s+"
            + root
            + @"\s*(?:[=;,)]|\bin\b)"
        )
      )
        continue;
      yield return Finding(
        part,
        hit.Index,
        $"{type}.{member}",
        Copies(hit, where)
      );
    }
  }

  // Where each lambda in a method body starts, at its '=>', and ends.
  private static List<(int Start, int End)> Lambdas(
    string code,
    MemberBlock method
  ) =>
    [
      .. Regex
        .Matches(code[..method.End], "=>")
        .Where(m => m.Index > method.Open)
        .Select(m => (m.Index, LambdaEnd(code, m.Index + 2))),
    ];

  // The index past the body of the lambda whose '=>' ends just before position from: its block,
  // or its expression up to the first ',', ';' or unmatched closer.
  private static int LambdaEnd(string code, int from) {
    int i = from;
    while (i < code.Length && char.IsWhiteSpace(code[i]))
      i++;
    if (i < code.Length && code[i] == '{')
      return HarnessUse.Close(code, i + 1, '}');
    int depth = 0;
    for (; i < code.Length; i++) {
      char c = code[i];
      if (c is '(' or '[' or '{')
        depth++;
      else if (c is ')' or ']' or '}') {
        if (depth == 0)
          return i;
        depth--;
      } else if (depth == 0 && c is ',' or ';')
        return i;
    }
    return code.Length;
  }

  private static string Copies(Match read, string where) =>
    $"copies {read.Groups["acc"].Value}.{read.Groups["value"].Value} {where}; an /exmod config "
    + "edit never reaches it";

  // A manageable config type, the accessor ExConfigGenerator emits for it, and its values.
  private sealed record TunableStore(
    Type Config,
    string Accessor,
    string[] Values
  );

  // The manageable config types in assemblies, by full name.
  private static List<TunableStore> TunableStores(
    IEnumerable<Assembly> assemblies
  ) {
    List<TunableStore> stores =
    [
      .. assemblies
        .Distinct()
        .SelectMany(LoadableTypes)
        .Select(t =>
          (Type: t, Register: t.GetCustomAttribute<ExConfigRegisterAttribute>())
        )
        .Where(c => c.Register is { Manageable: true })
        .OrderBy(c => c.Type.FullName, StringComparer.Ordinal)
        .Select(c => new TunableStore(
          c.Type,
          AccessorOf(c.Type, c.Register!),
          [
            .. c
              .Type.GetProperties(
                BindingFlags.Public
                  | BindingFlags.Instance
                  | BindingFlags.DeclaredOnly
              )
              .Where(p =>
                p.GetMethod is { IsPublic: true }
                && p.GetIndexParameters().Length == 0
                && p.Name != nameof(IExVersionedConfig.ConfigVersion)
              )
              .Select(p => p.Name),
          ]
        )),
    ];
    if (stores.Count == 0)
      throw new ArgumentException(
        "the assemblies hold no config type registered with Manageable = true",
        nameof(assemblies)
      );
    return stores;
  }

  // A read of any value of stores, as Accessor.Value, with the groups acc and value.
  private static Regex TunableRead(List<TunableStore> stores) =>
    new(
      $@"\b(?:{string.Join(
        "|",
        stores.Select(s =>
          $@"(?<acc>{Regex.Escape(s.Accessor)})\s*\.\s*(?<value>{string.Join("|", s.Values)})"
        )
      )})\b(?!\s*\()"
    );

  // A read of a value of stores on a config instance: .Value behind no accessor, neither called
  // nor assigned, with the group value.
  private static Regex InstanceRead(List<TunableStore> stores) =>
    new(
      $@"(?<!\b(?:{string.Join("|", stores.Select(s => Regex.Escape(s.Accessor)))})\s*)"
        + $@"\.\s*(?<value>{string.Join("|", stores.SelectMany(s => s.Values).Distinct())})\b"
        + @"(?!\s*\()(?!\s*=(?![=>]))"
    );

  // Each value's reads in the files by Accessor.Value, with whether a Lang.Get argument holds it;
  // a property that is only the read stands for it, and read by its base when an override.
  private static (
    SortedDictionary<string, List<(string File, int At, bool Shown)>> Reads,
    Dictionary<string, string> Code
  ) TunableReads(IEnumerable<string> sourceFiles, List<TunableStore> stores) {
    Regex read = TunableRead(stores);
    Regex instance = InstanceRead(stores);
    string[] files = [.. sourceFiles.OrderBy(f => f, StringComparer.Ordinal)];
    var code = files.ToDictionary(
      f => f,
      f => HarnessUse.CodeOnly(File.ReadAllText(f))
    );
    var reads = new SortedDictionary<
      string,
      List<(string File, int At, bool Shown)>
    >(StringComparer.Ordinal);
    var aliases = new List<(string Value, string Alias, string File, int At)>();
    foreach (string file in files) {
      string text = code[file];
      foreach (Match hit in read.Matches(text)) {
        if (Within(text, NameofCall, hit.Index))
          continue;
        string value = $"{hit.Groups["acc"].Value}.{hit.Groups["value"].Value}";
        int head = Math.Max(0, hit.Index - 160);
        Match alias = AliasHead.Match(text[head..hit.Index]);
        if (alias.Success && AliasTail.IsMatch(text, hit.Index + hit.Length)) {
          int at = head + alias.Index;
          aliases.Add((value, alias.Groups["alias"].Value, file, at));
          int statement = text.LastIndexOfAny([';', '{', '}'], at) + 1;
          if (OverrideKeyword.IsMatch(text[statement..at]))
            Read(reads, value, (file, hit.Index, false));
        } else
          Read(
            reads,
            value,
            (file, hit.Index, Within(text, LangCall, hit.Index))
          );
      }
      foreach (Match hit in instance.Matches(text)) {
        if (Within(text, NameofCall, hit.Index))
          continue;
        foreach (
          TunableStore store in stores.Where(s =>
            s.Values.Contains(hit.Groups["value"].Value)
          )
        )
          Read(
            reads,
            $"{store.Accessor}.{hit.Groups["value"].Value}",
            (file, hit.Index, Within(text, LangCall, hit.Index))
          );
      }
    }
    foreach ((string value, string alias, string declared, int at) in aliases)
      foreach (string file in files) {
        string text = code[file];
        foreach (Match use in Regex.Matches(text, $@"(?<!\w){alias}\b"))
          if (
            (file != declared || use.Index != at)
            && !Within(text, NameofCall, use.Index)
          )
            Read(
              reads,
              value,
              (file, use.Index, Within(text, LangCall, use.Index))
            );
      }
    return (reads, code);
  }

  // The accessor ExConfigGenerator emits for a config type.
  private static string AccessorOf(
    Type config,
    ExConfigRegisterAttribute register
  ) =>
    !string.IsNullOrWhiteSpace(register.AccessorName) ? register.AccessorName
    : config.Name.EndsWith("Config", StringComparison.Ordinal)
      ? config.Name[..^"Config".Length] + "Values"
    : config.Name + "Values";

  private static IEnumerable<Type> LoadableTypes(Assembly assembly) {
    try {
      return assembly.GetTypes();
    } catch (ReflectionTypeLoadException e) {
      return e.Types.OfType<Type>();
    }
  }

  private static void Read(
    SortedDictionary<string, List<(string File, int At, bool Shown)>> reads,
    string value,
    (string File, int At, bool Shown) read
  ) {
    if (!reads.TryGetValue(value, out List<(string, int, bool)>? all))
      reads[value] = all = [];
    all.Add(read);
  }

  // Whether position at lies inside the argument list of a call rule matches.
  private static bool Within(string code, Regex rule, int at) =>
    rule.Matches(code[..at])
      .Any(m => HarnessUse.Close(code, m.Index + m.Length, ')') > at);

  // Whether a type named name derives from target: through the bases the files list, then by
  // the game's and exlib's types.
  private static bool Derives(
    string name,
    Type target,
    Dictionary<string, List<TypePart>> types,
    HashSet<string> seen
  ) =>
    types.TryGetValue(name, out List<TypePart>? parts)
      ? seen.Add(name)
        && parts
          .Select(p => p.Base)
          .OfType<string>()
          .Any(b => Derives(b, target, types, seen))
      : Loaded(name) is { } type && target.IsAssignableFrom(type);

  // The class a type's parts derive from: a listed base in the files or among the game's and
  // exlib's classes, else the first listed; null when none is listed.
  private static string? BaseOf(
    List<TypePart> parts,
    Dictionary<string, List<TypePart>> types
  ) {
    string[] listed =
    [
      .. parts.Select(p => p.Base).OfType<string>().Distinct(),
    ];
    return listed.FirstOrDefault(b =>
        types.ContainsKey(b) || Loaded(b) is { IsClass: true }
      ) ?? listed.FirstOrDefault();
  }

  // A block entity or block entity dialog type of the game or exlib by simple name; null when
  // none or more than one has it.
  private static Type? Loaded(string name) =>
    LoadedTypes.Value.TryGetValue(name, out Type? type) ? type : null;

  // The innermost class or record whose body holds position at; null outside every one.
  private static string? EnclosingType(string code, int at) =>
    TypeDeclaration
      .Matches(code)
      .Select(m =>
        (Match: m, Open: code.IndexOfAny(['{', ';'], m.Index + m.Length))
      )
      .Where(t =>
        t.Open >= 0
        && code[t.Open] == '{'
        && t.Open < at
        && at < HarnessUse.Close(code, t.Open + 1, '}')
      )
      .Select(t => t.Match.Groups["name"].Value)
      .LastOrDefault();

  private static string Squeeze(string text) =>
    Regex.Replace(
      Regex.Replace(text.Trim(), @"\s+", " "),
      @"(?<=[(\[]) | (?=[)\]])",
      ""
    );
}
