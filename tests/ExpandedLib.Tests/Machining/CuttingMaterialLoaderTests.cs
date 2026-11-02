using System;
using System.IO;
using ExpandedLib.Industry.Machining;
using Xunit;

namespace ExpandedLib.Tests.Machining;

/// <summary>The cutting material catalogue: JSON declarations, the merge rule, the shipped period file.</summary>
public class CuttingMaterialLoaderTests {
  [Fact]
  public void A_material_declared_in_json_is_registered_with_its_figures_and_the_hand_power() {
    CuttingMaterialRegistry registry = MachiningTestData.MaterialRegistry(
      MachiningTestData.Materials
    );

    Assert.True(registry.TryGet("wroughtiron", out CuttingMaterial? material));
    Assert.Equal(7810f, material!.Density);
    Assert.Equal(307800f, material.EnergyFor(CutMotion.Reciprocating));
    Assert.Equal(60f, registry.HandPower(CutMotion.Rotary));
    Assert.Equal(40f, registry.HandPower(CutMotion.Reciprocating));
  }

  [Fact]
  public void A_material_with_no_energy_is_rejected_and_the_rest_of_the_file_is_skipped() {
    var registry = new CuttingMaterialRegistry();
    var errors = CuttingMaterialLoader.Load(
      [
        (
          "test:config/cuttingmaterials/bad.json",
          """{ "schema": 1, "materials": [ { "code": "brass", "density": 8400 } ] }"""
        ),
      ],
      registry
    );

    Assert.Contains("brass", Assert.Single(errors));
    Assert.Equal(0, registry.Count);
  }

  [Fact]
  public void A_material_redeclared_with_other_figures_is_reported_and_the_first_stands() {
    CuttingMaterialRegistry registry = MachiningTestData.MaterialRegistry(
      MachiningTestData.Materials
    );
    var other = new CuttingMaterialSet(
      1,
      [new CuttingMaterial("castiron", 1f, 2f, 3f)]
    );

    Assert.Contains("castiron", Assert.Single(registry.Contribute(other)));
    Assert.True(registry.TryGet("castiron", out CuttingMaterial? held));
    Assert.Equal(7200f, held!.Density);
  }

  // Fails when the shipped file loses a material or a figure.
  [Fact]
  public void The_shipped_period_file_carries_the_period_figures_and_the_hand_power_of_the_digest() {
    string root = AppContext.BaseDirectory;
    while (!File.Exists(Path.Combine(root, "ExpandedLib.sln")))
      root = Path.GetDirectoryName(root)!;
    string json = File.ReadAllText(
      Path.Combine(
        root,
        "src/ExpandedLib/assets/exlib/config/cuttingmaterials/period.json"
      )
    );
    CuttingMaterialRegistry registry = MachiningTestData.MaterialRegistry(json);

    Assert.True(registry.TryGet("castiron", out CuttingMaterial? cast));
    Assert.Equal(7200f, cast!.Density);
    Assert.Equal(185800f, cast.RotaryEnergy);
    Assert.Equal(295500f, cast.ReciprocatingEnergy);
    Assert.True(registry.TryGet("wroughtiron", out CuttingMaterial? wrought));
    Assert.Equal(7810f, wrought!.Density);
    Assert.Equal(193500f, wrought.RotaryEnergy);
    Assert.Equal(307800f, wrought.ReciprocatingEnergy);
    Assert.Equal(61.0f, registry.HandPower(CutMotion.Rotary));
    Assert.Equal(44.7f, registry.HandPower(CutMotion.Reciprocating));
  }
}
