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

  /// <summary>Reported when a class carries <c>[ExRecipeProfile]</c> with no companion
  /// <c>[ExConfigRegister]</c>: the recipe-profile pipeline only ever runs off the latter attribute, so
  /// without it the class generates nothing at all rather than the intended registration.</summary>
  private static readonly DiagnosticDescriptor OrphanRecipeProfile = new(
    id: "EXLIB0001",
    title: "[ExRecipeProfile] without [ExConfigRegister]",
    messageFormat: "'{0}' carries [ExRecipeProfile] but no [ExConfigRegister], so no RecipeProfile "
      + "registration will be generated. Add [ExConfigRegister] to the same class.",
    category: "ExpandedLib.Config",
    DiagnosticSeverity.Error,
    isEnabledByDefault: true
  );

  /// <summary>Reported alongside the <c>#error</c> <see cref="Emit"/> writes into the generated
  /// accessor for an invalid <c>[ExRecipeProfile]</c> shape (missing catalogue property, missing
  /// <c>DefaultCatalogue</c>, missing or unregistered level property): the <c>#error</c> is what
  /// stops the build, unsuppressible, so this diagnostic never replaces it - it only gives an IDE
  /// somewhere to navigate to, at the attribute's own location.</summary>
  private static readonly DiagnosticDescriptor InvalidRecipeProfile = new(
    id: "EXLIB0002",
    title: "[ExRecipeProfile] shape is invalid",
    messageFormat: "{0}",
    category: "ExpandedLib.Config",
    DiagnosticSeverity.Error,
    isEnabledByDefault: true
  );

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

    var orphanProfiles = context
      .SyntaxProvider.ForAttributeWithMetadataName(
        RecipeProfileAttributeName,
        predicate: static (node, _) => node is ClassDeclarationSyntax,
        transform: static (ctx, _) => ExtractOrphanDiagnostic(ctx)
      )
      .Where(static d => d is not null);

    context.RegisterSourceOutput(
      orphanProfiles,
      static (spc, diagnostic) => spc.ReportDiagnostic(diagnostic!)
    );

    var invalidProfiles = context
      .SyntaxProvider.ForAttributeWithMetadataName(
        RecipeProfileAttributeName,
        predicate: static (node, _) => node is ClassDeclarationSyntax,
        transform: static (ctx, _) => ExtractInvalidProfileDiagnostic(ctx)
      )
      .Where(static d => d is not null);

    context.RegisterSourceOutput(
      invalidProfiles,
      static (spc, diagnostic) => spc.ReportDiagnostic(diagnostic!)
    );
  }

  /// <summary>Flags a <c>[ExRecipeProfile]</c> class with no <c>[ExConfigRegister]</c> of its own -
  /// the only case <see cref="Extract"/> never sees, since its pipeline is driven by
  /// <c>[ExConfigRegister]</c>. Null when the class carries both attributes.</summary>
  private static Diagnostic? ExtractOrphanDiagnostic(GeneratorAttributeSyntaxContext ctx) {
    if (ctx.TargetSymbol is not INamedTypeSymbol type)
      return null;

    bool hasConfigRegister = type
      .GetAttributes()
      .Any(a => a.AttributeClass?.ToDisplayString() == AttributeName);
    if (hasConfigRegister)
      return null;

    return Diagnostic.Create(OrphanRecipeProfile, ctx.TargetNode.GetLocation(), type.Name);
  }

  /// <summary>Flags a <c>[ExRecipeProfile]</c> class whose shape <see cref="ExtractRecipeProfile"/>
  /// rejects, re-running that same check so the failure also lands as a diagnostic at the
  /// attribute's own location for IDE navigation - the accessor pipeline (<see cref="Extract"/>,
  /// <see cref="Emit"/>) still writes the unsuppressible <c>#error</c> regardless. Null when the
  /// class has no [ExConfigRegister] (the orphan case, EXLIB0001) or its recipe-profile shape is
  /// valid.</summary>
  private static Diagnostic? ExtractInvalidProfileDiagnostic(
    GeneratorAttributeSyntaxContext ctx
  ) {
    if (ctx.TargetSymbol is not INamedTypeSymbol type)
      return null;

    bool hasConfigRegister = type
      .GetAttributes()
      .Any(a => a.AttributeClass?.ToDisplayString() == AttributeName);
    if (!hasConfigRegister)
      return null;

    if (ExtractRecipeProfile(type) is not { IsValid: false } profile)
      return null;

    Location location =
      ctx.Attributes[0].ApplicationSyntaxReference?.GetSyntax().GetLocation()
      ?? ctx.TargetNode.GetLocation();
    return Diagnostic.Create(InvalidRecipeProfile, location, profile.Error);
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
  /// property), the level property it names or defaults to, and the generated accessor that owns that
  /// level property - <paramref name="type"/> itself, or <see cref="ExRecipeProfileAttribute.LevelConfig"/>
  /// when the two live on different <c>[ExConfigRegister]</c> configs, as the family mods' recipe
  /// catalogue and its owning gameplay config do. Returns null when the config carries no
  /// <c>[ExRecipeProfile]</c>.</summary>
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

    var levelConfigArg = attr
      .NamedArguments.FirstOrDefault(na => na.Key == "LevelConfig")
      .Value;
    INamedTypeSymbol? levelType =
      levelConfigArg.Kind == TypedConstantKind.Type && !levelConfigArg.IsNull
        ? levelConfigArg.Value as INamedTypeSymbol
        : type;
    string? levelAccessor = levelType is null ? null : ResolveAccessorName(levelType);

    var catalogueCandidates = type
      .GetMembers()
      .OfType<IPropertySymbol>()
      .Where(p =>
        !p.IsStatic
        && p.DeclaredAccessibility == Accessibility.Public
        && p.GetMethod is not null
        && IsRecipeCostDictionary(p.Type)
      )
      .ToImmutableArray();

    bool hasDefaultsMethod = type
      .GetMembers("DefaultCatalogue")
      .OfType<IMethodSymbol>()
      .Any(m =>
        m.IsStatic
        && m.DeclaredAccessibility == Accessibility.Public
        && m.Parameters.Length == 0
        && IsRecipeCostDictionary(m.ReturnType)
      );

    bool hasLevelProperty =
      levelType is not null
      && levelType
        .GetMembers(levelProperty)
        .OfType<IPropertySymbol>()
        .Any(p =>
          !p.IsStatic
          && p.DeclaredAccessibility == Accessibility.Public
          && p.GetMethod is not null
          && p.SetMethod is not null
          && p.Type.SpecialType == SpecialType.System_String
        );

    if (
      catalogueCandidates.Length == 1
      && hasDefaultsMethod
      && levelAccessor is not null
      && hasLevelProperty
    ) {
      return new RecipeProfileModel(
        IsValid: true,
        CatalogueProperty: catalogueCandidates[0].Name,
        LevelProperty: levelProperty,
        LevelAccessor: levelAccessor,
        Error: string.Empty
      );
    }

    string levelTypeName = levelType?.Name ?? "LevelConfig";
    string error = catalogueCandidates.Length == 0
      ? "needs exactly one public Dictionary<string, RecipeCostEntry> property (found none)"
      : catalogueCandidates.Length > 1
        ? "needs exactly one public Dictionary<string, RecipeCostEntry> property (found more than one)"
        : !hasDefaultsMethod
          ? "needs a public static DefaultCatalogue() method returning Dictionary<string, RecipeCostEntry>"
          : levelType is null
            ? "LevelConfig must name a class"
            : levelAccessor is null
              ? $"LevelConfig {levelTypeName} needs its own [ExConfigRegister] attribute"
              : $"needs a public string property named '{levelProperty}' with a getter and setter on {levelTypeName}";
    return new RecipeProfileModel(
      IsValid: false,
      CatalogueProperty: string.Empty,
      LevelProperty: levelProperty,
      LevelAccessor: string.Empty,
      Error: error
    );
  }

  /// <summary>True when <paramref name="type"/> is <c>Dictionary&lt;string, RecipeCostEntry&gt;</c> -
  /// the shape both the catalogue property and <c>DefaultCatalogue()</c>'s return type must have.</summary>
  private static bool IsRecipeCostDictionary(ITypeSymbol type) =>
    type is INamedTypeSymbol { Name: "Dictionary", TypeArguments.Length: 2 } dict
    && dict.TypeArguments[0].SpecialType == SpecialType.System_String
    && dict.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
      == RecipeCostEntryType;

  /// <summary>Resolves the fully qualified name of the accessor <c>ExConfigGenerator</c> generates for
  /// <paramref name="configType"/>, by the same rule <see cref="Extract"/> applies when generating that
  /// type's own accessor. Null when <paramref name="configType"/> carries no
  /// <c>[ExConfigRegister]</c>.</summary>
  private static string? ResolveAccessorName(INamedTypeSymbol configType) {
    var registerAttr = configType
      .GetAttributes()
      .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == AttributeName);
    if (registerAttr is null)
      return null;

    string? accessorName =
      registerAttr.NamedArguments.FirstOrDefault(na => na.Key == "AccessorName")
        .Value.Value as string;
    if (string.IsNullOrWhiteSpace(accessorName))
      accessorName = DefaultAccessorName(configType.Name);

    string? ns = configType.ContainingNamespace.IsGlobalNamespace
      ? null
      : configType.ContainingNamespace.ToDisplayString();
    return ns is null ? $"global::{accessorName}" : $"global::{ns}.{accessorName}";
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
        $"    GetLevel = () => {profile.LevelAccessor}.{profile.LevelProperty},"
      );
      loadStatements.Add(
        $"    SetLevel = level => {profile.LevelAccessor}.Edit(c => c.{profile.LevelProperty} = level),"
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

/// <summary>Resolution of a config's <c>[ExRecipeProfile]</c> attribute: either the catalogue property,
/// the level property, and the fully qualified generated accessor that owns the level property (the
/// config's own, or <see cref="ExRecipeProfileAttribute.LevelConfig"/>'s) to emit the registration
/// from, or the reason it could not be resolved (an <c>#error</c> in the generated source names
/// it).</summary>
internal sealed record RecipeProfileModel(
  bool IsValid,
  string CatalogueProperty,
  string LevelProperty,
  string LevelAccessor,
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
