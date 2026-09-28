using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Checks;
using ExpandedLib.Config;
using ExpandedLib.Migrations;
using ExpandedLib.Registries;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Every <c>/exmod</c> reply reaches the caller worded in the caller's own language, with
/// a second locale registered beside the test <c>en</c>. The game's player route re-formats a
/// single-line reply and the console logs it as it stands, so a reply carries finished text.</summary>
[Collection(LangCollection.Name)]
public class CommandLanguageTests : IDisposable {
  private const string Locale = "uk";

  private readonly TestWorld _world = new();

  public CommandLanguageTests() {
    TestLang.Init();
    var svc = Substitute.For<ITranslationService>();
    svc.LanguageCode.Returns(Locale);
    svc.Get(Arg.Any<string>(), Arg.Any<object[]>())
      .Returns(ci => Locale + ":" + ci.Arg<string>());
    svc.HasTranslation(Arg.Any<string>(), Arg.Any<bool>()).Returns(true);
    svc.HasTranslation(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
      .Returns(true);
    Lang.AvailableLanguages[Locale] = svc;
    _world.Api.ChatCommands.Parsers.Returns(
      new CommandArgumentParsers(_world.Api)
    );
  }

  public void Dispose() => Lang.AvailableLanguages.Remove(Locale);

  #region Fakes

  private static string Uk(string key) => Locale + ":" + key;

  // A fluent IChatCommand that answers every builder call with itself and keeps the last handler.
  private static IChatCommand FakeCommand(Action<OnCommandDelegate> onHandler) {
    var cmd = Substitute.For<IChatCommand>();
    cmd.BeginSubCommand(Arg.Any<string>()).Returns(cmd);
    cmd.WithDescription(Arg.Any<string>()).Returns(cmd);
    cmd.WithArgs(Arg.Any<ICommandArgumentParser[]>()).Returns(cmd);
    cmd.RequiresPrivilege(Arg.Any<string>()).Returns(cmd);
    cmd.HandleWith(Arg.Do<OnCommandDelegate>(onHandler)).Returns(cmd);
    cmd.EndSubCommand().Returns(cmd);
    return cmd;
  }

  private OnCommandDelegate Handler(IExSubCommand sub) {
    OnCommandDelegate? handler = null;
    sub.Register(_world.Api, null!, FakeCommand(h => handler = h));
    return handler ?? throw new InvalidOperationException("no handler");
  }

  // The words a player typed after the sub-command, as its argument parsers hand them over.
  private static TextCommandCallingArgs Called(params string?[] words) =>
    new() {
      LanguageCode = Locale,
      Parsers =
      [
        .. words.Select(w =>
        {
          var parser = Substitute.For<ICommandArgumentParser>();
          parser.GetValue().Returns(w);
          return parser;
        }),
      ],
    };

  // Points a registry sub-command at entries of the test's own instead of its static registry.
  private static void Serve(
    object command,
    ICollection<string> codes,
    Delegate resolve
  ) {
    ReflectionHelpers.SetField(
      command,
      "_codes",
      (Func<IEnumerable<string>>)(() => codes)
    );
    ReflectionHelpers.SetField(command, "_resolve", resolve);
  }

  #endregion

  #region The root and the plain sub-commands

  // Fails when the server root's help is worded in the server's language.
  [Fact]
  public void The_server_root_answers_its_help_in_the_callers_language() {
    OnCommandDelegate? handler = null;
    IChatCommand root = FakeCommand(h => handler = h);
    _world.Api.ChatCommands.GetOrCreate("exmod").Returns(root);

    new ExmodCommand().Register(_world.Api, null!);

    Assert.Equal(
      Uk("exlib:command-exmod-help-server"),
      handler!(Called()).StatusMessage
    );
  }

  // Fails when the heal count is reported in the server's language.
  [Fact]
  public void Heal_reports_in_the_callers_language() {
    var healer = new BlockEntityHealModSystem();
    ReflectionHelpers.SetField(healer, "_sapi", _world.Api);
    _world.Mods.Register(healer);

    TextCommandResult result = Handler(new HealSubCommand())(Called());

    Assert.Equal(Uk("exlib:command-heal-result"), result.StatusMessage);
  }

  // Fails when the unknown-domain line is worded in the server's language.
  [Fact]
  public void Verify_names_an_unknown_domain_in_the_callers_language() {
    _world.Mods.Add("stub", "1.0.0");

    TextCommandResult result = Handler(new VerifySubCommand())(
      Called("nosuchmod")
    );

    Assert.Equal(Uk("exlib:command-verify-unknown"), result.StatusMessage);
  }

  // Fails when the summary line is worded in the server's language.
  [Fact]
  public void Verify_sums_up_in_the_callers_language() {
    _world.Mods.Add("stub", "1.0.0");

    TextCommandResult result = Handler(new VerifySubCommand())(Called("stub"));

    Assert.Equal(Uk("exlib:command-verify-summary"), result.StatusMessage);
  }

  // Fails when the count of errors past the first ten is worded in the server's language.
  [Fact]
  public void Verify_counts_the_errors_it_leaves_out_in_the_callers_language() {
    string[] errors = [.. Enumerable.Range(1, 11).Select(i => "error " + i)];

    TextCommandResult result = VerifySubCommand.Report(
      [new CheckResult("check", "stub", errors)],
      Locale
    );

    Assert.Equal(
      string.Join(
        "\n",
        [
          Uk("exlib:command-verify-summary"),
          .. errors.Take(10),
          Uk("exlib:command-verify-more"),
        ]
      ),
      result.StatusMessage
    );
  }

  #endregion

  #region The registry sub-commands

  private static RecipeProfile Profile(string level) =>
    new() {
      Code = "demo",
      Catalogue = () => new Dictionary<string, RecipeCostEntry>(),
      Defaults = () => new Dictionary<string, RecipeCostEntry>(),
      GetLevel = () => level,
      SetLevel = _ => { },
      SaveCatalogue = () => { },
    };

  private OnCommandDelegate Recipes(params RecipeProfile[] profiles) {
    var command = new RecipesSubCommand();
    OnCommandDelegate handler = Handler(command);
    var byCode = profiles.ToDictionary(p => p.Code);
    Serve(
      command,
      byCode.Keys,
      (System.Func<string, RecipeProfile?>)(c => byCode.GetValueOrDefault(c))
    );
    return handler;
  }

  // Fails when the registry scaffold words its empty list, list header or unknown-code line in the
  // server's language.
  [Fact]
  public void The_registry_lines_are_in_the_callers_language() {
    Assert.Equal(
      Uk("exlib:command-recipes-none"),
      Recipes()(Called(null, null)).StatusMessage
    );
    Assert.Equal(
      Uk("exlib:command-recipes-list") + "\n  demo: normal",
      Recipes(Profile("normal"))(Called(null, null)).StatusMessage
    );
    Assert.Equal(
      Uk("exlib:command-recipes-unknown"),
      Recipes(Profile("normal"))(Called("nope", null)).StatusMessage
    );
  }

  // Fails when any of the four recipe-level replies is worded in the server's language.
  [Theory]
  [InlineData(null, "exlib:command-recipes-status")]
  [InlineData("dear", "exlib:command-recipes-invalid")]
  [InlineData("normal", "exlib:command-recipes-retain")]
  [InlineData("cheap", "exlib:command-recipes-set")]
  public void Recipes_replies_in_the_callers_language(string? level, string key) {
    TextCommandResult result = Recipes(Profile("normal"))(
      Called("demo", level)
    );

    Assert.Equal(Uk(key), result.StatusMessage);
  }

  private OnCommandDelegate Config(ExConfigEditStatus status) {
    var config = Substitute.For<IExConfigAccess>();
    config.ModId.Returns("demo");
    config.ValueNames.Returns(["Rate"]);
    config
      .TryGet("Rate", out Arg.Any<string>(), out Arg.Any<string>())
      .Returns(ci => {
        ci[1] = "Rate";
        ci[2] = "1";
        return true;
      });
    config
      .Set(Arg.Any<string>(), Arg.Any<string>())
      .Returns(new ExConfigEditResult { Status = status, Name = "Rate" });
    var command = new ConfigSubCommand();
    OnCommandDelegate handler = Handler(command);
    Serve(
      command,
      ["demo"],
      (System.Func<string, IExConfigAccess?>)(c => c == "demo" ? config : null)
    );
    return handler;
  }

  // Fails when the value list, a read or any write outcome is worded in the server's language, or
  // is handed over as a bare key the console would log as it stands.
  [Theory]
  [InlineData(null, null, ExConfigEditStatus.Ok, "exlib:command-config-values")]
  [InlineData(
    "Nope",
    null,
    ExConfigEditStatus.Ok,
    "exlib:command-config-novalue"
  )]
  [InlineData(
    "Rate",
    null,
    ExConfigEditStatus.Ok,
    "exlib:command-config-current"
  )]
  [InlineData("Rate", "2", ExConfigEditStatus.Ok, "exlib:command-config-set")]
  [InlineData(
    "Rate",
    "x",
    ExConfigEditStatus.ParseFailed,
    "exlib:command-config-parsefail"
  )]
  [InlineData(
    "Rate",
    "-1",
    ExConfigEditStatus.OutOfRange,
    "exlib:command-config-range"
  )]
  [InlineData(
    "Nope",
    "2",
    ExConfigEditStatus.UnknownValue,
    "exlib:command-config-novalue"
  )]
  public void Config_replies_in_the_callers_language(
    string? name,
    string? value,
    ExConfigEditStatus status,
    string key
  ) {
    string? rest = name == null ? null : (name + " " + value).Trim();

    TextCommandResult result = Config(status)(Called("demo", rest));

    Assert.Equal(Uk(key), result.StatusMessage.Split('\n')[0]);
    Assert.Null(result.MessageParams);
  }

  #endregion
}
