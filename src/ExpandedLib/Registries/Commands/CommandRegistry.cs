using System;
using System.Reflection;
using Vintagestory.API.Common;

namespace ExpandedLib.Registries;

/// <summary>
/// Reflection-driven chat-command registration: scans an assembly for <see cref="IExCommand"/>
/// classes carrying <see cref="CommandRegisterAttribute"/> and <see cref="IExSubCommand"/> classes
/// carrying <see cref="SubCommandRegisterAttribute"/>, and builds each one.
/// </summary>
public static class CommandRegistry {
  /// <summary>Registers every attributed <see cref="IExCommand"/> and <see cref="IExSubCommand"/> in <paramref name="asm"/> (default: the calling assembly) whose declared side matches <paramref name="api"/>.</summary>
  public static void RegisterAll(ICoreAPI api, Mod mod, Assembly? asm = null) {
    asm ??= Assembly.GetCallingAssembly();
    string modId = mod.Info.ModID;

    ReflectionScan.ForEachAttributed<CommandRegisterAttribute, IExCommand>(
      api,
      modId,
      asm,
      (attr, command) => {
        if (attr.Side != EnumAppSide.Universal && attr.Side != api.Side)
          return;
        command.Register(api, mod);
      }
    );

    ReflectionScan.ForEachAttributed<
      SubCommandRegisterAttribute,
      IExSubCommand
    >(
      api,
      modId,
      asm,
      (attr, sub) => {
        if (attr.Side != EnumAppSide.Universal && attr.Side != api.Side)
          return;
        IChatCommand parent = api.ChatCommands.GetOrCreate(sub.ParentName);
        sub.Register(api, mod, parent);
      }
    );
  }
}
