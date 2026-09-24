using System.Collections.Generic;
using ExpandedLib.Catalogues;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The summary every catalogue loader hands back from its <c>Load(ICoreAPI)</c>: what it read, what it
/// kept, and one line per thing it could not.
/// </summary>
public class CatalogueLoadReportTests {
  [Fact]
  public void Log_writes_one_summary_line_and_one_line_per_error() {
    var logger = new RecordingLogger();
    logger.Expect(EnumLogType.Error, "unknown key 'thicknes'");
    var report = new CatalogueLoadReport(
      "metals",
      3,
      12,
      ["iiex:config/metals/bad.json: unknown key 'thicknes'"]
    );

    report.Log(logger);

    Assert.Equal(
      [
        (
          EnumLogType.Notification,
          "[exlib] metals: 3 file(s), 12 entr(ies), 1 error(s)"
        ),
        (
          EnumLogType.Error,
          "[exlib] iiex:config/metals/bad.json: unknown key 'thicknes'"
        ),
      ],
      logger.Entries
    );
  }

  [Fact]
  public void A_report_with_no_errors_still_writes_its_summary() {
    var logger = new RecordingLogger();
    var report = new CatalogueLoadReport("liquids", 1, 4, []);

    report.Log(logger);

    Assert.Equal(
      [
        (
          EnumLogType.Notification,
          "[exlib] liquids: 1 file(s), 4 entr(ies), 0 error(s)"
        ),
      ],
      logger.Entries
    );
  }
}
