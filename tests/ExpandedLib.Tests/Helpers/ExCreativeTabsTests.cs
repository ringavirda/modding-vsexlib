using System;
using System.Reflection;
using System.Reflection.Emit;
using ExpandedLib.Helpers;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="ExCreativeTabs.EnsureTab"/> reaches into the client's
/// <c>Vintagestory.Client.NoObf.GuiDialogCreativeTabs</c> by reflection; a throwaway type of that
/// exact name stands in via Reflection.Emit.</summary>
public class ExCreativeTabsTests {
  [Fact]
  public void EnsureTab_appends_once_and_no_ops_when_the_client_type_is_absent() {
    ExCreativeTabs.EnsureTab("iiex");

    FieldInfo tabsField = DefineDynamicClientType(["existing"]);

    ExCreativeTabs.EnsureTab("iiex");
    Assert.Equal(["existing", "iiex"], (string[])tabsField.GetValue(null)!);

    ExCreativeTabs.EnsureTab("iiex");
    Assert.Equal(["existing", "iiex"], (string[])tabsField.GetValue(null)!);
  }

  // Builds a throwaway type named like the client's internal dialog, with a static tabs field.
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
