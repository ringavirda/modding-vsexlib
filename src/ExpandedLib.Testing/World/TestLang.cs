using NSubstitute;
using Vintagestory.API.Config;

namespace ExpandedLib.Testing;

/// <summary>
/// Stands up a minimal headless <see cref="Lang"/>, letting production code call
/// <see cref="Lang.Get"/> without the game's asset pipeline. The registered service echoes the key
/// back, the fallback the real service uses for an untranslated key.
/// </summary>
public static class TestLang {
  private static bool _ready;

  /// <summary>
  /// The registered substitute, letting a test restub
  /// <see cref="ITranslationService.HasTranslation(string, bool)"/> for one specific key to
  /// <c>false</c>.
  /// </summary>
  public static ITranslationService Service { get; private set; } = null!;

  /// <summary>Idempotently registers an echo-the-key translation service for the "en" locale.</summary>
  public static void Init() {
    if (_ready)
      return;
    _ready = true;

    var svc = Substitute.For<ITranslationService>();
    svc.LanguageCode.Returns("en");
    svc.Get(Arg.Any<string>(), Arg.Any<object[]>())
      .Returns(ci => ci.Arg<string>());
    svc.GetIfExists(Arg.Any<string>(), Arg.Any<object[]>())
      .Returns(ci => ci.Arg<string>());
    svc.GetUnformatted(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
    svc.GetMatching(Arg.Any<string>(), Arg.Any<object[]>())
      .Returns(ci => ci.Arg<string>());
    svc.GetMatchingIfExists(Arg.Any<string>(), Arg.Any<object[]>())
      .Returns(ci => ci.Arg<string>());
    svc.HasTranslation(Arg.Any<string>(), Arg.Any<bool>()).Returns(true);
    svc.HasTranslation(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
      .Returns(true);

    Lang.DefaultLocale = "en";
    // AvailableLanguages is a read-only auto-property; mutate the dictionary in place, not the field.
    Lang.AvailableLanguages["en"] = svc;
    // ChangeLanguage is the engine's path to set the getter-only CurrentLocale.
    Lang.ChangeLanguage("en");
    Service = svc;
  }
}
