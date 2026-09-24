using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="PressureVesselGate"/> over hand-built construction-staged defs.</summary>
public class PressureVesselGateTests {
  private const string Rivet = "exlibtest:rivet-iron";

  // Fails when RivetsRequired reads only the first stage, or counts an ingredient of another code.
  [Fact]
  [PlantedDefect(
    typeof(PressureVesselGate),
    nameof(PressureVesselGate.RivetsRequired)
  )]
  public void Rivets_asked_for_in_two_stages_sum() {
    ExBlockDef boiler = ExBlockDef
      .Create("exlibtest", "boiler")
      .Construction(c =>
        c.Stage(s =>
            s.RequireRivets("exlibtest", Rivet, 3)
              .RequireMetalPlate("exlibtest", 2)
          )
          .Stage(s => s.AddElements("Root/Shell"))
          .Stage(s =>
            s.RequireRivets("exlibtest", Rivet, 5)
              .RequireRivets("exlibtest", "exlibtest:rivet-steel", 7)
          )
      );

    Assert.Equal(8, PressureVesselGate.RivetsRequired(boiler, Rivet));
  }

  // Fails when RivetsRequired counts something for a build that asks for no rivets.
  [Fact]
  [PlantedDefect(
    typeof(PressureVesselGate),
    nameof(PressureVesselGate.RivetsRequired)
  )]
  public void A_build_asking_for_no_rivets_costs_none() {
    ExBlockDef bunker = ExBlockDef
      .Create("exlibtest", "bunker")
      .Construction(c => c.Stage(s => s.RequireMetalPlate("exlibtest", 4)));

    Assert.Equal(0, PressureVesselGate.RivetsRequired(bunker, Rivet));
  }

  // Fails when NailedIngredients stops naming a nails ingredient in a later stage.
  [Fact]
  [PlantedDefect(
    typeof(PressureVesselGate),
    nameof(PressureVesselGate.NailedIngredients)
  )]
  public void Nails_asked_for_in_a_later_stage_are_reported() {
    ExBlockDef boiler = ExBlockDef
      .Create("exlibtest", "boiler")
      .Construction(c =>
        c.Stage(s => s.RequireRivets("exlibtest", Rivet, 3))
          .Stage(s => s.RequireMetalNails("exlibtest", 2))
      );

    Assert.Equal(
      ["metalnailsandstrips-*"],
      PressureVesselGate.NailedIngredients(boiler)
    );
  }

  [Fact]
  public void A_riveted_build_has_no_nailed_ingredient() {
    ExBlockDef boiler = ExBlockDef
      .Create("exlibtest", "boiler")
      .Construction(c =>
        c.Stage(s =>
          s.RequireRivets("exlibtest", Rivet, 3)
            .RequireMetalPlate("exlibtest", 2)
        )
      );

    Assert.Empty(PressureVesselGate.NailedIngredients(boiler));
  }
}
