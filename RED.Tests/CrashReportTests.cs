using System;
using RED.Helper;
using Xunit;

namespace RED.Tests
{
    // The crash report captures the exception, OS, runtime, and app version - metadata
    // only, never file contents - so a user can attach it to a GitHub issue.
    public class CrashReportTests
    {
        [Fact]
        public void BuildReport_IncludesExceptionAndEnvironment()
        {
            Exception ex;
            try { throw new InvalidOperationException("boom-marker"); }
            catch (Exception caught) { ex = caught; }

            string report = CrashReport.BuildReport(ex, "unit-test-context");

            Assert.Contains("RED++ crash report", report);
            Assert.Contains("InvalidOperationException", report);
            Assert.Contains("boom-marker", report);                 // message + stack present
            Assert.Contains("unit-test-context", report);           // the context label
            Assert.Contains("Runtime:", report);
            Assert.Contains("OS:", report);
            Assert.Contains("Version:", report);
        }

        [Fact]
        public void BuildReport_NullException_DoesNotThrow()
        {
            string report = CrashReport.BuildReport(null, null);
            Assert.Contains("RED++ crash report", report);
            Assert.Contains("(no exception object)", report);
            Assert.Contains("unknown", report); // context defaults to "unknown"
        }
    }
}
