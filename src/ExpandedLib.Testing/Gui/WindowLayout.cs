using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Client;

namespace ExpandedLib.Testing;

/// <summary>
/// The layout gate over a composed dialog: text that does not fit its box, content elements that
/// overlap their siblings, and elements outside the dialog.
/// </summary>
public static class WindowLayout {
  private const double Tolerance = 0.5;

  /// <summary>Lists what is wrong with the layout of <paramref name="composer"/>, measured in the
  /// pixels of the GUI scale it was composed at.</summary>
  /// <remarks>Rules, each finding starting with its name and the element's key (with its text when
  /// it has some): <c>height</c>, a text element whose wrapped text is taller than its inner box;
  /// <c>width</c>, a text element with a word wider than its inner box, which the engine breaks
  /// inside the word, which is any word as wide as its box or wider; <c>overlap</c>, two content
  /// elements with the same parent bounds whose rectangles share more than half a pixel in both
  /// axes; <c>outside</c>, an element whose rectangle leaves the dialog's. Content elements are
  /// every element except dialog backgrounds, title bars, insets, clip markers and hover texts, which sit under
  /// or beside content by design, so passive item slots (<see cref="GuiElementPassiveItemSlot"/>),
  /// scrollbars and custom elements are compared; hover texts are also skipped by the text rules. Buttons size themselves to their text
  /// and are not measured for fit.</remarks>
  /// <param name="composer">A composed composer made on a <see cref="WindowRig"/>; composed here when
  /// it is not.</param>
  /// <returns>One line per finding; empty when the layout holds.</returns>
  public static IReadOnlyList<string> Check(GuiComposer composer) {
    composer.Compose(false);
    List<(string Key, GuiElement Element)> all = Elements(composer);
    var findings = new List<string>();

    foreach ((string key, GuiElement element) in all) {
      if (
        element is not GuiElementTextBase text
        || element is GuiElementHoverText
      )
        continue;
      string content = text.Text ?? "";
      double boxW = text.Bounds.InnerWidth;
      double boxH = text.Bounds.InnerHeight;
      string label = Label(key, content);
      double height = text.textUtil.GetMultilineTextHeight(
        text.Font,
        content,
        boxW
      );
      if (height > boxH + Tolerance)
        findings.Add(
          $"height {label}: wrapped text is {height:0.#} tall, box is {boxH:0.#}"
        );
      foreach (
        string word in content.Split(
          (char[]?)null,
          StringSplitOptions.RemoveEmptyEntries
        )
      ) {
        double wordW = text.Font.GetTextExtents(word).Width;
        if (wordW >= boxW)
          findings.Add(
            $"width {label}: the word \"{word}\" is {wordW:0.#} wide, box is {boxW:0.#}"
          );
      }
    }

    List<(string Key, GuiElement Element)> content2 = all.Where(e =>
        IsContent(e.Element)
      )
      .ToList();
    for (int i = 0; i < content2.Count; i++)
      for (int j = i + 1; j < content2.Count; j++) {
        ElementBounds a = content2[i].Element.Bounds;
        ElementBounds b = content2[j].Element.Bounds;
        if (!ReferenceEquals(a.ParentBounds, b.ParentBounds))
          continue;
        double dx =
          Math.Min(a.renderX + a.OuterWidth, b.renderX + b.OuterWidth)
          - Math.Max(a.renderX, b.renderX);
        double dy =
          Math.Min(a.renderY + a.OuterHeight, b.renderY + b.OuterHeight)
          - Math.Max(a.renderY, b.renderY);
        if (dx > Tolerance && dy > Tolerance)
          findings.Add(
            $"overlap {Label(content2[i])} and {Label(content2[j])}: they share {dx:0.#} x {dy:0.#}"
          );
      }

    ElementBounds dialog = composer.Bounds;
    foreach ((string key, GuiElement element) in all) {
      ElementBounds b = element.Bounds;
      if (
        b.renderX < dialog.renderX - Tolerance
        || b.renderY < dialog.renderY - Tolerance
        || b.renderX + b.OuterWidth
          > dialog.renderX + dialog.OuterWidth + Tolerance
        || b.renderY + b.OuterHeight
          > dialog.renderY + dialog.OuterHeight + Tolerance
      )
        findings.Add(
          $"outside {Label((key, element))}: {b.OuterWidth:0.#} x {b.OuterHeight:0.#} at "
            + $"{b.renderX - dialog.renderX:0.#},{b.renderY - dialog.renderY:0.#}, dialog is "
            + $"{dialog.OuterWidth:0.#} x {dialog.OuterHeight:0.#}"
        );
    }
    return findings;
  }

  private static readonly HashSet<string> Underlays =
  [
    nameof(GuiElementDialogBackground),
    nameof(GuiElementDialogTitleBar),
    nameof(GuiElementInset),
    nameof(GuiElementHoverText),
    "GuiElementClip",
  ];

  private static bool IsContent(GuiElement e) =>
    !Underlays.Contains(e.GetType().Name);

  private static string Label((string Key, GuiElement Element) e) =>
    Label(e.Key, (e.Element as GuiElementTextBase)?.Text ?? "");

  private static string Label(string key, string text) =>
    text.Length == 0 ? key : $"{key} \"{text}\"";

  private static List<(string, GuiElement)> Elements(GuiComposer composer) {
    var result = new List<(string, GuiElement)>();
    foreach (string field in new[] { "staticElements", "interactiveElements" }) {
      var dict = (IDictionary)(
        typeof(GuiComposer)
          .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
          ?.GetValue(composer)
        ?? throw new MissingFieldException(nameof(GuiComposer), field)
      );
      foreach (DictionaryEntry e in dict)
        if (!result.Any(r => ReferenceEquals(r.Item2, e.Value)))
          result.Add(((string)e.Key, (GuiElement)e.Value!));
    }
    return result;
  }
}
