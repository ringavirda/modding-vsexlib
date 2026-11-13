using Vintagestory.API.Client;

namespace ExpandedLib.Assembling;

/// <summary>The one line of text hand assembly shows near the bottom of the screen.</summary>
internal sealed class HandAssemblyHudDialog : HudElement {
  private const string TextKey = "line";
  private const double Width = 640;

  public HandAssemblyHudDialog(ICoreClientAPI capi)
    : base(capi) {
    ElementBounds dialog = ElementStdBounds
      .AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterBottom)
      .WithFixedAlignmentOffset(0, -140);
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
