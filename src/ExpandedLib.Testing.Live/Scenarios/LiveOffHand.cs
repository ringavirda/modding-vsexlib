using System.Collections.Generic;
using System.IO;
using ExpandedLib.Scenarios;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing.Live;

/// <summary>The off-hand allow list of the run and the one way a scenario fills an off-hand slot.</summary>
internal static class LiveOffHand {
  /// <summary>The codes the run's mods still put in the off hand; empty until the runner loads them.</summary>
  internal static IReadOnlyDictionary<string, string> Allowed { get; set; } =
    new Dictionary<string, string>();

  /// <summary>Reads every direct subfolder's allow list.</summary>
  /// <param name="scenariosFolder">The server's <c>Scenarios</c> folder, one subfolder per mod.</param>
  /// <returns>The union of the lists, each code with its mechanic.</returns>
  /// <exception cref="SetupFileException">A list is malformed or a code is listed in two.</exception>
  internal static IReadOnlyDictionary<string, string> Load(
    string scenariosFolder
  ) {
    var union = new Dictionary<string, string>(System.StringComparer.Ordinal);
    if (!Directory.Exists(scenariosFolder))
      return union;
    foreach (string folder in Directory.GetDirectories(scenariosFolder)) {
      string file = Path.Combine(folder, SetupOffHand.FileName);
      if (!File.Exists(file))
        continue;
      foreach (
        (string code, string mechanic) in SetupOffHand.Parse(
          File.ReadAllText(file),
          file
        )
      )
        if (!union.TryAdd(code, mechanic))
          throw new SetupFileException($"{file}: {code} is listed twice.");
    }
    return union;
  }

  /// <summary>Puts <paramref name="stack"/> in <paramref name="slot"/> unless the game's off-hand slot
  /// would refuse it and its code is not in <see cref="Allowed"/>.</summary>
  /// <exception cref="StepFailure">The stack is refused; the slot is untouched.</exception>
  internal static void Put(ItemSlot slot, ItemStack? stack) {
    if (SetupOffHand.Refusal(stack, Allowed) is { } refusal)
      throw new StepFailure(refusal, Suspect.Scenario);
    slot.Itemstack = stack;
  }
}
