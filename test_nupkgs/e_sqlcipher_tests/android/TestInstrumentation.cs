using System;
using System.Text;
using Android.App;
using Android.OS;
using Android.Runtime;
using Android.Util;

namespace e_sqlcipher_tests
{
    // Run with:
    //   adb shell am instrument -w com.sqlitepclraw.e_sqlcipher_tests/e_sqlcipher_tests.TestInstrumentation
    // The log comes back in the "stream" result, so no logcat polling is needed.
    [Instrumentation(Name = "e_sqlcipher_tests.TestInstrumentation")]
    public class TestInstrumentation : Instrumentation
    {
        const string Tag = "E_SQLCIPHER_TESTS";

        protected TestInstrumentation(IntPtr handle, JniHandleOwnership transfer)
            : base(handle, transfer)
        {
        }

        public override void OnCreate(Bundle arguments)
        {
            base.OnCreate(arguments);
            Start();
        }

        public override void OnStart()
        {
            var output = new StringBuilder();
            int failed;
            try
            {
                string workDir = System.IO.Path.Combine(TargetContext.CacheDir.AbsolutePath, "e_sqlcipher_tests");
                failed = SqlcipherChecks.Run(workDir, line =>
                {
                    output.AppendLine(line);
                    Log.Info(Tag, line);
                });
            }
            catch (Exception ex)
            {
                failed = 1;
                string line = SqlcipherChecks.FailedMarker + " (unhandled) " + ex;
                output.AppendLine(line);
                Log.Error(Tag, line);
            }

            var results = new Bundle();
            results.PutString("stream", "\n" + output);
            Finish(failed == 0 ? Result.Ok : Result.Canceled, results);
        }
    }
}
