using System.Collections.Generic;
using ExpandedLib.Catalogues;
using ExpandedLib.Registries;
using Vintagestory.API.Common;

namespace WasherPunch;

/// <summary>Registers this assembly's attribute-marked classes and code-first definitions via
/// <see cref="ExModSystem"/>, and adds the copper job to the press's table from C#; the iron job is in
/// <c>config/processjobs/washerpunch.json</c>.</summary>
public class WasherPunchModSystem : ExModSystem {
  /// <summary>The key the press's jobs are filed under in <see cref="ProcessJobRegistry"/>.</summary>
  public const string Machine = "washerpunch";

  /// <summary>Washers one plate gives: a punch cuts through the plate, so the plate's 9 x 9 face over
  /// the washer's 2 x 2 outer square, rounded down. The press makes one a stroke.</summary>
  public const int WashersPerPlate = 20;

  protected override void OnStart(ICoreAPI api) =>
    ProcessJobRegistry.Contributors.Register(_ =>
      ProcessExtensions.Shared.AddJobs(
        Machine,
        [
          new ProcessJob(
            "game:metalplate-copper",
            "washerpunch:washer-copper",
            WashersPerPlate,
            null,
            null,
            0f,
            Strokes: WashersPerPlate
          ),
        ]
      )
    );
}
