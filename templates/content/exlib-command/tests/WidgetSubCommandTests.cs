using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using WidgetNamespace.Commands;
using Xunit;

namespace WidgetNamespace.Tests;

/// <summary>Registers onto a fake <c>/exmod</c> and answers its one line.</summary>
public class WidgetSubCommandTests {
  [Fact]
  public void Answers_its_result_line() {
    TestLang.Init();
    OnCommandDelegate? handler = null;
    var parent = Substitute.For<IChatCommand>();
    parent.WithDescription(Arg.Any<string>()).Returns(parent);
    parent.HandleWith(Arg.Do<OnCommandDelegate>(h => handler = h))
      .Returns(parent);
    parent.EndSubCommand().Returns(parent);
    parent.BeginSubCommand(Arg.Any<string>()).Returns(parent);

    var command = new WidgetSubCommand();
    command.Register(new TestWorld().Api, null!, parent);

    Assert.NotNull(handler);
    TextCommandResult result = handler!(null!);

    Assert.Equal(EnumCommandStatus.Success, result.Status);
    Assert.Equal("widgetdomain:command-widget-result", result.StatusMessage);
  }
}
