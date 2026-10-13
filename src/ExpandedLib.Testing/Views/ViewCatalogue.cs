using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing;

/// <summary>One sized item of a design page's table.</summary>
/// <param name="Code">The item's code with its domain; a <c>{variant}</c> segment matches any text.</param>
/// <param name="Extents">The row's three extents in voxels, ascending.</param>
/// <param name="Page">The page's path under the docs root.</param>
/// <param name="Line">The 1-based line of the row in the page.</param>
internal sealed record CatalogueRow(
  string Code,
  double[] Extents,
  string Page,
  int Line
) {
  internal bool Matches(string code) =>
    Code.Contains('{')
      ? Regex.IsMatch(
        code,
        "^" + Regex.Replace(Regex.Escape(Code), @"\\\{[^}]*\}", ".+") + "$"
      )
      : Code == code;

  internal string Where => $"{Page}:{Line}";
}

/// <summary>The sizes the design pages give stock items: every table under <c>design</c> whose first column is
/// <c>Item</c> and that has a <c>Section x length</c> column.</summary>
internal sealed class ViewCatalogue {
  /// <summary>How far a drawn extent may differ from the row's, in voxels.</summary>
  internal const double Tolerance = 0.05;

  private const string ItemColumn = "Item";
  private const string SizeColumn = "Section x length";

  private static readonly Regex Size = new(
    @"(\d+(?:\.\d+)?) x (\d+(?:\.\d+)?) x (\d+(?:\.\d+)?)",
    RegexOptions.Compiled
  );

  private static readonly Regex Token = new("`([^`]+)`", RegexOptions.Compiled);

  /// <summary>The rows that carry one size, in page and line order, with those of <c>game:</c> items, of proposed items,
  /// of rows with no size or several (a composite) left out.</summary>
  internal IReadOnlyList<CatalogueRow> Rows { get; }

  private ViewCatalogue(IReadOnlyList<CatalogueRow> rows) {
    Rows = rows;
  }

  /// <summary>Reads the tables under <c>&lt;docsRoot&gt;/design</c>.</summary>
  /// <param name="docsRoot">The docs root, such as <see cref="RepoPaths.DocsRoot"/>; null when the repository names none.</param>
  /// <param name="defaultDomain">The domain of a code that names none, the guarded mod's.</param>
  /// <returns>The catalogue; null when <paramref name="docsRoot"/> is null.</returns>
  /// <exception cref="DirectoryNotFoundException"><paramref name="docsRoot"/> names a folder that does not
  /// exist.</exception>
  internal static ViewCatalogue? Load(string? docsRoot, string defaultDomain) {
    if (docsRoot == null)
      return null;
    if (!Directory.Exists(docsRoot))
      throw new DirectoryNotFoundException(
        $"The docs root {docsRoot} does not exist. Clone exdocs beside the repository so that path resolves."
      );
    string design = Path.Combine(docsRoot, "design");
    var rows = new List<CatalogueRow>();
    if (Directory.Exists(design))
      foreach (
        string file in Directory
          .EnumerateFiles(design, "*.md", SearchOption.AllDirectories)
          .Order(StringComparer.Ordinal)
      )
        rows.AddRange(
          Parse(
            File.ReadAllLines(file),
            Path.GetRelativePath(docsRoot, file).Replace('\\', '/'),
            defaultDomain
          )
        );
    return new ViewCatalogue(rows);
  }

  private static IEnumerable<CatalogueRow> Parse(
    string[] lines,
    string page,
    string defaultDomain
  ) {
    int size = -1;
    for (int i = 0; i < lines.Length; i++) {
      if (!lines[i].StartsWith('|')) {
        size = -1;
        continue;
      }
      string[] cells = Cells(lines[i]);
      if (
        cells[0] == ItemColumn
        && Array.IndexOf(cells, SizeColumn) is var at
        && at > 0
      ) {
        size = at;
        i++;
        continue;
      }
      if (size < 0 || cells.Length <= size)
        continue;
      string codeCell = cells[0],
        sizeCell = cells[size];
      if (
        codeCell.StartsWith("Proposed", StringComparison.Ordinal)
        || sizeCell.Contains("Proposed", StringComparison.Ordinal)
      )
        continue;
      Match code = Token.Match(codeCell);
      MatchCollection sizes = Size.Matches(sizeCell);
      if (!code.Success || sizes.Count != 1)
        continue;
      string text = code.Groups[1].Value;
      if (text.StartsWith("game:", StringComparison.Ordinal))
        continue;
      if (!text.Contains(':'))
        text = defaultDomain + ":" + text;
      double[] extents =
      [
        .. Enumerable
          .Range(1, 3)
          .Select(n =>
            double.Parse(sizes[0].Groups[n].Value, CultureInfo.InvariantCulture)
          )
          .Order(),
      ];
      yield return new CatalogueRow(text, extents, page, i + 1);
    }
  }

  private static string[] Cells(string line) =>
    [.. line.Trim().Trim('|').Split('|').Select(c => c.Trim())];

  /// <summary>The codes two or more pages give different sizes.</summary>
  internal IReadOnlyList<string> Disagreements() =>
    [
      .. Rows.GroupBy(r => r.Code)
        .Where(g => g.Any(r => !Same(r.Extents, g.First().Extents)))
        .Select(g => g.Key),
    ];

  /// <summary>The G12 failure of each row whose code no collectible of <paramref name="world"/> has.</summary>
  /// <param name="world">A world from <see cref="TestWorld.LoadAsGame"/>.</param>
  /// <param name="domain">The domain whose rows are checked; null checks every row.</param>
  /// <returns>One finding per unregistered row, in page and line order, whose message names the row's page and line;
  /// empty when every row names a collectible.</returns>
  internal IReadOnlyList<ViewFinding> Unregistered(
    TestWorld world,
    string? domain = null
  ) {
    string[] codes =
    [
      .. world
        .World.Collectibles.Where(c => c.Code != null)
        .Select(c => c.Code.ToString()),
    ];
    return
    [
      .. Rows.Where(r =>
          (
            domain == null
            || r.Code.StartsWith(domain + ":", StringComparison.Ordinal)
          ) && !codes.Any(r.Matches)
        )
        .Select(r =>
        {
          string key = $"{r.Code} [catalogue] G12 unregistered";
          return new ViewFinding(
            key,
            $"{key} at {r.Where}: no collectible has this code"
          );
        }),
    ];
  }

  /// <summary>The rule G12 catalogue for <paramref name="group"/>: its box against every row that names one of its codes.</summary>
  /// <param name="group">The group to judge.</param>
  /// <returns>The reading; null when no row names a code of the group.</returns>
  internal GateReading? Reading(ViewGroup group) {
    double[] drawn = [.. group.Box.Extents().Order()];
    (CatalogueRow Row, double Off)[] misses =
    [
      .. Rows.Where(r => group.Codes.Any(r.Matches))
        .Select(r => (r, Off(drawn, r.Extents))),
    ];
    if (misses.Length == 0)
      return null;
    (CatalogueRow worstRow, double worst) = misses.MaxBy(m => m.Off);
    return new GateReading(
      "G12",
      "catalogue",
      "item",
      worst,
      null,
      Tolerance,
      null,
      worst <= Tolerance,
      $"drawn {string.Join(" x ", drawn.Select(Format))} against {string.Join(" x ", worstRow.Extents.Select(Format))} at {worstRow.Where}"
    );
  }

  private static double Off(double[] a, double[] b) =>
    a.Zip(b, (x, y) => Math.Abs(x - y)).Max();

  private static bool Same(double[] a, double[] b) => Off(a, b) <= Tolerance;

  private static string Format(double v) =>
    v.ToString("0.##", CultureInfo.InvariantCulture);
}
