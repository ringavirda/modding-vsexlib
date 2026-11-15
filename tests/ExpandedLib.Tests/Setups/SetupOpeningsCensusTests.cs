#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>The openings census <see cref="SetupCases.Run"/> writes when
/// <see cref="SetupRunOptions.OpeningsCensusVariable"/> names a file: one line per open face of a
/// pipe node, with the class of its neighbour.</summary>
[Collection("Setups")]
public class SetupOpeningsCensusTests : IDisposable
{
  private const string CensusVar = "EX_TEST_OPENINGS_CENSUS";

  private readonly string _folder = Path.Combine(
    Path.GetTempPath(),
    "exlib-setup-census-" + Guid.NewGuid().ToString("N")
  );

  public SetupOpeningsCensusTests()
  {
    Environment.SetEnvironmentVariable("EX_WRITE_SETUPS", null);
    Environment.SetEnvironmentVariable(CensusVar, null);
    Directory.CreateDirectory(_folder);
    File.WriteAllText(
      Path.Combine(_folder, "c.json"),
      SetupCaseTests.Cases("")
    );
  }

  public void Dispose()
  {
    Environment.SetEnvironmentVariable(CensusVar, null);
    if (Directory.Exists(_folder))
      Directory.Delete(_folder, recursive: true);
  }

  private const int NodeId = 900;

  private sealed class CensusNode : BlockNetworkNode
  {
    public override string NetworkType => "pipe";

    public override bool HasConnectorAt(BlockFacing face) =>
      face != BlockFacing.WEST;
  }

  private sealed class SourcingNode : BlockNetworkNode, IPipeVentSource
  {
    public BlockFacing? Claimed { get; init; }

    public override string NetworkType => "pipe";

    public override bool HasConnectorAt(BlockFacing face) =>
      face != BlockFacing.WEST;

    public IPipeVentStrategy? CreateVentStrategy() => new ClaimVent(Claimed);
  }

  private sealed class ClaimVent(BlockFacing? claimed) : IPipeVentStrategy
  {
    public bool TryClassifyVent(
      IBlockAccessor blockAccessor,
      BlockNetworkNode node,
      BlockPos pos,
      BlockFacing face,
      Block neighbour,
      out BlockPos ventPos
    )
    {
      ventPos = pos.AddCopy(face);
      return face == claimed;
    }

    public float Vent(
      IReadOnlyList<BlockPos> vents,
      PipeNetworkState state,
      bool liquid,
      BlockNetworkModSystem manager
    ) => 0f;
  }

  private static Block Solid(Block block, string code, int id, int sides)
  {
    TestBlocks.Configure(block, code, id);
    block.SideSolid = new SmallBoolArray(sides);
    return block;
  }

  private static void Fill(
    TestWorld w,
    BlockPos node,
    BlockFacing face,
    Block? block
  )
  {
    if (block != null)
      w.Place(node.AddCopy(face), block);
  }

  private SetupRunOptions Options(
    IPipeVentStrategy? factory,
    Block node,
    Action<TestWorld, BlockPos> around
  ) =>
    new()
    {
      ModFolder = _ => SetupCaseTests.BoxPath,
      Recordings = Path.Combine(_folder, "Recordings"),
      ExlibVersion = "0.8.4",
      BeforeLoad = SetupCaseTests.Register,
      AfterLoad = w =>
      {
        w.Networks.ReplaceNetworkType(
          "pipe",
          () => new PipeNetwork(w.Networks, factory)
        );
        BlockPos at = new(5, 5, 5);
        w.Place(at, node);
        w.AddNode(at, "pipe");
        around(w, at);
      },
      OpeningsCensusVariable = CensusVar,
    };

  private void Run(SetupRunOptions options) =>
    SetupCases.Run((SetupCaseRow)SetupCases.In(_folder).Single()[0], options);

  private static Dictionary<string, string[]> Read(string file) =>
    File.ReadAllLines(file).Select(l => l.Split('\t')).ToDictionary(f => f[3]);

  private string CensusFile => Path.Combine(_folder, "census.tsv");

  // Fails when a structure cell is classed solid, or any neighbour is classed as another.
  [Fact]
  public void Census_ClassifiesEachNeighbour()
  {
    Environment.SetEnvironmentVariable(CensusVar, CensusFile);
    Block chimney = TestBlocks.Configure(new Block(), "test:chimney", 901);
    Block filler = Solid(new BlockStructureFiller(), "test:filler", 902, 0x3F);
    Block full = Solid(new Block(), "test:full", 903, 0x3F);
    Block slab = Solid(new Block(), "test:slab", 904, 0);

    Run(
      Options(
        null,
        TestBlocks.Configure(
          new SourcingNode { Claimed = BlockFacing.UP },
          "test:sourcing",
          NodeId
        ),
        (w, at) =>
        {
          Fill(w, at, BlockFacing.UP, chimney);
          Fill(w, at, BlockFacing.NORTH, filler);
          Fill(w, at, BlockFacing.SOUTH, full);
          Fill(w, at, BlockFacing.EAST, slab);
        }
      )
    );

    Dictionary<string, string[]> lines = Read(CensusFile);
    Assert.Equal(
      new Dictionary<string, string>
      {
        ["up"] = "vent",
        ["down"] = "air",
        ["north"] = "structure",
        ["south"] = "solid",
        ["east"] = "other",
      },
      lines.ToDictionary(kv => kv.Key, kv => kv.Value[6])
    );
    Assert.Equal(
      [
        "cases",
        "main",
        "5,5,5",
        "north",
        "test:sourcing",
        "test:filler",
        "structure",
      ],
      lines["north"]
    );
  }

  // Fails when the node's own strategy is not asked (the face is classed air), or when only the
  // node's IPipeVentSource is asked and the network factory's strategy is not.
  [Fact]
  public void Census_VentFaceIsVent()
  {
    Environment.SetEnvironmentVariable(CensusVar, CensusFile);
    Run(
      Options(
        null,
        TestBlocks.Configure(
          new SourcingNode { Claimed = BlockFacing.UP },
          "test:sourcing",
          NodeId
        ),
        (_, _) => { }
      )
    );
    Assert.Equal("vent", Read(CensusFile)["up"][6]);
    Assert.Equal("air", Read(CensusFile)["down"][6]);

    File.Delete(CensusFile);
    Run(
      Options(
        new ClaimVent(BlockFacing.UP),
        TestBlocks.Configure(new CensusNode(), "test:plain", NodeId),
        (_, _) => { }
      )
    );
    Assert.Equal("vent", Read(CensusFile)["up"][6]);
    Assert.Equal("air", Read(CensusFile)["down"][6]);
  }

  // Fails when a run with the variable unset or empty writes a census file.
  [Fact]
  public void Census_WritesOnlyWhenNamed()
  {
    SetupRunOptions options = Options(
      null,
      TestBlocks.Configure(new CensusNode(), "test:plain", NodeId),
      (_, _) => { }
    );

    Run(options);
    Environment.SetEnvironmentVariable(CensusVar, "");
    Run(options);

    Assert.DoesNotContain(
      Directory.GetFiles(_folder, "*", SearchOption.AllDirectories),
      f => f.EndsWith(".tsv")
    );

    Environment.SetEnvironmentVariable(CensusVar, CensusFile);
    Run(options);
    Assert.True(File.Exists(CensusFile));
  }
}
#endif
