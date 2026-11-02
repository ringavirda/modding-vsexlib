using ExpandedLib.Catalogues;
using ExpandedLib.Industry.Benches;
using Xunit;

namespace ExpandedLib.Tests.Benches;

/// <summary>The bench verdict table: machine, then blank, then drive.</summary>
public class BenchFeedTests {
  private static ProcessJob Job(float minTorque) =>
    new("test:in", "test:out", 1, null, null, minTorque);

  [Fact]
  public void A_bench_without_a_die_has_no_work() {
    var d = BenchFeed.Decide(false, Job(0f), 10f, 1f);

    Assert.Equal(BenchVerdict.NoDie, d.Verdict);
    Assert.Null(d.Job);
  }

  [Fact]
  public void A_blank_with_no_job_is_refused_before_the_drive_is_read() {
    var d = BenchFeed.Decide(true, null, 0f, 0f);

    Assert.Equal(BenchVerdict.NoJob, d.Verdict);
    Assert.False(d.Accepted);
  }

  // Fails when the speed gate is dropped: a stopped run with torque on tap then reads as Ok.
  [Fact]
  public void A_stopped_run_is_not_turning_whatever_torque_it_has() {
    var d = BenchFeed.Decide(true, Job(1f), 10f, 0f);

    Assert.Equal(BenchVerdict.NotTurning, d.Verdict);
  }

  // Fails when the torque comparison is flipped or loosened to strict.
  [Theory]
  [InlineData(4.9f, BenchVerdict.NotEnoughDrive)]
  [InlineData(5f, BenchVerdict.Ok)]
  [InlineData(9f, BenchVerdict.Ok)]
  public void The_drive_must_reach_the_jobs_torque(
    float available,
    BenchVerdict expected
  ) {
    var job = Job(5f);
    var d = BenchFeed.Decide(true, job, available, 1f);

    Assert.Equal(expected, d.Verdict);
    Assert.Same(job, d.Job);
    Assert.Equal(expected == BenchVerdict.Ok, d.Accepted);
  }
}
