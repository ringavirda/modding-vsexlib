using Vintagestory.API.Client;

namespace ExpandedLib.Assembling;

/// <summary>The one line of text hand assembly shows near the bottom of the screen.</summary>
internal sealed class HandAssemblyHudDialog : HudElement {
  private const string TextKey = "line";
  private const double Width = 640;
  private const double Gap = 8;

  /// <summary>Lines of held-item help the line clears: hand assembly's two and one the game may add.</summary>
  internal const int HeldHelpLines = 3;

  /// <summary>Height of the held-item help's bottom above the screen's bottom, as <c>HudHotbar.OnRenderGUI</c> places its composer.</summary>
  internal const double HelpBottom = 95;

  /// <summary>Height of one line's element in <c>DrawWorldInteractionUtil.ComposeBlockWorldInteractionHelp</c>.</summary>
  internal const double HelpLineHeight = 80;

  /// <summary>Step between help lines: the hotbar's <c>DrawWorldInteractionUtil.UnscaledLineHeight</c> of 25 plus the 8 <c>ComposeBlockWorldInteractionHelp</c> adds.</summary>
  internal const double HelpLineStep = 33;

  public HandAssemblyHudDialog(ICoreClientAPI capi)
    : base(capi) {
    ElementBounds dialog = ElementStdBounds
      .AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterBottom)
      .WithFixedAlignmentOffset(0, BottomOffset(HeldHelpLines));
    ElementBounds text = ElementBounds.Fixed(0, 0, Width, 24);
    dialog.BothSizing = ElementSizing.FitToChildren;
    dialog.WithChild(text);
    SingleComposer = capi
      .Gui.CreateCompo("exlib-handassembly-hud", dialog)
      .AddDynamicText(
        "",
        CairoFont.WhiteSmallText(),
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

  /// <summary>Shows <paramref name="line"/>, opening the element when it is closed.</summary>
  public void Show(string line) {
    SingleComposer.GetDynamicText(TextKey).SetNewText(line);
    if (!IsOpened())
      TryOpen();
  }

  /// <summary>Closes the element when it is open.</summary>
  public void Hide() {
    if (IsOpened())
      TryClose();
  }
}
