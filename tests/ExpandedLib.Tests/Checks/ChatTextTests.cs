using System.Collections.Generic;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="ChatText"/> over planted sources and locales.</summary>
public class ChatTextTests {
  private sealed record Outcome(
    IReadOnlyList<string> Keys,
    IReadOnlyList<string> Findings
  );

  private static Outcome Run(string source, string english) {
    using var files = new PlantedFiles();
    files.Write("src/Planted.cs", source);
    files.Write("lang/en.json", english);
    var (keys, findings) = ChatText.Check(
      "plantedchat",
      files.Path("src"),
      files.Path("lang")
    );
    return new Outcome(keys, findings);
  }

  [Theory]
  [InlineData("a < b", "raw <")]
  [InlineData("a -> b", "raw >")]
  [InlineData("press <hk>sneak</hk>", "<hk>")]
  [PlantedDefect(typeof(ChatText), nameof(ChatText.Check))]
  public void A_chat_value_with_markup_is_reported(string value, string defect) {
    var (keys, findings) = Run(
      "player.SendLocalisedMessage(0, \"plantedchat:note\");",
      $$"""{ "note": "{{value}}" }"""
    );

    Assert.Equal(["plantedchat:note"], keys);
    Assert.Contains($"en: plantedchat:note (Planted.cs): {defect}", findings);
  }

  [Theory]
  [InlineData(
    "return TextCommandResult.Success(Lang.Get(\"plantedchat:note\"));"
  )]
  [InlineData(
    "return TextCommandResult.Error(Lang.Get(\"plantedchat:note\"));"
  )]
  [InlineData("api.ShowChatMessage(Lang.Get(\"plantedchat:note\"));")]
  [InlineData(
    "p.SendMessage(1, Lang.Get(\"plantedchat:note\"), T.Notification);"
  )]
  [InlineData(
    "const string Note = \"plantedchat:note\"; TextCommandResult.Success(Lang.Get(Note));"
  )]
  public void Every_chat_call_form_is_read(string source) {
    var (_, findings) = Run(source, """{ "note": "a < b" }""");

    Assert.Equal(["en: plantedchat:note (Planted.cs): raw <"], findings);
  }

  // Fails when a value is read although no chat call names its key.
  [Fact]
  public void A_handbook_value_no_chat_call_names_passes() {
    var (keys, findings) = Run(
      "var t = Lang.Get(\"plantedchat:page\");",
      """{ "page": "<strong>Furnace</strong>" }"""
    );

    Assert.Empty(keys);
    Assert.Empty(findings);
  }

  // Fails when a key of another domain is read against this domain's locale.
  [Fact]
  public void A_key_of_another_domain_is_not_read() {
    var (keys, findings) = Run(
      "player.SendLocalisedMessage(0, \"otherdomain:note\");",
      """{ "otherdomain:note": "a < b" }"""
    );

    Assert.Empty(keys);
    Assert.Empty(findings);
  }

  [Fact]
  public void A_clean_chat_value_passes() {
    var (keys, findings) = Run(
      "player.SendLocalisedMessage(0, \"plantedchat:note\");",
      """{ "note": "Repaired." }"""
    );

    Assert.Equal(["plantedchat:note"], keys);
    Assert.Empty(findings);
  }

  // Fails when a method called in the arguments is not followed into its body.
  [Theory]
  [InlineData(
    "TextCommandResult.Success(List()); string List() => Lang.Get(\"plantedchat:note\");"
  )]
  [InlineData(
    "TextCommandResult.Success(List()); string List() { return Lang.Get(\"plantedchat:note\"); }"
  )]
  public void A_method_called_in_the_arguments_is_read(string source) {
    var (keys, findings) = Run(source, """{ "note": "a < b" }""");

    Assert.Equal(["plantedchat:note"], keys);
    Assert.Equal(["en: plantedchat:note (Planted.cs): raw <"], findings);
  }

  // Fails when a key property named in the arguments is not resolved.
  [Fact]
  public void A_key_property_named_in_the_arguments_is_read() {
    var (keys, findings) = Run(
      "TextCommandResult.Error(Lang.Get(NoteKey)); string NoteKey => \"plantedchat:note\";",
      """{ "note": "a -> b" }"""
    );

    Assert.Equal(["plantedchat:note"], keys);
    Assert.Equal(["en: plantedchat:note (Planted.cs): raw >"], findings);
  }
}
