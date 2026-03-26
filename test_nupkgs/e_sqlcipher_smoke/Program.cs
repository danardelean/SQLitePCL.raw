using System;
using SQLitePCL;
using SQLitePCL.Ugly;

namespace smoke_sqlcipher
{
    class Program
    {
        static void Main(string[] args)
        {
            Batteries_V2.Init();

            // Basic open + version check
            using (var db = ugly.open(":memory:"))
            {
                var version = db.query_scalar<string>("SELECT sqlite_version()");
                Console.WriteLine("SQLite version: {0}", version);
            }

            // Verify SQLCipher encryption works: open, key, create table, query
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "sqlcipher_smoke_" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using (var db = ugly.open(path))
                {
                    db.exec("PRAGMA key = 'test_password';");
                    db.exec("CREATE TABLE t1 (id INTEGER PRIMARY KEY, value TEXT);");
                    db.exec("INSERT INTO t1 (value) VALUES ('hello sqlcipher');");
                    var result = db.query_scalar<string>("SELECT value FROM t1 WHERE id = 1");
                    if (result != "hello sqlcipher")
                        throw new Exception($"Unexpected value: {result}");

                    var cipherVersion = db.query_scalar<string>("PRAGMA cipher_version;");
                    Console.WriteLine("SQLCipher version: {0}", cipherVersion);

                    if (string.IsNullOrEmpty(cipherVersion))
                        throw new Exception("PRAGMA cipher_version returned empty — SQLCipher not linked!");
                }

                // Verify the DB is actually encrypted: re-open with wrong key should fail
                bool wrongKeyFailed = false;
                try
                {
                    using (var db = ugly.open(path))
                    {
                        db.exec("PRAGMA key = 'wrong_password';");
                        db.query_scalar<string>("SELECT value FROM t1 WHERE id = 1");
                    }
                }
                catch
                {
                    wrongKeyFailed = true;
                }

                if (!wrongKeyFailed)
                    throw new Exception("Database was readable with wrong key — encryption not working!");

                Console.WriteLine("SQLCipher smoke test PASSED");
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }
    }
}
