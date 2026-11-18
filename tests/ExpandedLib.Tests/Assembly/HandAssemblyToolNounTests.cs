using System;
using System.Collections.Generic;
using System.IO;
using ExpandedLib.Assembling;
using ExpandedLib.Testing;
using Newtonsoft.Json;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Xunit;
using Scene = ExpandedLib.Tests.HandAssemblyFixTests.Scene;

namespace ExpandedLib.Tests;

/// <summary>A hand assembly refusal names the tool by a noun per pattern word, not by the name of whichever item the pattern matches first.</summary>
[Collection(LangCollection.Name)]
public class HandAssemblyToolNounTests : IDisposable {
  private static readonly BlockPos Ground = new(64, 15, 64, 0);

  private readonly Dictionary<string, ITranslationService?> _saved = [];

  public HandAssemblyToolNounTests() => TestLang.Init();

  public void Dispose() {
    foreach ((string locale, ITranslationService? service) in _saved)
      if (service == null)
        Lang.AvailableLanguages.Remove(locale);
      else
        Lang.AvailableLanguages[locale] = service;
  }

  private void UseShippedLang(string locale) {
    Lang.AvailableLanguages.TryGetValue(locale, out var previous);
    _saved.TryAdd(locale, previous);
    Lang.AvailableLanguages[locale] = ShippedLang(locale);
  }

  private static ITranslationService ShippedLang(string locale) {
    var entries = JsonConvert.DeserializeObject<Dictionary<string, string>>(
      File.ReadAllText(
        Path.Combine(RepoPaths.Assets("exlib"), "lang", locale + ".json")
      )
    )!;
    string Lookup(string key) =>
      entries.TryGetValue(
        key.StartsWith("exlib:") ? key["exlib:".Length..] : key,
        out string? text
      )
        ? text
        : key;
    bool Has(string key) =>
      entries.ContainsKey(
        key.StartsWith("exlib:") ? key["exlib:".Length..] : key
      );
    var svc = Substitute.For<ITranslationService>();
    svc.LanguageCode.Returns(locale);
    svc.Get(Arg.Any<string>(), Arg.Any<object[]>())
      .Returns(ci =>
        string.Format(Lookup(ci.Arg<string>()), ci.Arg<object[]>())
      );
    svc.HasTranslation(Arg.Any<string>(), Arg.Any<bool>())
      .Returns(ci => Has(ci.Arg<string>()));
    svc.HasTranslation(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
      .Returns(ci => Has(ci.Arg<string>()));
    return svc;
  }

  // Fails when the noun is the name of the first matching item, a metal and all.
  [Theory]
  [InlineData("en", "Needs a wrench in the off hand.")]
  [InlineData("ru", "Нужен гаечный ключ во второй руке.")]
  [InlineData("uk", "Потрібен гайковий ключ у другій руці.")]
  public void ToolNoun_WrenchPatternReadsWrench(string locale, string sentence) {
    UseShippedLang(locale);
    var scene = Scene.Standing(["ns"]);
    scene.World.RegisterItem("test:wrench-copper");
    scene.Player.ServerPlayer!.LanguageCode.Returns(locale);
    scene.Player.HoldOffHand(null);

    AssemblyResult? result = scene.Fix("ns", Ground, BlockFacing.UP);

    Assert.Equal(HandAssemblyServer.NeedsTool, result!.Code);
    Assert.Equal(
      sentence,
      HandAssemblyServer.Line(locale, result.Code, result.Args)
    );
  }

  // Fails when a word with no lang entry returns the bare word, not the item name.
  [Fact]
  public void ToolNoun_UnknownWordFallsBackToTheItemName() {
    var scene = Scene.Standing(["ns"]);
    scene.World.RegisterItem("test:saw-iron");
    string key = "exlib:handassembly-tool-saw";
    TestLang.Service.HasTranslation(key, Arg.Any<bool>()).Returns(false);
    try {
      string noun = HandAssemblyServer.ToolNoun(
        scene.World.World,
        "test:saw-*",
        "en"
      );

      Assert.Equal("test:item-saw-iron", noun);
    } finally {
      TestLang.Service.HasTranslation(key, Arg.Any<bool>()).Returns(true);
    }
  }

  // Fails when the survival gate words its refusal with the tool's item name.
  [Fact]
  public void Gate_SurvivalRefusalNamesTheNoun() {
    UseShippedLang("ru");
    var scene = Scene.Standing(["ns"]);
    scene.Player.GameMode = EnumGameMode.Survival;
    scene.Player.ServerPlayer!.LanguageCode.Returns("ru");
    string code = "";

    scene
      .Part("ns")
      .TryPlaceBlock(
        scene.World.World,
        scene.Player.Player,
        new ItemStack(scene.Part("ns")),
        Scene.Select(Ground, BlockFacing.UP),
        ref code
      );

    Assert.Contains(
      scene.Player.ServerPlayer!.ReceivedCalls(),
      call =>
        call.GetMethodInfo().Name == "SendIngameError"
        && (string)call.GetArguments()[1]!
          == "Нужен гаечный ключ во второй руке."
    );
  }
}
