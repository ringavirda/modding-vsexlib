using ExpandedLib.Helpers;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// <see cref="ExCreativeTabs.EnsureTab"/> reaches into the client's
/// <c>Vintagestory.Client.NoObf.GuiDialogCreativeTabs</c>, which no test lane loads (there is no
/// live client here). What is exercised is the documented degrade path: with the type absent from
/// every loaded assembly, the call is a no-op rather than a throw.
/// </summary>
public class ExCreativeTabsTests {
  [Fact]
  public void EnsureTab_does_not_throw_when_the_client_type_is_not_loaded() {
    ExCreativeTabs.EnsureTab("iiex");
  }
}
