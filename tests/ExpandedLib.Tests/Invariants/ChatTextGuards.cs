using System.Collections.Generic;
using System.IO;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Every lang value exlib's source sends to chat is free of raw markup; the rule is
/// <see cref="ChatText"/>'.</summary>
[GuardOf(typeof(ChatText), nameof(ChatText.Check))]
public class ChatTextGuards {
  private static readonly Dictionary<string, string> Allowed = new();

  private static readonly Dictionary<string, string> KnownFindings = new();

  [Fact]
  public void Every_chat_value_is_free_of_raw_markup() {
    var (keys, findings) = ChatText.Check(
      "exlib",
      RepoPaths.Src("exlib"),
      Path.Combine(RepoPaths.Assets("exlib"), "lang")
    );

    Premise.NotEmpty(keys, "exlib chat call naming a lang key");
    FindingLists.Assert(findings, Allowed, KnownFindings);
  }
}
