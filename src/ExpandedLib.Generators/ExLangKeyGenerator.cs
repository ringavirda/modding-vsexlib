using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Threading;
using ExpandedLib.Generators.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ExpandedLib.Generators;

/// <summary>Emits a typed <c>{Domain}Lang</c> class of <c>public const string</c> members, one per
/// key in <c>assets/{AssetDomain}/lang/en.json</c>.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class ExLangKeyGenerator : IIncrementalGenerator {
  /// <summary>Reported when the consuming project declares <c>$(AssetDomain)</c> but
  /// <c>assets/{domain}/lang/en.json</c> did not yield a parsed lang file for it.</summary>
  private static readonly DiagnosticDescriptor NoMatchingLangFile = new(
    id: "EXLIB0003",
    title: "AssetDomain has no parsed lang file",
    messageFormat: "AssetDomain '{0}' has no parsed assets/{0}/lang/en.json among the "
      + "AdditionalFiles, so {0}Lang will not be generated",
    category: "ExpandedLib.Lang",
    DiagnosticSeverity.Warning,
    isEnabledByDefault: true
  );

  /// <summary>Reported per sanitised member-name collision, when two lang keys reduce to the same
  /// identifier.</summary>
  private static readonly DiagnosticDescriptor MemberNameCollision = new(
    id: "EXLIB0004",
    title: "Lang keys collide on their sanitised member name",
    messageFormat: "Lang keys '{0}' and '{1}' both sanitise to the same member name; '{1}' is "
      + "emitted as '{2}' instead",
    category: "ExpandedLib.Lang",
    DiagnosticSeverity.Warning,
    isEnabledByDefault: true
  );

  public void Initialize(IncrementalGeneratorInitializationContext context) {
    // Class namespace is the consuming project's RootNamespace; absent, the global namespace.
    var rootNs = context.AnalyzerConfigOptionsProvider.Select(
      static (opts, _) =>
        opts.GlobalOptions.TryGetValue(
          "build_property.RootNamespace",
          out var ns
        ) && !string.IsNullOrWhiteSpace(ns)
          ? ns
          : null
    );

    // Gates the no-emit warning; a project with no AssetDomain is expected to feed nothing.
    var assetDomain = context.AnalyzerConfigOptionsProvider.Select(
      static (opts, _) =>
        opts.GlobalOptions.TryGetValue(
          "build_property.AssetDomain",
          out var domain
        ) && !string.IsNullOrWhiteSpace(domain)
          ? domain
          : null
    );

    var langs = context
      .AdditionalTextsProvider.Where(static t => {
        string p = t.Path.Replace('\\', '/');
        return p.Contains("/lang/")
          && p.EndsWith("/en.json", StringComparison.OrdinalIgnoreCase)
          && !p.Contains("/bin/");
      })
      .Select(static (t, ct) => ReadLang(t, ct))
      .Where(static m => m is not null)
      .Collect();

    context.RegisterSourceOutput(
      langs.Combine(rootNs).Combine(assetDomain),
      static (spc, data) =>
        Emit(spc, data.Left.Left!, data.Left.Right, data.Right)
    );
  }

  private static LangModel? ReadLang(AdditionalText text, CancellationToken ct) {
    string? content = text.GetText(ct)?.ToString();
    if (content is null)
      return null;

    string domain = DomainFromPath(text.Path);
    if (domain.Length == 0)
      return null;

    var root = MiniJson.Parse(content);
    if (root is null || root.Kind != JKind.Object || root.Obj is null)
      return null;

    var keys = ImmutableArray.CreateBuilder<string>();
    foreach (var k in root.Obj.Keys)
      keys.Add(k);
    return new LangModel(
      domain,
      new EquatableArray<string>(keys.ToImmutable())
    );
  }

  // ".../assets/{domain}/lang/en.json" -> "{domain}".
  private static string DomainFromPath(string path) {
    string p = path.Replace('\\', '/');
    int lang = p.IndexOf("/lang/", StringComparison.Ordinal);
    if (lang < 0)
      return string.Empty;
    int slash = p.LastIndexOf('/', lang - 1);
    return slash < 0 ? string.Empty : p.Substring(slash + 1, lang - slash - 1);
  }

  private static void Emit(
    SourceProductionContext spc,
    ImmutableArray<LangModel> models,
    string? rootNs,
    string? assetDomain
  ) {
    // Merges keys across files sharing a domain.
    var byDomain = new Dictionary<string, SortedSet<string>>(
      StringComparer.Ordinal
    );
    foreach (var m in models) {
      if (!byDomain.TryGetValue(m.Domain, out var set))
        byDomain[m.Domain] = set = new SortedSet<string>(
          StringComparer.Ordinal
        );
      foreach (var k in m.Keys.AsSpan())
        set.Add(k);
    }

    if (assetDomain is not null && !byDomain.ContainsKey(assetDomain))
      spc.ReportDiagnostic(
        Diagnostic.Create(NoMatchingLangFile, Location.None, assetDomain)
      );

    foreach (var kv in byDomain) {
      string domain = kv.Key;
      string className = Pascal(domain) + "Lang";
      var used = new HashSet<string>(StringComparer.Ordinal);
      var owners = new Dictionary<string, string>(StringComparer.Ordinal);

      var sb = new StringBuilder();
      sb.AppendLine("// <auto-generated/> Lang key constants. Do not edit.");
      sb.AppendLine("#nullable enable");
      sb.AppendLine();
      if (!string.IsNullOrWhiteSpace(rootNs)) {
        sb.AppendLine($"namespace {rootNs};");
        sb.AppendLine();
      }
      sb.AppendLine(
        $"/// <summary>Typed lang keys for the <c>{domain}</c> domain (assets/{domain}/lang/en.json).</summary>"
      );
      sb.AppendLine($"public static class {className}");
      sb.AppendLine("{");
      // Walked in ordinal order: an earlier-sorting key claims a collision's base member name.
      foreach (string key in kv.Value) {
        string? member = Member(key, used, owners, spc);
        if (member is null)
          continue;
        string value = key.IndexOf(':') >= 0 ? key : domain + ":" + key;
        sb.AppendLine($"  /// <summary><c>{Xml(value)}</c></summary>");
        sb.AppendLine($"  public const string {member} = {Literal(value)};");
      }
      sb.AppendLine("}");

      spc.AddSource(
        $"{className}.g.cs",
        SourceText.From(sb.ToString(), Encoding.UTF8)
      );
    }
  }

  // owners maps a base (pre-suffix) name to the first key that claimed it.
  private static string? Member(
    string key,
    HashSet<string> used,
    Dictionary<string, string> owners,
    SourceProductionContext spc
  ) {
    int colon = key.LastIndexOf(':');
    string src = colon >= 0 ? key.Substring(colon + 1) : key;

    var sb = new StringBuilder(src.Length);
    bool upper = true;
    foreach (char c in src) {
      if (char.IsLetterOrDigit(c)) {
        sb.Append(upper ? char.ToUpperInvariant(c) : c);
        upper = false;
      } else {
        upper = true; // separator - capitalise the next letter
      }
    }
    if (sb.Length == 0)
      return null;
    if (char.IsDigit(sb[0]))
      sb.Insert(0, '_');

    string name = sb.ToString();
    if (used.Add(name)) {
      owners[name] = key;
      return name;
    }

    string unique = name;
    for (int n = 2; !used.Add(unique); n++)
      unique = name + "_" + n;
    spc.ReportDiagnostic(
      Diagnostic.Create(
        MemberNameCollision,
        Location.None,
        owners[name],
        key,
        unique
      )
    );
    return unique;
  }

  private static string Pascal(string s) {
    if (s.Length == 0)
      return s;
    return char.ToUpperInvariant(s[0]) + s.Substring(1);
  }

  private static string Literal(string s) =>
    "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

  private static string Xml(string s) =>
    s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

internal sealed record LangModel(string Domain, EquatableArray<string> Keys);
