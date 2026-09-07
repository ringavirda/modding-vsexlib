using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ExpandedLib.Generators;

/// <summary>
/// Source generator that emits the static accessor class for every config POCO marked
/// <c>[ExConfigRegister(fileName, modId)]</c>. For a config <c>IiexConfig</c> it generates a
/// <c>IiexValues</c> static partial class holding the <c>ConfigFileName</c> const, the backing
/// <c>ExConfigRegister&lt;IiexConfig&gt;</c>, a <c>Load(ICoreAPI)</c> method and one read-only
/// <c>public static</c> property per config value. The class also carries
/// <c>[ExConfigAccessor(typeof(IiexConfig))]</c>, so <c>ExConfig.LoadAll</c> can find it by reflection.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ExConfigGenerator : IIncrementalGenerator {
  private const string AttributeName =
    "ExpandedLib.Config.ExConfigRegisterAttribute";
  private const string RecipeProfileAttributeName =
    "ExpandedLib.Config.ExRecipeProfileAttribute";
  private const string RecipeCostEntryType =
    "global::ExpandedLib.Registries.RecipeCostEntry";
  private const string ConfigVersionProperty = "ConfigVersion";

  public void Initialize(IncrementalGeneratorInitializationContext context) {
    var models = context
      .SyntaxProvider.ForAttributeWithMetadataName(
        AttributeName,
        predicate: static (node, _) => node is ClassDeclarationSyntax,
        transform: static (ctx, _) => Extract(ctx)
      )
      .Where(static m => m is not null);

    context.RegisterSourceOutput(
      models,
      static (spc, model) => Emit(spc, model!)
    );
  }

  private static ConfigModel? Extract(GeneratorAttributeSyntaxContext ctx) {
    if (ctx.TargetSymbol is not INamedTypeSymbol type)
      return null;

    var attr = ctx.Attributes[0];
    if (attr.ConstructorArguments.Length < 2)
      return null;

    string fileName =
      attr.ConstructorArguments[0].Value as string ?? string.Empty;
    string modId = attr.ConstructorArguments[1].Value as string ?? string.Empty;

    string? accessorName =
      attr.NamedArguments.FirstOrDefault(na =>
        na.Key == "AccessorName"
      ).Value.Value as string;
    if (string.IsNullOrWhiteSpace(accessorName))
      accessorName = DefaultAccessorName(type.Name);

    var legacyArg = attr
      .NamedArguments.FirstOrDefault(na => na.Key == "LegacyFileNames")
      .Value;
    var legacyNames =
      legacyArg.Kind == TypedConstantKind.Array && !legacyArg.IsNull
        ? legacyArg
          .Values.Select(v => v.Value as string)
          .Where(s => !string.IsNullOrEmpty(s))
          .Select(s => s!)
          .ToImmutableArray()
        : ImmutableArray<string>.Empty;

    var legacySectionArg = attr
      .NamedArguments.FirstOrDefault(na => na.Key == "LegacySectionIds")
      .Value;
    var legacySections =
      legacySectionArg.Kind == TypedConstantKind.Array
      && !legacySectionArg.IsNull
        ? legacySectionArg
          .Values.Select(v => v.Value as string)
          .Where(s => !string.IsNullOrEmpty(s))
          .Select(s => s!)
          .ToImmutableArray()
        : ImmutableArray<string>.Empty;

    bool manageable =
      attr
        .NamedArguments.FirstOrDefault(na => na.Key == "Manageable")
        .Value.Value
      is true;

    bool hasMigrations = type.GetMembers("Migrations")
      .Any(m => m.IsStatic && m is IFieldSymbol or IPropertySymbol);

    var properties = type.GetMembers()
      .OfType<IPropertySymbol>()
      .Where(p =>
        !p.IsStatic
        && !p.IsIndexer
        && p.DeclaredAccessibility == Accessibility.Public
        && p.GetMethod is not null
        && p.Name != ConfigVersionProperty
      )
      .Select(p => new PropModel(
        p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        p.Name
      ))
      .ToImmutableArray();

    string? ns = type.ContainingNamespace.IsGlobalNamespace
      ? null
      : type.ContainingNamespace.ToDisplayString();

    return new ConfigModel(
      ns,
      type.Name,
      accessorName!,
      fileName,
      modId,
      hasMigrations,
      manageable,
      new EquatableArray<string>(legacyNames),
      new EquatableArray<string>(legacySections),
      new EquatableArray<PropModel>(properties),
      ExtractRecipeProfile(type)
    );
  }

  /// <summary>Reads the co-located <c>[ExRecipeProfile]</c> attribute, if any, and resolves the
  /// catalogue property (the config's sole <c>Dictionary&lt;string, RecipeCostEntry&gt;</c>
  /// property) and the level property it names or defaults to. Returns null when the config carries
  /// no <c>[ExRecipeProfile]</c>.</summary>
  private static RecipeProfileModel? ExtractRecipeProfile(INamedTypeSymbol type) {
    var attr = type
      .GetAttributes()
      .FirstOrDefault(a =>
        a.AttributeClass?.ToDisplayString() == RecipeProfileAttributeName
      );
    if (attr is null)
      return null;

    string levelProperty =
      attr.NamedArguments.FirstOrDefault(na => na.Key == "RecipeLevelProperty")
        .Value.Value as string ?? "RecipeLevel";

    var catalogueCandidates = type
      .GetMembers()
      .OfType<IPropertySymbol>()
      .Where(p =>
        !p.IsStatic
        && p.DeclaredAccessibility == Accessibility.Public
        && p.GetMethod is not null
        && p.Type is INamedTypeSymbol {
          Name: "Dictionary",
          TypeArguments.Length: 2,
        } dict
        && dict.TypeArguments[0].SpecialType == SpecialType.System_String
        && dict.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
          == RecipeCostEntryType
      )
      .ToImmutableArray();

    bool hasDefaultsMethod = type
      .GetMembers("DefaultCatalogue")
      .OfType<IMethodSymbol>()
      .Any(m => m.IsStatic && m.Parameters.Length == 0);

    bool hasLevelProperty = type
      .GetMembers(levelProperty)
      .OfType<IPropertySymbol>()
      .Any(p =>
        !p.IsStatic
        && p.DeclaredAccessibility == Accessibility.Public
        && p.GetMethod is not null
        && p.SetMethod is not null
        && p.Type.SpecialType == SpecialType.System_String
      );

    if (catalogueCandidates.Length == 1 && hasDefaultsMethod && hasLevelProperty) {
      return new RecipeProfileModel(
        IsValid: true,
        CatalogueProperty: catalogueCandidates[0].Name,
        LevelProperty: levelProperty,
        Error: string.Empty
      );
    }

    string error = catalogueCandidates.Length == 0
      ? "needs exactly one public Dictionary<string, RecipeCostEntry> property (found none)"
      : catalogueCandidates.Length > 1
        ? "needs exactly one public Dictionary<string, RecipeCostEntry> property (found more than one)"
        : !hasDefaultsMethod
          ? "needs a public static DefaultCatalogue() method returning the catalogue"
          : $"needs a public string property named '{levelProperty}' with a getter and setter";
    return new RecipeProfileModel(
      IsValid: false,
      CatalogueProperty: string.Empty,
      LevelProperty: levelProperty,
      Error: error
    );
  }

  private static void Emit(SourceProductionContext spc, ConfigModel m) {
    var sb = new StringBuilder();
    sb.AppendLine("// <auto-generated/>");
    sb.AppendLine("#nullable enable");
    sb.AppendLine("using Vintagestory.API.Common;");
    sb.AppendLine("using ExpandedLib.Config;");
    sb.AppendLine();
    if (m.Namespace is not null) {
      sb.AppendLine($"namespace {m.Namespace};");
      sb.AppendLine();
    }

    sb.AppendLine(
      "/// <summary>Generated accessor for the mod's gameplay tunables - see the"
    );
    sb.AppendLine(
      $"/// <c>{m.ConfigTypeName}</c> POCO and the <c>[ExConfig]</c> attribute on it.</summary>"
    );
    sb.AppendLine(
      $"[global::ExpandedLib.Config.ExConfigAccessor(typeof({m.ConfigTypeName}))]"
    );
    sb.AppendLine($"public static partial class {m.AccessorName}");
    sb.AppendLine("{");
    sb.AppendLine(
      "  /// <summary>Config file name, written under the game's <c>ModConfig</c> folder.</summary>"
    );
    sb.AppendLine($"  public const string ConfigFileName = \"{m.FileName}\";");
    sb.AppendLine();

    string migrationsArg = m.HasMigrations
      ? $", {m.ConfigTypeName}.Migrations"
      : string.Empty;
    sb.AppendLine(
      $"  private static readonly ExConfigRegister<{m.ConfigTypeName}> _store ="
    );
    var initializers = new List<string>(2);
    AddArrayInitializer(initializers, "LegacyFileNames", m.LegacyFileNames);
    AddArrayInitializer(initializers, "LegacySectionIds", m.LegacySectionIds);
    if (initializers.Count > 0) {
      sb.AppendLine($"    new(ConfigFileName, \"{m.ModId}\"{migrationsArg})");
      sb.AppendLine($"    {{ {string.Join(", ", initializers)} }};");
    } else {
      sb.AppendLine($"    new(ConfigFileName, \"{m.ModId}\"{migrationsArg});");
    }
    sb.AppendLine();
    sb.AppendLine(
      $"  private static {m.ConfigTypeName} _config => _store.Config;"
    );
    sb.AppendLine();
    sb.AppendLine(
      "  /// <summary>Loads the config (falling back to defaults), applies any"
    );
    sb.AppendLine(
      "  /// version-change resets and writes it back. Call once during mod startup.</summary>"
    );
    if (m.RecipeProfile is { IsValid: false } badProfile) {
      sb.AppendLine(
        $"#error [ExRecipeProfile] on {m.ConfigTypeName} {badProfile.Error}"
      );
    }

    var loadStatements = new List<string> { "_store.Load(api);" };
    if (m.Manageable) {
      loadStatements.Add(
        "// Manageable config: expose its values to the generic /exmod config command."
      );
      loadStatements.Add("ExConfigProfiles.Register(_store);");
    }
    if (m.RecipeProfile is { IsValid: true } profile) {
      loadStatements.Add(
        "// [ExRecipeProfile]: register with the shared recipe-cost framework."
      );
      loadStatements.Add(
        "global::ExpandedLib.Registries.ExRecipeProfiles.Register("
      );
      loadStatements.Add(
        "  new global::ExpandedLib.Registries.RecipeProfile"
      );
      loadStatements.Add("  {");
      loadStatements.Add($"    Code = \"{m.ModId}\",");
      loadStatements.Add(
        $"    Catalogue = () => _config.{profile.CatalogueProperty},"
      );
      loadStatements.Add($"    Defaults = {m.ConfigTypeName}.DefaultCatalogue,");
      loadStatements.Add(
        $"    GetLevel = () => _config.{profile.LevelProperty},"
      );
      loadStatements.Add(
        $"    SetLevel = level => Edit(c => c.{profile.LevelProperty} = level),"
      );
      loadStatements.Add("    SaveCatalogue = Save,");
      loadStatements.Add("  }");
      loadStatements.Add(");");
    }

    if (loadStatements.Count == 1) {
      sb.AppendLine(
        $"  public static void Load(ICoreAPI api) => {loadStatements[0]}"
      );
    } else {
      sb.AppendLine("  public static void Load(ICoreAPI api)");
      sb.AppendLine("  {");
      foreach (var line in loadStatements)
        sb.AppendLine($"    {line}");
      sb.AppendLine("  }");
    }
    sb.AppendLine();
    sb.AppendLine(
      "  /// <summary>Mutates the live config through <paramref name=\"mutate\"/> and writes it back."
    );
    sb.AppendLine(
      "  /// For runtime admin edits; server-side in practice (config is host-authoritative).</summary>"
    );
    sb.AppendLine(
      $"  public static void Edit(System.Action<{m.ConfigTypeName}> mutate)"
    );
    sb.AppendLine("  {");
    sb.AppendLine("    mutate(_store.Config);");
    sb.AppendLine("    _store.Save();");
    sb.AppendLine("  }");
    sb.AppendLine();
    sb.AppendLine(
      "  /// <summary>Persists the live config to its file on disk.</summary>"
    );
    sb.AppendLine("  public static void Save() => _store.Save();");

    foreach (var p in m.Properties.AsSpan()) {
      sb.AppendLine();
      sb.AppendLine($"  public static {p.Type} {p.Name} => _config.{p.Name};");
    }

    sb.AppendLine("}");

    spc.AddSource(
      $"{m.AccessorName}.g.cs",
      SourceText.From(sb.ToString(), Encoding.UTF8)
    );
  }

  /// <summary>Appends <c>Name = new string[] { ... }</c> for a non-empty list, so the store's object
  /// initializer carries only the lists the attribute actually declared.</summary>
  private static void AddArrayInitializer(
    List<string> into,
    string name,
    EquatableArray<string> values
  ) {
    var span = values.AsSpan();
    if (span.Length == 0)
      return;
    var quoted = new List<string>(span.Length);
    foreach (var v in span)
      quoted.Add($"\"{v}\"");
    into.Add($"{name} = new string[] {{ {string.Join(", ", quoted)} }}");
  }

  private static string DefaultAccessorName(string typeName) =>
    typeName.EndsWith("Config", StringComparison.Ordinal)
      ? typeName.Substring(0, typeName.Length - "Config".Length) + "Values"
      : typeName + "Values";
}

internal sealed record ConfigModel(
  string? Namespace,
  string ConfigTypeName,
  string AccessorName,
  string FileName,
  string ModId,
  bool HasMigrations,
  bool Manageable,
  EquatableArray<string> LegacyFileNames,
  EquatableArray<string> LegacySectionIds,
  EquatableArray<PropModel> Properties,
  RecipeProfileModel? RecipeProfile
);

internal readonly record struct PropModel(string Type, string Name);

/// <summary>Resolution of a config's <c>[ExRecipeProfile]</c> attribute: either the catalogue and
/// level property names to emit the registration from, or the reason it could not be resolved (an
/// <c>#error</c> in the generated source names it).</summary>
internal sealed record RecipeProfileModel(
  bool IsValid,
  string CatalogueProperty,
  string LevelProperty,
  string Error
);

/// <summary>Value-equatable wrapper over <see cref="ImmutableArray{T}"/> so generator models compare
/// by content, which the incremental pipeline requires to cache across edits.</summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>
  where T : IEquatable<T> {
  private readonly ImmutableArray<T> _array;

  public EquatableArray(ImmutableArray<T> array) => _array = array;

  public ReadOnlySpan<T> AsSpan() =>
    _array.IsDefault ? ReadOnlySpan<T>.Empty : _array.AsSpan();

  public bool Equals(EquatableArray<T> other) =>
    AsSpan().SequenceEqual(other.AsSpan());

  public override bool Equals(object? obj) =>
    obj is EquatableArray<T> other && Equals(other);

  public override int GetHashCode() {
    unchecked {
      int hash = 17;
      foreach (var item in AsSpan())
        hash = hash * 31 + EqualityComparer<T>.Default.GetHashCode(item);
      return hash;
    }
  }
}
