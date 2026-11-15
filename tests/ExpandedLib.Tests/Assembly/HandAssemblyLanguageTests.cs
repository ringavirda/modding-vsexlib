using System;
using ExpandedLib.Assembling;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Xunit;
using Scene = ExpandedLib.Tests.HandAssemblyFixTests.Scene;

namespace ExpandedLib.Tests;

/// <summary>A refusal of hand assembly reaches the player worded in the player's language, with the tool named in it, whatever language the server runs in.</summary>
[Collection(LangCollection.Name)]
public class HandAssemblyLanguageTests : IDisposable {
  private const string Locale = "uk";

  private static readonly BlockPos Ground = new(64, 15, 64, 0);

  public HandAssemblyLanguageTests() {
    TestLang.Init();
    var svc = Substitute.For<ITranslationService>();
    svc.LanguageCode.Returns(Locale);
    svc.Get(Arg.Any<string>(), Arg.Any<object[]>())
      .Returns(ci => Locale + ":" + ci.Arg<string>());
    svc.GetMatching(Arg.Any<string>(), Arg.Any<object[]>())
      .Returns(ci => Locale + ":" + ci.Arg<string>());
    svc.HasTranslation(Arg.Any<string>(), Arg.Any<bool>()).Returns(true);
    svc.HasTranslation(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
      .Returns(true);
    Lang.AvailableLanguages[Locale] = svc;
  }

  public void Dispose() => Lang.AvailableLanguages.Remove(Locale);

  // Fails when the tool's name in a refusal is worded in the server's language.
  [Fact]
  public void A_fix_refusal_names_the_tool_in_the_players_language() {
    var scene = Scene.Standing(["ns"]);
    scene.Player.ServerPlayer!.LanguageCode.Returns(Locale);
    scene.Player.HoldOffHand(null);

    AssemblyResult? result = scene.Fix("ns", Ground, BlockFacing.UP);

    Assert.Equal(HandAssemblyServer.NeedsTool, result!.Code);
    Assert.Equal("uk:test:item-wrench-iron", result.Args[0]);
  }

  // Fails when the gate words its line with the server's language.
  [Fact]
  public void A_placement_outside_the_handler_is_refused_in_the_players_language() {
    var scene = Scene.Standing(["ns"]);
    scene.Player.GameMode = EnumGameMode.Survival;
    scene.Player.ServerPlayer!.LanguageCode.Returns(Locale);
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
          == "uk:game:placefailure-" + HandAssemblyServer.NeedsTool
    );
  }

  // Fails when a told line is worded with the server's language.
  [Fact]
  public void A_told_line_is_worded_in_the_players_language() {
    var scene = Scene.Standing(["ns"]);
    scene.Player.ServerPlayer!.LanguageCode.Returns(Locale);

    HandAssemblyServer.Tell(
      scene.World.World,
      scene.Player.ServerPlayer!,
      HandAssemblyServer.Fixed
    );

    Assert.Contains(
      scene.Player.ServerPlayer!.ReceivedCalls(),
      call =>
        call.GetMethodInfo().Name == "SendIngameError"
        && (string)call.GetArguments()[1]!
          == "uk:game:placefailure-" + HandAssemblyServer.Fixed
    );
  }
}
