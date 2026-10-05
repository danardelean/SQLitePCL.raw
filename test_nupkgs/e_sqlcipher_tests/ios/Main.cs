using System;
using System.IO;

namespace e_sqlcipher_tests
{
    // No UIApplication: the checks run in Main and the process exits, so the
    // simulator run needs no UI and `simctl launch --console` returns by itself.
    static class Program
    {
        static int Main()
        {
            string workDir = Path.Combine(Path.GetTempPath(), "e_sqlcipher_tests_" + Guid.NewGuid().ToString("N"));
            try
            {
                return SqlcipherChecks.Run(workDir, Console.WriteLine) == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine(SqlcipherChecks.FailedMarker + " (unhandled) " + ex);
                return 1;
            }
        }
    }
}
