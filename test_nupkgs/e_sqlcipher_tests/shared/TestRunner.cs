using System;
using System.IO;
using System.Text;

namespace e_sqlcipher_tests
{
    // Entry point shared by the desktop, iOS and Android heads: runs the core
    // SQLitePCLRaw test suite (src/common/tests_xunit.cs, through the reflection
    // runner in src/common/fake_xunit.cs) and then the SQLCipher checks, and
    // prints a single final marker that the run scripts and CI look for.
    public static class TestRunner
    {
        public const string PassedMarker = "E_SQLCIPHER_TESTS: PASSED";
        public const string FailedMarker = "E_SQLCIPHER_TESTS: FAILED";

        public static int Run(string workDir, Action<string> log)
        {
            log("runtime: " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
                + " on " + System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier);

            int coreFailed;
            string coreDir = Path.Combine(workDir, "core");
            string previous = Directory.GetCurrentDirectory();
            try
            {
                // The core tests create their databases in the current directory,
                // which is read-only inside an iOS bundle or an Android process.
                Directory.CreateDirectory(coreDir);
                Directory.SetCurrentDirectory(coreDir);
                SQLitePCL.Batteries_V2.Init();
                using (var output = new LineWriter(log))
                {
                    Xunit.Run.Output = output;
                    coreFailed = Xunit.Run.AllTestsIn(typeof(TestRunner).Assembly);
                }
            }
            catch (Exception ex)
            {
                log("[FAIL] core suite: " + ex);
                coreFailed = 1;
            }
            finally
            {
                Directory.SetCurrentDirectory(previous);
            }

            int checksFailed = SqlcipherChecks.Run(Path.Combine(workDir, "checks"), log);

            int failed = coreFailed + checksFailed;
            log(failed == 0
                ? PassedMarker
                : $"{FailedMarker} (core suite: {coreFailed}, SQLCipher checks: {checksFailed})");
            return failed;
        }

        // Turns the runner's Write/WriteLine calls into whole log lines.
        sealed class LineWriter : TextWriter
        {
            readonly Action<string> log;
            readonly StringBuilder line = new StringBuilder();

            public LineWriter(Action<string> log) { this.log = log; }

            public override Encoding Encoding => Encoding.UTF8;

            public override void Write(char value)
            {
                if (value == '\n')
                {
                    log(line.ToString().TrimEnd('\r'));
                    line.Clear();
                }
                else
                {
                    line.Append(value);
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (line.Length > 0)
                    log(line.ToString());
                line.Clear();
                base.Dispose(disposing);
            }
        }
    }
}
