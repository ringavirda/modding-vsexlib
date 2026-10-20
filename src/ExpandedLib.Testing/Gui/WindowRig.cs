using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Cairo;
using HarmonyLib;
using NSubstitute;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

/// <summary>
/// A client API with no window and no GL, on which a dialog composes its <see cref="GuiComposer"/> and
/// renders one frame to pixels.
/// </summary>
/// <remarks>The engine's own element code runs: layout, Cairo drawing and the text measure are the
/// game's, with the game's fonts from the provisioned install and Arial (or Liberation Sans) for
/// <c>sans-serif</c>. What is faked is the texture store
/// (<c>Gui.LoadCairoTexture</c> and <c>LoadOrUpdateCairoTexture</c> keep each surface's pixels under
/// an id) and the draw calls (<c>Render.Render2DTexture*</c> paint that surface into the frame at the
/// rectangle given). An item stack is a plain square with its code's path and, where the draw call
/// asks for a stack size, its count. Shaders, item meshes, sounds, input and the cursor are not
/// simulated. <see cref="Api"/> is built for the composer's dialog
/// only: it has no world, no player and no assets. <see cref="Cairo.Context"/> work needs the system
/// Cairo and fontconfig.
/// <para>The rig sets the process-wide <c>RuntimeEnv.GUIScale</c> for its lifetime and puts the old
/// value back on <see cref="Dispose"/>, so tests that use it run in one collection.</para></remarks>
public sealed class WindowRig : IDisposable {
  private readonly float _previousScale;
  private bool _disposed;
  private readonly Dictionary<int, ImageSurface> _textures = new();
  private readonly List<WindowDraw> _draws = new();
  private int _nextTexture;
  private readonly Dictionary<string, string> _lang = new();
  private ITranslationService? _langService;
  private ITranslationService? _previousService;
  private string? _previousLocale;
  private Context? _frame;
  private int _clipDepth;
  private double _originX;
  private double _originY;

  /// <summary>Makes a rig for one GUI scale and screen size.</summary>
  /// <param name="guiScale">The game's GUI scale setting, above zero; 1.0 is the default.</param>
  /// <param name="screenWidth">Window width in pixels, above zero.</param>
  /// <param name="screenHeight">Window height in pixels, above zero.</param>
  /// <exception cref="ArgumentOutOfRangeException">A size or the scale is zero or less.</exception>
  /// <exception cref="InvalidOperationException">No game install with a fonts folder is
  /// provisioned.</exception>
  public WindowRig(
    float guiScale = 1f,
    int screenWidth = 1920,
    int screenHeight = 1080
  ) {
    ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(guiScale, 0f);
    ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(screenWidth, 0);
    ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(screenHeight, 0);
    CairoNative.Ensure();
    GuiScale = guiScale;
    ScreenWidth = screenWidth;
    ScreenHeight = screenHeight;
    _previousScale = RuntimeEnv.GUIScale;
    RuntimeEnv.GUIScale = guiScale;
    PatchTextures();
    Api = BuildApi();
  }

  /// <summary>The GUI scale the rig runs at.</summary>
  public float GuiScale { get; }

  /// <summary>The window width in pixels.</summary>
  public int ScreenWidth { get; }

  /// <summary>The window height in pixels.</summary>
  public int ScreenHeight { get; }

  /// <summary>The client API a dialog builds its composer against. <c>Gui.CreateCompo</c> makes real
  /// composers parented to the screen.</summary>
  public ICoreClientAPI Api { get; }

  /// <summary>Composes <paramref name="composer"/> if it is not composed and draws one frame of it,
  /// as the composer's render does in the game.</summary>
  /// <param name="composer">A composer made by <see cref="Api"/>.</param>
  /// <param name="margin">Pixels of frame kept around the dialog's rectangle on every side, so
  /// content that overflows it shows; 0 for the dialog's own size.</param>
  /// <returns>The frame; the caller disposes it.</returns>
  public WindowFrame Render(GuiComposer composer, int margin = 0) =>
    Draw(composer, margin, () => composer.Render(0.016f));

  /// <summary>Composes the dialog's <see cref="GuiDialog.SingleComposer"/> if it is not composed and
  /// draws one frame of the dialog, running its <see cref="GuiDialog.OnRenderGUI"/> so draws the
  /// override makes beyond the composer's own (shadows, labels) land in the frame.</summary>
  /// <param name="dialog">A dialog built on <see cref="Api"/> whose <c>SingleComposer</c> is set.</param>
  /// <param name="margin">Pixels of frame kept around the dialog's rectangle on every side; 0 for the
  /// dialog's own size.</param>
  /// <returns>The frame; the caller disposes it.</returns>
  public WindowFrame Render(GuiDialog dialog, int margin = 0) =>
    Draw(dialog.SingleComposer, margin, () => dialog.OnRenderGUI(0.016f));

  /// <summary>Registers the translations of one mod's language file for <c>Lang.Get</c> until
  /// <see cref="Dispose"/>, so labels measure and render as in game instead of echoing their keys.</summary>
  /// <remarks>Replaces the "en" translation service and makes "en" the current locale; both are put
  /// back on <see cref="Dispose"/>. A key with no exact entry takes the longest wildcard entry (<c>block-x-*</c>) it starts with; a key found in no loaded file still echoes itself. Values are
  /// formatted with <see cref="string.Format(string, object[])"/>. Not thread-safe: tests that use it
  /// share the rig's collection.</remarks>
  /// <param name="domain">The mod domain the file's keys belong to, such as <c>iiex</c>.</param>
  /// <param name="path">A flat JSON file of key to text, such as <c>assets/iiex/lang/en.json</c>.</param>
  /// <exception cref="FileNotFoundException"><paramref name="path"/> does not exist.</exception>
  public void LoadLang(string domain, string path) {
    using FileStream stream = File.OpenRead(path);
    using JsonDocument doc = JsonDocument.Parse(
      stream,
      new JsonDocumentOptions {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
      }
    );
    foreach (JsonProperty p in doc.RootElement.EnumerateObject())
      _lang[domain + ":" + p.Name] = p.Value.GetString() ?? "";
    if (_langService != null)
      return;
    _langService = BuildLangService();
    Lang.AvailableLanguages.TryGetValue("en", out _previousService);
    _previousLocale = Lang.CurrentLocale;
    Lang.AvailableLanguages["en"] = _langService;
    Lang.ChangeLanguage("en");
  }

  private WindowFrame Draw(GuiComposer composer, int margin, Action render) {
    composer.Compose(false);
    ElementBounds b = composer.Bounds;
    int width = (int)Math.Ceiling(b.OuterWidth) + 2 * margin;
    int height = (int)Math.Ceiling(b.OuterHeight) + 2 * margin;
    _originX = b.renderX - margin;
    _originY = b.renderY - margin;
    _draws.Clear();
    _clipDepth = 0;
    var surface = new ImageSurface(Format.Argb32, width, height);
    _frame = new Context(surface);
    try {
      render();
    } finally {
      _frame.Dispose();
      _frame = null;
    }
    return new WindowFrame(surface, _originX, _originY, [.. _draws]);
  }

  public void Dispose() {
    RuntimeEnv.GUIScale = _previousScale;
    if (!_disposed)
      UnpatchTextures();
    _disposed = true;
    if (_langService != null) {
      if (_previousService != null)
        Lang.AvailableLanguages["en"] = _previousService;
      else
        Lang.AvailableLanguages.Remove("en");
      if (_previousLocale != null)
        Lang.ChangeLanguage(_previousLocale);
      _langService = null;
    }
    foreach (ImageSurface s in _textures.Values)
      s.Dispose();
    _textures.Clear();
  }

  private ICoreClientAPI BuildApi() {
    var api = Substitute.For<ICoreClientAPI>();
    var screen = new ScreenBounds(ScreenWidth, ScreenHeight);

    var gui = Substitute.For<IGuiAPI>();
    gui.WindowBounds.Returns(screen);
    gui.Icons.Returns(new IconUtil(api));
    gui.TextTexture.Returns(new TextTextureUtil(api));
    gui.Text.Returns(new TextDrawUtil());
    gui.CreateCompo(Arg.Any<string>(), Arg.Any<ElementBounds>())
      .Returns(ci =>
        MakeComposer(api, screen, ci.Arg<string>(), ci.Arg<ElementBounds>())
      );
    gui.LoadCairoTexture(Arg.Any<ImageSurface>(), Arg.Any<bool>())
      .Returns(ci => Store(0, ci.Arg<ImageSurface>()));
    gui.When(g => {
      ImageSurface any = Arg.Any<ImageSurface>();
      bool linear = Arg.Any<bool>();
      LoadedTexture into = Arg.Any<LoadedTexture>();
      g.LoadOrUpdateCairoTexture(any, linear, ref into);
    })
      .Do(ci => {
        var into = (LoadedTexture)ci[2];
        ImageSurface surface = ci.Arg<ImageSurface>();
        into.TextureId = Store(into.TextureId, surface);
        into.Width = surface.Width;
        into.Height = surface.Height;
      });
    api.Gui.Returns(gui);

    var render = Substitute.For<IRenderAPI>();
    render.FrameWidth.Returns(ScreenWidth);
    render.FrameHeight.Returns(ScreenHeight);
    WireDraws(render);
    api.Render.Returns(render);

    var input = Substitute.For<IInputAPI>();
    input.MouseX.Returns(-1);
    input.MouseY.Returns(-1);
    input.KeyboardKeyState.Returns(new bool[512]);
    api.Input.Returns(input);

    var world = Substitute.For<IClientWorldAccessor>();
    world.Rand.Returns(new Random(1));
    api.World.Returns(world);
    return api;
  }

  private static readonly object PatchGate = new();
  private static int _patched;
  private static readonly Harmony Patcher = new(
    "expandedlib.testing.windowrig"
  );

  /// <summary>Stands in for the engine's PNG-backed surface loader, whose decoder needs a native
  /// library the rig does not load.</summary>
  private static bool FlatTile(int mulAlpha, ref ImageSurface __result) {
    __result = new ImageSurface(Format.Argb32, 16, 16);
    using var ctx = new Context(__result);
    ctx.SetSourceRGBA(0.2, 0.15, 0.12, mulAlpha / 255.0);
    ctx.Paint();
    return false;
  }

  private static void PatchTextures() {
    lock (PatchGate)
      if (_patched++ == 0)
        Patcher.Patch(
          typeof(GuiElement).GetMethod(
            "getImageSurfaceFromAsset",
            BindingFlags.Static | BindingFlags.Public,
            null,
            [typeof(ICoreClientAPI), typeof(AssetLocation), typeof(int)],
            null
          )!,
          prefix: new HarmonyMethod(
            typeof(WindowRig).GetMethod(
              nameof(FlatTile),
              BindingFlags.Static | BindingFlags.NonPublic
            )
          )
        );
  }

  private static void UnpatchTextures() {
    lock (PatchGate)
      if (--_patched == 0)
        Patcher.UnpatchAll(Patcher.Id);
  }

  private string? Lookup(string key) {
    if (_lang.TryGetValue(key, out string? text))
      return text;
    string? best = null;
    foreach (string entry in _lang.Keys)
      if (
        entry.EndsWith('*')
        && key.StartsWith(
          entry.AsSpan(0, entry.Length - 1),
          StringComparison.Ordinal
        )
        && (best == null || entry.Length > best.Length)
      )
        best = entry;
    return best == null ? null : _lang[best];
  }

  private ITranslationService BuildLangService() {
    var svc = Substitute.For<ITranslationService>();
    svc.LanguageCode.Returns("en");
    string Translate(string key, object[] args) =>
      Lookup(key) is { } text
        ? (args.Length == 0 ? text : string.Format(text, args))
        : key;
    svc.Get(Arg.Any<string>(), Arg.Any<object[]>())
      .Returns(ci => Translate(ci.Arg<string>(), ci.Arg<object[]>()));
    svc.GetIfExists(Arg.Any<string>(), Arg.Any<object[]>())
      .Returns(ci => Translate(ci.Arg<string>(), ci.Arg<object[]>()));
    svc.GetUnformatted(Arg.Any<string>())
      .Returns(ci => Translate(ci.Arg<string>(), []));
    svc.GetMatching(Arg.Any<string>(), Arg.Any<object[]>())
      .Returns(ci => Translate(ci.Arg<string>(), ci.Arg<object[]>()));
    svc.GetMatchingIfExists(Arg.Any<string>(), Arg.Any<object[]>())
      .Returns(ci => Translate(ci.Arg<string>(), ci.Arg<object[]>()));
    svc.HasTranslation(Arg.Any<string>(), Arg.Any<bool>()).Returns(true);
    svc.HasTranslation(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
      .Returns(true);
    return svc;
  }

  private static GuiComposer MakeComposer(
    ICoreClientAPI api,
    ElementBounds screen,
    string name,
    ElementBounds bounds
  ) {
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic;
    ConstructorInfo ctor =
      typeof(GuiComposer).GetConstructor(
        Any,
        null,
        [typeof(ICoreClientAPI), typeof(ElementBounds), typeof(string)],
        null
      )
      ?? throw new MissingMethodException(
        "GuiComposer(ICoreClientAPI, ElementBounds, string)"
      );
    var composer = (GuiComposer)ctor.Invoke([api, bounds, name]);
    bounds.ParentBounds ??= screen;
    typeof(GuiComposer)
      .GetField("composerManager", Any)
      ?.SetValue(composer, Substitute.For<IGuiComposerManager>());
    return composer;
  }

  private int Store(int id, ImageSurface source) {
    if (id == 0 || !_textures.ContainsKey(id))
      id = ++_nextTexture;
    else
      _textures[id].Dispose();
    var copy = new ImageSurface(Format.Argb32, source.Width, source.Height);
    using (var ctx = new Context(copy)) {
      ctx.Operator = Operator.Source;
      ctx.SetSourceSurface(source, 0, 0);
      ctx.Paint();
    }
    _textures[id] = copy;
    return id;
  }

  private void WireDraws(IRenderAPI render) {
    render
      .When(r => r.PushScissor(Arg.Any<ElementBounds>(), Arg.Any<bool>()))
      .Do(ci => PushScissor(ci.Arg<ElementBounds>()));
    render.When(r => r.PopScissor()).Do(_ => PopScissor());
    render
      .When(r =>
        r.Render2DTexture(
          Arg.Any<int>(),
          Arg.Any<float>(),
          Arg.Any<float>(),
          Arg.Any<float>(),
          Arg.Any<float>(),
          Arg.Any<float>(),
          Arg.Any<Vec4f>()
        )
      )
      .Do(ci =>
        DrawTexture(
          ci.ArgAt<int>(0),
          ci.ArgAt<float>(1),
          ci.ArgAt<float>(2),
          ci.ArgAt<float>(3),
          ci.ArgAt<float>(4)
        )
      );
    render
      .When(r =>
        r.Render2DTexturePremultipliedAlpha(
          Arg.Any<int>(),
          Arg.Any<float>(),
          Arg.Any<float>(),
          Arg.Any<float>(),
          Arg.Any<float>(),
          Arg.Any<float>(),
          Arg.Any<Vec4f>()
        )
      )
      .Do(ci =>
        DrawTexture(
          ci.ArgAt<int>(0),
          ci.ArgAt<float>(1),
          ci.ArgAt<float>(2),
          ci.ArgAt<float>(3),
          ci.ArgAt<float>(4)
        )
      );
    render
      .When(r =>
        r.Render2DTexturePremultipliedAlpha(
          Arg.Any<int>(),
          Arg.Any<double>(),
          Arg.Any<double>(),
          Arg.Any<double>(),
          Arg.Any<double>(),
          Arg.Any<float>(),
          Arg.Any<Vec4f>()
        )
      )
      .Do(ci =>
        DrawTexture(
          ci.ArgAt<int>(0),
          ci.ArgAt<double>(1),
          ci.ArgAt<double>(2),
          ci.ArgAt<double>(3),
          ci.ArgAt<double>(4)
        )
      );
    render
      .When(r =>
        r.RenderTexture(
          Arg.Any<int>(),
          Arg.Any<double>(),
          Arg.Any<double>(),
          Arg.Any<double>(),
          Arg.Any<double>(),
          Arg.Any<float>(),
          Arg.Any<Vec4f>()
        )
      )
      .Do(ci =>
        DrawTexture(
          ci.ArgAt<int>(0),
          ci.ArgAt<double>(1),
          ci.ArgAt<double>(2),
          ci.ArgAt<double>(3),
          ci.ArgAt<double>(4)
        )
      );
    render
      .When(r =>
        r.Render2DTexturePremultipliedAlpha(
          Arg.Any<int>(),
          Arg.Any<ElementBounds>(),
          Arg.Any<float>(),
          Arg.Any<Vec4f>()
        )
      )
      .Do(ci => DrawTexture(ci.ArgAt<int>(0), ci.ArgAt<ElementBounds>(1)));
    render
      .When(r =>
        r.Render2DTexture(
          Arg.Any<int>(),
          Arg.Any<ElementBounds>(),
          Arg.Any<float>(),
          Arg.Any<Vec4f>()
        )
      )
      .Do(ci => DrawTexture(ci.ArgAt<int>(0), ci.ArgAt<ElementBounds>(1)));
    render
      .When(r =>
        r.RenderItemstackToGui(
          Arg.Any<ItemSlot>(),
          Arg.Any<double>(),
          Arg.Any<double>(),
          Arg.Any<double>(),
          Arg.Any<float>(),
          Arg.Any<int>(),
          Arg.Any<bool>(),
          Arg.Any<bool>(),
          Arg.Any<bool>()
        )
      )
      .Do(ci =>
        DrawStack(
          ci.Arg<ItemSlot>(),
          ci.ArgAt<double>(1),
          ci.ArgAt<double>(2),
          ci.ArgAt<float>(4),
          ci.ArgAt<bool>(8)
        )
      );
    render
      .When(r =>
        r.RenderItemstackToGui(
          Arg.Any<ItemSlot>(),
          Arg.Any<double>(),
          Arg.Any<double>(),
          Arg.Any<double>(),
          Arg.Any<float>(),
          Arg.Any<int>(),
          Arg.Any<float>(),
          Arg.Any<bool>(),
          Arg.Any<bool>(),
          Arg.Any<bool>()
        )
      )
      .Do(ci =>
        DrawStack(
          ci.Arg<ItemSlot>(),
          ci.ArgAt<double>(1),
          ci.ArgAt<double>(2),
          ci.ArgAt<float>(4),
          false
        )
      );
  }

  private void PushScissor(ElementBounds b) {
    if (_frame == null)
      return;
    _frame.Save();
    _clipDepth++;
    _frame.Rectangle(
      b.renderX - _originX,
      b.renderY - _originY,
      b.OuterWidth,
      b.OuterHeight
    );
    _frame.Clip();
  }

  private void PopScissor() {
    if (_frame == null || _clipDepth == 0)
      return;
    _clipDepth--;
    _frame.Restore();
  }

  private void DrawTexture(int id, ElementBounds b) =>
    DrawTexture(id, b.renderX, b.renderY, b.OuterWidth, b.OuterHeight);

  private void DrawTexture(int id, double x, double y, double w, double h) {
    if (_frame == null || !_textures.TryGetValue(id, out ImageSurface? tex))
      return;
    if (w <= 0 || h <= 0)
      return;
    _draws.Add(new WindowDraw("texture", x - _originX, y - _originY, w, h, ""));
    _frame.Save();
    _frame.Translate(x - _originX, y - _originY);
    _frame.Scale(w / tex.Width, h / tex.Height);
    _frame.SetSourceSurface(tex, 0, 0);
    _frame.Paint();
    _frame.Restore();
  }

  private void DrawStack(
    ItemSlot slot,
    double cx,
    double cy,
    float size,
    bool showCount
  ) {
    ItemStack? stack = slot.Itemstack;
    if (_frame == null || stack == null)
      return;
    double x = cx - size / 2 - _originX;
    double y = cy - size / 2 - _originY;
    string label = stack.Collectible?.Code?.Path ?? "?";
    if (showCount)
      label += " x" + stack.StackSize;
    _draws.Add(new WindowDraw("stack", x, y, size, size, label));
    _frame.Save();
    _frame.Rectangle(x, y, size, size);
    _frame.SetSourceRGBA(0.55, 0.55, 0.62, 1);
    _frame.FillPreserve();
    _frame.SetSourceRGBA(0.9, 0.9, 0.95, 1);
    _frame.LineWidth = 1;
    _frame.Stroke();
    _frame.Rectangle(x, y, size, size);
    _frame.Clip();
    _frame.SelectFontFace("Montserrat", FontSlant.Normal, FontWeight.Normal);
    _frame.SetFontSize(Math.Max(6, size / 4));
    _frame.SetSourceRGBA(0, 0, 0, 1);
    _frame.MoveTo(x + 1, y + size / 2);
    _frame.ShowText(label);
    _frame.Restore();
  }

  private sealed class ScreenBounds : ElementBounds {
    public ScreenBounds(int width, int height) {
      absInnerWidth = width;
      absInnerHeight = height;
      IsWindowBounds = true;
      Initialized = true;
    }

    public override void CalcWorldBounds() { }

    public override double absX => 0;

    public override double absY => 0;

    public override double renderX => 0;

    public override double renderY => 0;

    public override double drawX => 0;

    public override double drawY => 0;

    public override double bgDrawX => 0;

    public override double bgDrawY => 0;
  }
}
