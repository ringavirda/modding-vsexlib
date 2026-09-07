using System;
using System.Reflection;
using System.Reflection.Emit;
using ExpandedLib.Helpers;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// <see cref="ExCreativeTabs.EnsureTab"/> reaches into the client's
/// <c>Vintagestory.Client.NoObf.GuiDialogCreativeTabs</c>, which no test lane loads (there is no
/// live client here). The first call below exercises the documented degrade path: with the type
/// absent from every loaded assembly, the call is a no-op rather than a throw. The rest of the
/// test then stands a throwaway type of that exact name up via Reflection.Emit, so the reflection
/// body that appends to its static <c>tabs</c> field runs for real.
/// </summary>
public class ExCreativeTabsTests {
  [Fact]
  public void EnsureTab_appends_once_and_no_ops_when_the_client_type_is_absent() {
    // No client type loaded yet: the degrade path is a no-op, not a throw.
    ExCreativeTabs.EnsureTab("iiex");

    FieldInfo tabsField = DefineDynamicClientType(["existing"]);

    ExCreativeTabs.EnsureTab("iiex");
    Assert.Equal(["existing", "iiex"], (string[])tabsField.GetValue(null)!);

    ExCreativeTabs.EnsureTab("iiex"); // already present: not appended a second time
    Assert.Equal(["existing", "iiex"], (string[])tabsField.GetValue(null)!);
  }

  // Builds a throwaway type named exactly like the client's internal dialog, with a static
  // string[] tabs field, so EnsureTab's reflection body finds it instead of degrading.
  private static FieldInfo DefineDynamicClientType(string[] initialTabs) {
    var name = new AssemblyName(
      $"ExCreativeTabsTests.Dynamic.{Guid.NewGuid():N}"
    );
    AssemblyBuilder asm = AssemblyBuilder.DefineDynamicAssembly(
      name,
      AssemblyBuilderAccess.Run
    );
    ModuleBuilder module = asm.DefineDynamicModule(name.Name!);
    TypeBuilder client = module.DefineType(
      "Vintagestory.Client.NoObf.GuiDialogCreativeTabs",
      TypeAttributes.Public
    );
    client.DefineField(
      "tabs",
      typeof(string[]),
      FieldAttributes.Public | FieldAttributes.Static
    );
    Type type = client.CreateType();
    FieldInfo tabsField = type.GetField("tabs")!;
    tabsField.SetValue(null, initialTabs);
    return tabsField;
  }
}
