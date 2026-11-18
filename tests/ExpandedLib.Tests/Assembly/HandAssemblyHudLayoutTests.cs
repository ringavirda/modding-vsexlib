using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Assembling;
using ExpandedLib.Testing;
using Newtonsoft.Json;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The HUD text is measured and broken into whole lines, one where it fits the screen, so no line is cropped or cut mid-word.</summary>
public class HandAssemblyHudLayoutTests {
  private const double CharWidth = 8;

  private static double Measure(string text) => text.Length * CharWidth;

  private static readonly (string Part, string Fastener)[] Names =
  [
    ("Cross", "Bolt bundle"),
    ("Крестовина", "Болтовой набор"),
    ("Хрестовина", "Болтовий набір"),
  ];

  private static string Longest(string locale, string part, string fastener) {
    var entries = JsonConvert.DeserializeObject<Dictionary<string, string>>(
      File.ReadAllText(
        Path.Combine(RepoPaths.Assets("exlib"), "lang", locale + ".json")
      )
    )!;
    string Get(string key, object[] args) =>
      string.Format(entries[key.Replace("exlib:", "")], args);
    return HandAssemblyHud.Line(
      Get,
      4,
      4,
      part,
      [
        ("Pipe", BlockFacing.NORTH),
        ("Pipe", BlockFacing.EAST),
        ("Pipe", BlockFacing.SOUTH),
        ("Pipe", BlockFacing.WEST),
      ],
      [],
      4,
      fastener,
      37
    );
  }

  public static IEnumerable<object[]> Locales() =>
    new[] { "en", "ru", "uk" }.Select(
      (locale, i) => new object[] { locale, Names[i].Part, Names[i].Fastener }
    );

  // Fails when the width is capped at the old fixed 640 px.
  [Theory]
  [MemberData(nameof(Locales))]
  public void Layout_LongestLineIsOneLineOnAWideScreen(
    string locale,
    string part,
    string fastener
  ) {
    string text = Longest(locale, part, fastener);

    HandAssemblyHud.HudLayout layout = HandAssemblyHud.Layout(
      text,
      1880,
      Measure
    );

    Assert.Equal([text], layout.Lines);
    Assert.Equal(Measure(text), layout.Width);
    Assert.True(layout.Width > 640);
    Assert.Equal(HandAssemblyHud.LineHeight, layout.Height);
  }

  // Fails when the height stays one line though the text wrapped.
  [Fact]
  public void Layout_NarrowScreenWrapsWholeWords() {
    string text = Longest("en", "Cross", "Bolt bundle");

    HandAssemblyHud.HudLayout layout = HandAssemblyHud.Layout(
      text,
      480,
      Measure
    );

    Assert.True(layout.Lines.Count > 1);
    Assert.Equal(
      layout.Lines.Count * HandAssemblyHud.LineHeight,
      layout.Height
    );
    Assert.All(layout.Lines, l => Assert.True(Measure(l) <= 480));
    Assert.Equal(layout.Lines.Max(Measure), layout.Width);
    Assert.Equal(text, string.Join(" ", layout.Lines));
  }

  // Fails when a line is broken at the character that overflows rather than between words.
  [Theory]
  [InlineData(1280)]
  [InlineData(1920)]
  [InlineData(300)]
  public void Layout_NoLineCutsAWord(double screen) {
    foreach (
      (string locale, string part, string fastener) in Locales()
        .Select(o => ((string)o[0], (string)o[1], (string)o[2]))
    ) {
      string text = Longest(locale, part, fastener);
      var words = text.Split(' ').ToHashSet();

      HandAssemblyHud.HudLayout layout = HandAssemblyHud.Layout(
        text,
        screen - 40,
        Measure
      );

      Assert.All(
        layout.Lines.SelectMany(l => l.Split(' ')),
        w => Assert.Contains(w, words)
      );
      Assert.Equal(text, string.Join(" ", layout.Lines));
    }
  }
}
