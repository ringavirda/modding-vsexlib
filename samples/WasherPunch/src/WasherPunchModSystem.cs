using System.Collections.Generic;
using ExpandedLib.Catalogues;
using ExpandedLib.Industry.Machining;
using ExpandedLib.Registries;
using Vintagestory.API.Common;

namespace WasherPunch;

/// <summary>Registers this assembly's attribute-marked classes and code-first definitions via
/// <see cref="ExModSystem"/>, and adds the copper job to the press's table and the meteoric plate's job to
/// the drilling machine's from C#; the iron jobs are in <c>config/processjobs/washerpunch.json</c> and
/// <c>config/machiningjobs/drill.json</c>.</summary>
public class WasherPunchModSystem : ExModSystem {
  /// <summary>The key the press's jobs are filed under in <see cref="ProcessJobRegistry"/>.</summary>
  public const string Machine = "washerpunch";

  /// <summary>Washers one plate gives: a punch cuts through the plate, so the plate's 9 x 9 face over
  /// the washer's 2 x 2 outer square, rounded down. The press makes one a stroke.</summary>
  public const int WashersPerPlate = 20;

  /// <summary>Mass the drilling machine cuts from one plate, kg: four holes 0.5 voxel across through a
  /// plate 1 voxel thick, 4 x pi/4 x 0.25 x 1 voxel3 at 0.000244 m3 a voxel3 and 7810 kg/m3 of wrought
  /// iron.</summary>
  public const float DrilledKg = 1.5f;

  /// <summary>Temper the meteoric plate asks of the bit: steel's.</summary>
  public const int MeteoricMinTier = 2;

  protected override void OnStart(ICoreAPI api) {
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
    MachiningJobRegistry.Contributors.Register(_ =>
      MachiningJobRegistry.Shared.Contribute(
        new MachiningJobSet(
          MachiningJobSet.CurrentSchema,
          [
            new MachiningJob(
              "drill",
              "game:metalplate-meteoriciron",
              "washerpunch:drilledplate-meteoriciron",
              1,
              "wroughtiron",
              DrilledKg,
              null,
              MeteoricMinTier,
              MachiningJob.DefaultWear,
              null,
              [],
              [],
              null
            ),
          ]
        )
      )
    );
  }
}
