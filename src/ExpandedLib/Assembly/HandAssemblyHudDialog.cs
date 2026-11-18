using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace ExpandedLib.Assembling;

/// <summary>The text hand assembly shows near the bottom of the screen: one line where it fits the screen, else whole lines growing upward from the one line's place.</summary>
internal sealed class HandAssemblyHudDialog : HudElement {
  private const string TextKey = "line";
  private const double Gap = 8;

  /// <summary>Lines of held-item help the line clears: hand assembly's two and one the game may add.</summary>
  internal const int HeldHelpLines = 3;

  /// <summary>Height of the held-item help's bottom above the screen's bottom, as <c>HudHotbar.OnRenderGUI</c> places its composer.</summary>
  internal const double HelpBottom = 95;

  /// <summary>Height of one line's element in <c>DrawWorldInteractionUtil.ComposeBlockWorldInteractionHelp</c>.</summary>
  internal const double HelpLineHeight = 80;

  /// <summary>Step between help lines: the hotbar's <c>DrawWorldInteractionUtil.UnscaledLineHeight</c> of 25 plus the 8 <c>ComposeBlockWorldInteractionHelp</c> adds.</summary>
  internal const double HelpLineStep = 33;

  private const double ScreenMargin = 40;
  private const double WidthSlack = 8;

  private readonly CairoFont _font = CairoFont.WhiteSmallText();
  private string _text = "";
  private double _maxWidth;

  public HandAssemblyHudDialog(ICoreClientAPI capi)
    : base(capi) {
    Compose(HandAssemblyHud.Layout("", 0, MeasureUnscaled));
  }

  private static double Scale => RuntimeEnv.GUIScale;

  private double MeasureUnscaled(string text) =>
    _font.GetTextExtents(text).Width / Scale;

  /// <summary>Width the text may take: the screen's width less its margins, in unscaled GUI px.</summary>
  private double ScreenWidth => (capi.Render.FrameWidth / Scale) - ScreenMargin;

  private void Compose(HandAssemblyHud.HudLayout layout) {
    ElementBounds dialog = ElementStdBounds
      .AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterBottom)
      .WithFixedAlignmentOffset(0, BottomOffset(HeldHelpLines));
    ElementBounds text = ElementBounds.Fixed(
      0,
      0,
      layout.Width + WidthSlack,
      layout.Height
    );
    dialog.BothSizing = ElementSizing.FitToChildren;
    dialog.WithChild(text);
    SingleComposer = capi
      .Gui.CreateCompo("exlib-handassembly-hud", dialog)
      .AddDynamicText(
        string.Join("\n", layout.Lines),
        _font,
        EnumTextOrientation.Center,
        text,
        TextKey
      )
      .Compose();
  }

  /// <summary>The line's vertical offset from the screen's bottom, in unscaled GUI px (negative is up), clearing <paramref name="helpLines"/> lines of held-item help by a gap of 8 px.</summary>
  internal static double BottomOffset(int helpLines) =>
    -(HelpBottom + HelpLineHeight + ((helpLines - 1) * HelpLineStep) + Gap);

  public override bool Focusable => false;

  public override bool ShouldReceiveKeyboardEvents() => false;

  /// <summary>Shows <paramref name="line"/>, sized to it and the screen's width and recomposed when either changed, opening the element when it is closed.</summary>
  public void Show(string line) {
    double maxWidth = ScreenWidth;
    if (line != _text || maxWidth != _maxWidth) {
      _text = line;
      _maxWidth = maxWidth;
      Compose(HandAssemblyHud.Layout(line, maxWidth, MeasureUnscaled));
    }
    if (!IsOpened())
      TryOpen();
  }

  /// <summary>Closes the element when it is open.</summary>
  public void Hide() {
    if (IsOpened())
      TryClose();
  }
}
