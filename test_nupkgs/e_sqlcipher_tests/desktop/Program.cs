using System;
using System.IO;

namespace e_sqlcipher_tests
{
    static class Program
    {
        static int Main()
        {
            string workDir = Path.Combine(Path.GetTempPath(), "e_sqlcipher_tests_" + Guid.NewGuid().ToString("N"));
            try
            {
                return SqlcipherChecks.Run(workDir, Console.WriteLine) == 0 ? 0 : 1;
            }
            finally
            {
                try { Directory.Delete(workDir, true); } catch { }
            }
        }
    }
}
