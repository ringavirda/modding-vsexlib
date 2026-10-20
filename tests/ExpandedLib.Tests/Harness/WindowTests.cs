using System;
using System.IO;
using System.Linq;
using ExpandedLib.Testing;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Xunit;
using Context = Cairo.Context;
using Format = Cairo.Format;
using ImageSurface = Cairo.ImageSurface;

namespace ExpandedLib.Tests;

/// <summary><see cref="WindowRig"/> and <see cref="WindowLayout"/> over a dialog of a static text, a
/// dynamic text, a button and a 2 x 2 slot grid, composed with the engine's own element code.</summary>
public class WindowTests {
  private const double DialogWidth = 320;
  private const double DialogHeight = 250;

  private static string OutDir =>
    Path.Combine(AppContext.BaseDirectory, "window-renders");

  private static GuiComposer Fixture(WindowRig rig) {
    ICoreClientAPI capi = rig.Api;
    var inventory = new DummyInventory(capi, 4);
    inventory[0].Itemstack = new ItemStack(
      new Item { Code = new AssetLocation("game:ingot-iron") },
      3
    );
    ElementBounds dialog = ElementBounds.Fixed(
      100,
      100,
      DialogWidth,
      DialogHeight
    );
    return capi
      .Gui.CreateCompo("window-fixture", dialog)
      .AddStaticText(
        "Bench",
        CairoFont.WhiteSmallishText(),
        ElementBounds.Fixed(10, 10, 200, 30),
        "title"
      )
      .AddDynamicText(
        "Lay the brick",
        CairoFont.WhiteSmallText(),
        ElementBounds.Fixed(10, 50, 300, 30),
        "status"
      )
      .AddSmallButton(
        "Craft",
        () => true,
        ElementBounds.Fixed(10, 90, 100, 30),
        EnumButtonStyle.Normal,
        "craft"
      )
      .AddItemSlotGrid(
        inventory,
        _ => { },
        2,
        ElementStdBounds.SlotGrid(EnumDialogArea.None, 10, 130, 2, 2),
        "slots"
      )
      .Compose();
  }

  private static GuiComposer WithText(
    WindowRig rig,
    string text,
    double width,
    double height
  ) =>
    rig
      .Api.Gui.CreateCompo(
        "window-text",
        ElementBounds.Fixed(100, 100, 320, 120)
      )
      .AddDynamicText(
        text,
        CairoFont.WhiteSmallText(),
        ElementBounds.Fixed(10, 10, width, height),
        "label"
      )
      .Compose();

  private sealed class DrawingDialog : GuiDialog {
    public DrawingDialog(ICoreClientAPI capi)
      : base(capi) {
      SingleComposer = capi
        .Gui.CreateCompo(
          "window-dialog",
          ElementBounds.Fixed(100, 100, 320, 120)
        )
        .AddStaticText(
          "x",
          CairoFont.WhiteSmallText(),
          ElementBounds.Fixed(10, 10, 50, 30)
        )
        .Compose();
    }

    public override string ToggleKeyCombinationCode => "";

    public override void OnRenderGUI(float deltaTime) {
      base.OnRenderGUI(deltaTime);
      using var surface = new ImageSurface(Format.Argb32, 17, 13);
      using (var ctx = new Context(surface)) {
        ctx.SetSourceRGBA(1, 0, 0, 1);
        ctx.Paint();
      }
      int id = capi.Gui.LoadCairoTexture(surface, false);
      capi.Render.Render2DTexture(id, 200f, 160f, 17f, 13f);
    }
  }

  private static (double Width, double Height) Box(string text, double width) {
    var font = CairoFont.WhiteSmallText();
    var util = new TextDrawUtil();
    return (width, util.GetMultilineTextHeight(font, text, width));
  }

  // Fails when Render2DTexture* draws nothing into the frame, so each element's rectangle stays
  // transparent.
  [Fact]
  public void RenderDrawsInkInsideEveryElement() {
    using var rig = new WindowRig();
    GuiComposer composer = Fixture(rig);
    using WindowFrame frame = rig.Render(composer);

    Assert.Equal((int)DialogWidth, frame.Width);
    Assert.Equal((int)DialogHeight, frame.Height);
    foreach (string key in new[] { "title", "status", "craft", "slots" }) {
      ElementBounds b = composer[key].Bounds;
      int ink = frame.InkIn(
        b.renderX - frame.OriginX,
        b.renderY - frame.OriginY,
        b.OuterWidth,
        b.OuterHeight
      );
      Assert.True(ink > 0, $"{key} drew no ink");
    }
    string path = Path.Combine(OutDir, "fixture.png");
    frame.Save(path);
    Assert.True(new FileInfo(path).Length > 0);
    Console.WriteLine("window fixture PNG: " + path);
  }

  // Fails when an item stack is not drawn, so the slot grid's draws hold no stack.
  [Fact]
  public void RenderDrawsTheItemStackAsALabelledSquare() {
    using var rig = new WindowRig();
    using WindowFrame frame = rig.Render(Fixture(rig));

    WindowDraw stack = Assert.Single(frame.Draws, d => d.Kind == "stack");
    Assert.Contains("ingot-iron", stack.Label);
    Assert.Equal(stack.Width, stack.Height);
    Assert.True(frame.InkIn(stack.X, stack.Y, stack.Width, stack.Height) > 0);
  }

  // Fails when the rig ignores its GUI scale, so the dialog renders at 320 x 250 instead of twice that.
  [Fact]
  public void RenderScalesTheDialogByTheGuiScale() {
    using var rig = new WindowRig(guiScale: 2f);
    using WindowFrame frame = rig.Render(Fixture(rig));

    Assert.Equal((int)(DialogWidth * 2), frame.Width);
    Assert.Equal((int)(DialogHeight * 2), frame.Height);
  }

  // Fails when Dispose leaves the rig's GUI scale in RuntimeEnv.
  [Fact]
  public void DisposeRestoresTheGuiScale() {
    float before = RuntimeEnv.GUIScale;
    RuntimeEnv.GUIScale = 1.5f;
    try {
      new WindowRig(guiScale: 3f).Dispose();
      Assert.Equal(1.5f, RuntimeEnv.GUIScale);
    } finally {
      RuntimeEnv.GUIScale = before;
    }
  }

  // Fails when the margin argument is dropped, so the frame is the dialog's own size.
  [Fact]
  public void RenderKeepsTheMarginAroundTheDialog() {
    using var rig = new WindowRig();
    using WindowFrame frame = rig.Render(Fixture(rig), margin: 20);

    Assert.Equal((int)DialogWidth + 40, frame.Width);
    Assert.Equal((int)DialogHeight + 40, frame.Height);
    Assert.Equal(80, (int)frame.OriginX);
  }

  // Fails when the screen size argument is ignored, so a centred dialog sits at the same x on a
  // wider window.
  [Fact]
  public void RenderCentresTheDialogOnTheScreenSize() {
    double Origin(int screenWidth) {
      using var rig = new WindowRig(
        screenWidth: screenWidth,
        screenHeight: 600
      );
      GuiComposer composer = rig
        .Api.Gui.CreateCompo(
          "window-centred",
          ElementBounds.Fixed(EnumDialogArea.CenterMiddle, 0, 0, 320, 120)
        )
        .AddStaticText(
          "x",
          CairoFont.WhiteSmallText(),
          ElementBounds.Fixed(10, 10, 50, 30)
        )
        .Compose();
      using WindowFrame frame = rig.Render(composer);
      return frame.OriginX;
    }

    Assert.Equal(100, Origin(1000) - Origin(800), 0.5);
  }

  [Fact]
  public void GateFindsNothingOnTheFixtureAsLaid() {
    using var rig = new WindowRig();

    Assert.Empty(WindowLayout.Check(Fixture(rig)));
  }

  // Fails when the height check is off: a two-line text in a one-line box is then not reported.
  [Fact]
  public void GateFindsATextOneLineTooShort() {
    using var rig = new WindowRig();
    string text = "first\nsecond";
    (double width, double height) = Box(text, 200);
    double line = new TextDrawUtil().GetLineHeight(CairoFont.WhiteSmallText());

    Assert.Empty(WindowLayout.Check(WithText(rig, text, width, height)));
    string finding = Assert.Single(
      WindowLayout.Check(WithText(rig, text, width, height - line))
    );
    Assert.StartsWith("height ", finding);
    Assert.Contains("label", finding);
    Assert.Contains("first", finding);
  }

  // Fails when the width check is off: a word wider than its box is then not reported.
  [Fact]
  public void GateFindsAWordWiderThanItsBox() {
    using var rig = new WindowRig();
    GuiComposer composer = WithText(rig, "Work", 20, 60);

    Assert.Contains(
      WindowLayout.Check(composer),
      f => f.StartsWith("width ") && f.Contains("Work")
    );
  }

  // Fails when the overlap check is off: two texts sharing a rectangle are then not reported.
  [Fact]
  public void GateFindsTwoOverlappingSiblings() {
    using var rig = new WindowRig();
    GuiComposer composer = rig
      .Api.Gui.CreateCompo(
        "window-overlap",
        ElementBounds.Fixed(100, 100, 320, 120)
      )
      .AddDynamicText(
        "one",
        CairoFont.WhiteSmallText(),
        ElementBounds.Fixed(10, 10, 100, 30),
        "a"
      )
      .AddDynamicText(
        "two",
        CairoFont.WhiteSmallText(),
        ElementBounds.Fixed(60, 20, 100, 30),
        "b"
      )
      .Compose();

    string finding = Assert.Single(WindowLayout.Check(composer));
    Assert.StartsWith("overlap ", finding);
    Assert.Contains("a \"one\"", finding);
    Assert.Contains("b \"two\"", finding);
  }

  // Fails when one side's clause of the outside check is dropped: each row leaves the dialog on that
  // side only.
  [Theory]
  [InlineData("left", -20, 10)]
  [InlineData("top", 10, -20)]
  [InlineData("right", 300, 10)]
  [InlineData("bottom", 10, 100)]
  public void GateFindsAnElementOutsideTheDialog(
    string side,
    double x,
    double y
  ) {
    using var rig = new WindowRig();
    GuiComposer composer = rig
      .Api.Gui.CreateCompo(
        "window-outside",
        ElementBounds.Fixed(100, 100, 320, 120)
      )
      .AddDynamicText(
        "far",
        CairoFont.WhiteSmallText(),
        ElementBounds.Fixed(x, y, 100, 30),
        "far"
      )
      .Compose();

    var findings = WindowLayout.Check(composer);
    Assert.True(findings.Count == 1, $"{side}: {string.Join("; ", findings)}");
    Assert.StartsWith("outside ", findings[0]);
    Assert.Contains("far", findings[0]);
  }

  // Fails when the overlap test needs only one axis to overlap: boxes side by side then collide.
  [Fact]
  public void GateIgnoresSiblingsThatShareOnlyAnAxis() {
    using var rig = new WindowRig();
    GuiComposer composer = rig
      .Api.Gui.CreateCompo(
        "window-beside",
        ElementBounds.Fixed(100, 100, 320, 120)
      )
      .AddDynamicText(
        "one",
        CairoFont.WhiteSmallText(),
        ElementBounds.Fixed(10, 10, 100, 30),
        "a"
      )
      .AddDynamicText(
        "two",
        CairoFont.WhiteSmallText(),
        ElementBounds.Fixed(120, 10, 100, 30),
        "b"
      )
      .AddDynamicText(
        "three",
        CairoFont.WhiteSmallText(),
        ElementBounds.Fixed(10, 50, 100, 30),
        "c"
      )
      .Compose();

    Assert.Empty(WindowLayout.Check(composer));
  }

  // Fails when the overlap rule compares elements of different parents: a text inside a child panel
  // is then reported against a text of the dialog beside it.
  [Fact]
  public void GateComparesOnlyElementsOfOneParent() {
    using var rig = new WindowRig();
    ElementBounds panel = ElementBounds.Fixed(120, 10, 100, 100);
    GuiComposer composer = rig
      .Api.Gui.CreateCompo(
        "window-nested",
        ElementBounds.Fixed(100, 100, 320, 120)
      )
      .AddDynamicText(
        "one",
        CairoFont.WhiteSmallText(),
        ElementBounds.Fixed(10, 10, 100, 30),
        "a"
      )
      .BeginChildElements(panel)
      .AddDynamicText(
        "two",
        CairoFont.WhiteSmallText(),
        ElementBounds.Fixed(-110, 0, 100, 30),
        "b"
      )
      .EndChildElements()
      .Compose();

    Assert.Empty(WindowLayout.Check(composer));
  }

  // Fails when a hover text is measured as text: its tiny box then reports its long line.
  [Fact]
  public void GateSkipsHoverText() {
    using var rig = new WindowRig();
    GuiComposer composer = rig
      .Api.Gui.CreateCompo(
        "window-hover",
        ElementBounds.Fixed(100, 100, 320, 120)
      )
      .AddHoverText(
        "a long hover line that cannot fit",
        CairoFont.WhiteSmallText(),
        100,
        ElementBounds.Fixed(10, 10, 20, 10),
        "tip"
      )
      .Compose();

    Assert.Empty(WindowLayout.Check(composer));
  }

  // Fails when Render(GuiDialog) calls only the composer's render: the draw the dialog's
  // OnRenderGUI override makes is then missing from the frame.
  [Fact]
  public void RenderRunsTheDialogsOwnRenderOverride() {
    using var rig = new WindowRig();
    var dialog = new DrawingDialog(rig.Api);
    using WindowFrame frame = rig.Render(dialog);

    WindowDraw draw = Assert.Single(
      frame.Draws,
      d => d.Kind == "texture" && d.Width == 17 && d.Height == 13
    );
    Assert.Equal(100, draw.X, 0.5);
    Assert.True(frame.InkIn(draw.X, draw.Y, draw.Width, draw.Height) > 0);
  }

  // Fails when Assets.Get or BitmapCreateFromPng is not wired: the shaded dialog background cannot load its
  // texture and the compose throws.
  [Fact]
  public void AShadedDialogBackgroundComposesAndDrawsInk() {
    using var rig = new WindowRig();
    ElementBounds dialog = ElementBounds.Fixed(100, 100, 320, 120);
    ElementBounds bg = ElementBounds.Fill.WithFixedPadding(
      GuiStyle.ElementToDialogPadding
    );
    bg.BothSizing = ElementSizing.FitToChildren;
    GuiComposer composer = rig
      .Api.Gui.CreateCompo("window-bg", dialog)
      .AddShadedDialogBG(bg, true)
      .BeginChildElements(bg)
      .AddStaticText(
        "x",
        CairoFont.WhiteSmallText(),
        ElementBounds.Fixed(0, 0, 80, 30)
      )
      .EndChildElements()
      .Compose();
    using WindowFrame frame = rig.Render(composer);

    Assert.True(frame.InkIn(0, 0, frame.Width, frame.Height) > 0);
  }

  // Fails when the title bar is compared with the dialog's content: its full-width bar overlaps its own title.
  [Fact]
  public void GateLeavesTheTitleBarAlone() {
    using var rig = new WindowRig();
    ElementBounds bg = ElementBounds.Fill.WithFixedPadding(
      GuiStyle.ElementToDialogPadding
    );
    bg.BothSizing = ElementSizing.FitToChildren;
    GuiComposer composer = rig
      .Api.Gui.CreateCompo(
        "window-titlebar",
        ElementStdBounds.AutosizedMainDialog.WithAlignment(
          EnumDialogArea.CenterMiddle
        )
      )
      .AddShadedDialogBG(bg, true)
      .AddDialogTitleBar("Bench", () => { })
      .BeginChildElements(bg)
      .AddStaticText(
        "x",
        CairoFont.WhiteSmallText(),
        ElementBounds.Fixed(0, GuiStyle.TitleBarHeight + 6, 80, 30)
      )
      .EndChildElements()
      .Compose();

    Assert.Empty(WindowLayout.Check(composer));
  }

  // Fails when PushScissor is not wired: the text beyond the 30 px clip then leaves ink to its right.
  [Fact]
  public void ClippedTextLeavesNoInkOutsideTheClip() {
    using var rig = new WindowRig();
    GuiComposer composer = rig
      .Api.Gui.CreateCompo(
        "window-clip",
        ElementBounds.Fixed(100, 100, 320, 120)
      )
      .BeginClip(ElementBounds.Fixed(10, 10, 30, 30))
      .AddDynamicText(
        "WWWWWWWWWWWWWWWWWWWW",
        CairoFont.WhiteSmallText(),
        ElementBounds.Fixed(10, 10, 300, 30),
        "wide"
      )
      .EndClip()
      .Compose();
    using WindowFrame frame = rig.Render(composer);

    Assert.True(frame.InkIn(10, 10, 30, 30) > 0);
    Assert.Equal(0, frame.InkIn(40, 0, 280, 120));
  }

  // Fails when the width rule keeps a tolerance: a word exactly as wide as its box, which the engine
  // breaks, is then not reported.
  [Fact]
  public void GateFindsAWordExactlyAsWideAsItsBox() {
    using var rig = new WindowRig();
    double word = CairoFont.WhiteSmallText().GetTextExtents("Work").Width;

    Assert.Contains(
      WindowLayout.Check(WithText(rig, "Work", word, 60)),
      f => f.StartsWith("width ") && f.Contains("Work")
    );
    Assert.DoesNotContain(
      WindowLayout.Check(WithText(rig, "Work", word + 1, 60)),
      f => f.StartsWith("width ")
    );
  }

  // Fails when the overlap rule compares only text, buttons and slot grids: a passive slot laid over a
  // button is then not reported.
  [Fact]
  public void GateFindsAPassiveSlotOverAButton() {
    using var rig = new WindowRig();
    var inventory = new DummyInventory(rig.Api, 1);
    GuiComposer composer = rig
      .Api.Gui.CreateCompo(
        "window-passive",
        ElementBounds.Fixed(100, 100, 320, 120)
      )
      .AddSmallButton(
        "Craft",
        () => true,
        ElementBounds.Fixed(10, 10, 100, 30),
        EnumButtonStyle.Normal,
        "craft"
      )
      .AddPassiveItemSlot(
        ElementBounds.Fixed(50, 15, 48, 48),
        inventory,
        inventory[0],
        false,
        "preview"
      )
      .Compose();

    string finding = Assert.Single(WindowLayout.Check(composer));
    Assert.StartsWith("overlap ", finding);
    Assert.Contains("preview", finding);
    Assert.Contains("craft", finding);
  }

  // Fails when LoadLang does not register its service: the label then reads its key.
  [Fact]
  public void LoadLangRendersTranslationsAndDisposeRestoresThem() {
    string path = Path.Combine(OutDir, "lang-test-en.json");
    Directory.CreateDirectory(OutDir);
    File.WriteAllText(path, "{ \"greeting\": \"Hello {0}\" }");
    TestLang.Init();
    string label;
    using (var rig = new WindowRig()) {
      rig.LoadLang("wintest", path);
      GuiComposer composer = rig
        .Api.Gui.CreateCompo(
          "window-lang",
          ElementBounds.Fixed(100, 100, 320, 120)
        )
        .AddDynamicText(
          Lang.Get("wintest:greeting", "bench"),
          CairoFont.WhiteSmallText(),
          ElementBounds.Fixed(10, 10, 200, 30),
          "label"
        )
        .Compose();
      label = ((GuiElementTextBase)composer["label"]).Text;
    }

    Assert.Equal("Hello bench", label);
    Assert.Equal("wintest:greeting", Lang.Get("wintest:greeting"));
  }

  // Fails when a key with no exact entry skips the wildcard entries: the colored name then reads its key.
  [Fact]
  public void LoadLangResolvesTheLongestWildcardEntry() {
    string path = Path.Combine(OutDir, "lang-wild-en.json");
    Directory.CreateDirectory(OutDir);
    File.WriteAllText(
      path,
      "{ \"block-pipe-*\": \"Pipe\", \"block-pipe-tee-*\": \"Tee\" }"
    );
    TestLang.Init();
    string plain;
    string tee;
    using (var rig = new WindowRig()) {
      rig.LoadLang("wintest", path);
      plain = Lang.GetMatching("wintest:block-pipe-bend-red");
      tee = Lang.GetMatching("wintest:block-pipe-tee-red");
    }

    Assert.Equal("Pipe", plain);
    Assert.Equal("Tee", tee);
  }
}
