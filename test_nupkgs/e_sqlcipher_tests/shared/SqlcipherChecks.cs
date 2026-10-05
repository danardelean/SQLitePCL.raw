using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Data.Sqlite;
using SQLitePCL;
using SQLitePCL.Ugly;

namespace e_sqlcipher_tests
{
    // Runtime checks for the e_sqlcipher packages.  The same code runs in the
    // desktop, iOS and Android heads, so it only writes through the log
    // callback and only touches files under workDir.
    public static class SqlcipherChecks
    {
        public const string PassedMarker = "E_SQLCIPHER_TESTS: PASSED";
        public const string FailedMarker = "E_SQLCIPHER_TESTS: FAILED";

        const string Key = "correct horse battery staple";
        const string PlainHeader = "SQLite format 3\0";

        public static int Run(string workDir, Action<string> log)
        {
            var checks = new List<(string name, Action<string> body)>
            {
                ("reports the expected SQLCipher version", ReportsExpectedVersion),
                ("round-trips data through an encrypted database", RoundTrip),
                ("does not write a plaintext header", HeaderIsNotPlaintext),
                ("rejects the wrong key", RejectsWrongKey),
                ("rejects a missing key", RejectsMissingKey),
                ("accepts a raw hex key", RawHexKey),
                ("changes the key with rekey", Rekey),
                ("exports an encrypted database to plaintext", ExportToPlaintext),
                ("exports a plaintext database to encrypted", ExportToEncrypted),
                ("passes cipher_integrity_check", IntegrityCheck),
                ("survives WAL mode with many pages", WalWithManyPages),
                ("has FTS5, JSON1, RTREE and math functions", CompileTimeFeatures),
                ("opens a database created by SQLCipher 4.14.0", OpensOlderDatabase),
                ("works through Microsoft.Data.Sqlite with Password", MicrosoftDataSqlite),
            };

            int failed = 0;
            try
            {
                Directory.CreateDirectory(workDir);
                Batteries_V2.Init();
            }
            catch (Exception ex)
            {
                log("[FAIL] initialization: " + ex);
                log(FailedMarker + " (initialization)");
                return checks.Count;
            }

            foreach (var (name, body) in checks)
            {
                string dir = Path.Combine(workDir, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                try
                {
                    body(dir);
                    log("[PASS] " + name);
                }
                catch (Exception ex)
                {
                    failed++;
                    log("[FAIL] " + name + ": " + ex.GetType().Name + ": " + ex.Message);
                }
                finally
                {
                    try { Directory.Delete(dir, true); } catch { }
                }
            }

            log(failed == 0
                ? $"{PassedMarker} {checks.Count}/{checks.Count}"
                : $"{FailedMarker} {failed} of {checks.Count}");
            return failed;
        }

        // Set at build time with -p:ExpectedSqlcipherVersion=x.y.z; empty skips the comparison.
        static string ExpectedVersion =>
            typeof(SqlcipherChecks).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "ExpectedSqlcipherVersion")?.Value ?? "";

        static void ReportsExpectedVersion(string dir)
        {
            using (var db = ugly.open(":memory:"))
            {
                string cipher = db.query_scalar<string>("PRAGMA cipher_version;");
                Require(!string.IsNullOrEmpty(cipher), "PRAGMA cipher_version is empty: SQLCipher is not linked");

                string expected = ExpectedVersion;
                if (expected.Length > 0)
                    Require(cipher.StartsWith(expected + " ", StringComparison.Ordinal) || cipher == expected,
                        $"expected SQLCipher {expected}, found '{cipher}'");
            }
        }

        static void RoundTrip(string dir)
        {
            string path = CreateEncrypted(dir, "a.db", Key);
            using (var db = OpenWithKey(path, Key))
                RequireEqual("hello sqlcipher", db.query_scalar<string>("SELECT value FROM t WHERE id = 1;"));
        }

        static void HeaderIsNotPlaintext(string dir)
        {
            string path = CreateEncrypted(dir, "a.db", Key);
            Require(!HasPlainHeader(path), "the file starts with the plaintext SQLite header");
        }

        static void RejectsWrongKey(string dir)
        {
            string path = CreateEncrypted(dir, "a.db", Key);
            RequireThrows(() =>
            {
                using (var db = OpenWithKey(path, "not the key"))
                    db.query_scalar<string>("SELECT value FROM t WHERE id = 1;");
            }, "the database was readable with the wrong key");
        }

        static void RejectsMissingKey(string dir)
        {
            string path = CreateEncrypted(dir, "a.db", Key);
            RequireThrows(() =>
            {
                using (var db = ugly.open(path))
                    db.query_scalar<string>("SELECT value FROM t WHERE id = 1;");
            }, "the database was readable without a key");
        }

        static void RawHexKey(string dir)
        {
            const string hex = "x'2DD29CA851E7B56E4697B0E1F08507293D761A05CE4D1B628663F411A8086D99'";
            string path = Path.Combine(dir, "a.db");
            using (var db = ugly.open(path))
            {
                db.exec($"PRAGMA key = \"{hex}\";");
                db.exec("CREATE TABLE t (id INTEGER PRIMARY KEY, value TEXT);");
                db.exec("INSERT INTO t (value) VALUES ('raw key');");
            }
            using (var db = ugly.open(path))
            {
                db.exec($"PRAGMA key = \"{hex}\";");
                RequireEqual("raw key", db.query_scalar<string>("SELECT value FROM t WHERE id = 1;"));
            }
        }

        static void Rekey(string dir)
        {
            const string newKey = "a different key";
            string path = CreateEncrypted(dir, "a.db", Key);
            using (var db = OpenWithKey(path, Key))
                db.exec($"PRAGMA rekey = '{newKey}';");

            using (var db = OpenWithKey(path, newKey))
                RequireEqual("hello sqlcipher", db.query_scalar<string>("SELECT value FROM t WHERE id = 1;"));

            RequireThrows(() =>
            {
                using (var db = OpenWithKey(path, Key))
                    db.query_scalar<string>("SELECT value FROM t WHERE id = 1;");
            }, "the old key still works after rekey");
        }

        static void ExportToPlaintext(string dir)
        {
            string source = CreateEncrypted(dir, "a.db", Key);
            string dest = Path.Combine(dir, "plain.db");
            using (var db = OpenWithKey(source, Key))
            {
                db.exec($"ATTACH DATABASE '{dest}' AS plain KEY '';");
                db.exec("SELECT sqlcipher_export('plain');");
                db.exec("DETACH DATABASE plain;");
            }

            Require(HasPlainHeader(dest), "the exported file is not a plaintext database");
            Require(!HasPlainHeader(source), "the source lost its encryption");
            using (var db = ugly.open(dest))
                RequireEqual("hello sqlcipher", db.query_scalar<string>("SELECT value FROM t WHERE id = 1;"));
        }

        static void ExportToEncrypted(string dir)
        {
            string source = Path.Combine(dir, "plain.db");
            using (var db = ugly.open(source))
            {
                db.exec("CREATE TABLE t (id INTEGER PRIMARY KEY, value TEXT);");
                db.exec("INSERT INTO t (value) VALUES ('was plaintext');");
            }

            string dest = Path.Combine(dir, "encrypted.db");
            using (var db = ugly.open(source))
            {
                db.exec($"ATTACH DATABASE '{dest}' AS encrypted KEY '{Key}';");
                db.exec("SELECT sqlcipher_export('encrypted');");
                db.exec("DETACH DATABASE encrypted;");
            }

            Require(!HasPlainHeader(dest), "the exported file is not encrypted");
            using (var db = OpenWithKey(dest, Key))
                RequireEqual("was plaintext", db.query_scalar<string>("SELECT value FROM t WHERE id = 1;"));
        }

        static void IntegrityCheck(string dir)
        {
            string path = CreateEncrypted(dir, "a.db", Key);
            using (var db = OpenWithKey(path, Key))
            {
                var problems = db.query_one_column<string>("PRAGMA cipher_integrity_check;").ToList();
                Require(problems.Count == 0, "cipher_integrity_check reported: " + string.Join("; ", problems));
                RequireEqual("ok", db.query_scalar<string>("PRAGMA integrity_check;"));
            }
        }

        static void WalWithManyPages(string dir)
        {
            const int rows = 5000;
            string path = Path.Combine(dir, "a.db");
            using (var db = OpenWithKey(path, Key))
            {
                RequireEqual("wal", db.query_scalar<string>("PRAGMA journal_mode = WAL;"));
                db.exec("CREATE TABLE t (id INTEGER PRIMARY KEY, value TEXT);");
                db.exec("BEGIN;");
                for (int i = 0; i < rows; i++)
                    db.exec("INSERT INTO t (value) VALUES (?);", "row " + i + " " + new string('x', 200));
                db.exec("COMMIT;");
            }
            using (var db = OpenWithKey(path, Key))
            {
                RequireEqual(rows, db.query_scalar<int>("SELECT count(*) FROM t;"));
                RequireEqual("ok", db.query_scalar<string>("PRAGMA integrity_check;"));
            }
        }

        static void CompileTimeFeatures(string dir)
        {
            using (var db = ugly.open(":memory:"))
            {
                db.exec("CREATE VIRTUAL TABLE docs USING fts5(body);");
                db.exec("INSERT INTO docs (body) VALUES ('the quick brown fox');");
                RequireEqual(1, db.query_scalar<int>("SELECT count(*) FROM docs WHERE docs MATCH 'quick';"));

                RequireEqual("42", db.query_scalar<string>("SELECT json_extract('{\"a\":42}', '$.a');"));

                db.exec("CREATE VIRTUAL TABLE boxes USING rtree(id, minX, maxX, minY, maxY);");
                db.exec("INSERT INTO boxes VALUES (1, 0, 10, 0, 10);");
                RequireEqual(1, db.query_scalar<int>("SELECT count(*) FROM boxes WHERE minX <= 5 AND maxX >= 5;"));

                RequireEqual(3, db.query_scalar<int>("SELECT CAST(sqrt(9) AS INTEGER);"));
            }
        }

        static void OpensOlderDatabase(string dir)
        {
            string path = Path.Combine(dir, "sqlcipher_4_14_0.db");
            var assembly = typeof(SqlcipherChecks).Assembly;
            string resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("sqlcipher_4_14_0.db", StringComparison.Ordinal));
            using (var input = assembly.GetManifestResourceStream(resource))
            using (var output = File.Create(path))
                input.CopyTo(output);

            using (var db = OpenWithKey(path, "fixture-key"))
            {
                RequireEqual("created by sqlcipher 4.14.0", db.query_scalar<string>("SELECT value FROM fixture WHERE id = 1;"));
                db.exec("INSERT INTO fixture (value) VALUES ('written by the current build');");
                RequireEqual(2, db.query_scalar<int>("SELECT count(*) FROM fixture;"));
            }
        }

        static void MicrosoftDataSqlite(string dir)
        {
            string path = Path.Combine(dir, "a.db");
            string connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Password = Key,
                Pooling = false,
            }.ToString();

            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                Execute(connection, "CREATE TABLE t (id INTEGER PRIMARY KEY, value TEXT);");
                Execute(connection, "INSERT INTO t (value) VALUES ('through ado.net');");
            }

            Require(!HasPlainHeader(path), "Microsoft.Data.Sqlite wrote a plaintext database");

            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT value FROM t WHERE id = 1;";
                    RequireEqual("through ado.net", (string)command.ExecuteScalar());
                }
            }

            RequireThrows(() =>
            {
                var wrong = new SqliteConnectionStringBuilder { DataSource = path, Password = "not the key", Pooling = false }.ToString();
                using (var connection = new SqliteConnection(wrong))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT value FROM t WHERE id = 1;";
                        command.ExecuteScalar();
                    }
                }
            }, "Microsoft.Data.Sqlite opened the database with the wrong password");
        }

        static void Execute(SqliteConnection connection, string sql)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }

        static string CreateEncrypted(string dir, string name, string key)
        {
            string path = Path.Combine(dir, name);
            using (var db = OpenWithKey(path, key))
            {
                db.exec("CREATE TABLE t (id INTEGER PRIMARY KEY, value TEXT);");
                db.exec("INSERT INTO t (value) VALUES ('hello sqlcipher');");
            }
            return path;
        }

        static sqlite3 OpenWithKey(string path, string key)
        {
            var db = ugly.open(path);
            db.exec($"PRAGMA key = '{key}';");
            return db;
        }

        static bool HasPlainHeader(string path)
        {
            byte[] header = new byte[PlainHeader.Length];
            using (var stream = File.OpenRead(path))
            {
                if (stream.Read(header, 0, header.Length) != header.Length)
                    return false;
            }
            return Encoding.ASCII.GetString(header) == PlainHeader;
        }

        static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        static void RequireEqual<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException($"expected '{expected}', found '{actual}'");
        }

        static void RequireThrows(Action action, string messageIfNot)
        {
            try
            {
                action();
            }
            catch
            {
                return;
            }
            throw new InvalidOperationException(messageIfNot);
        }
    }
}
