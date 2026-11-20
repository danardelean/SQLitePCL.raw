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
                ("runs on CoreCLR", RunsOnCoreClr),
                ("reports the expected SQLite version", ReportsExpectedSqliteVersion),
                ("has the expected compile options", CompileOptions),
                ("uses the expected crypto provider", CryptoProvider),
                ("keys through the C API (key, key_v2, rekey)", CApiKeys),
                ("honours custom cipher settings", CustomCipherSettings),
                ("keeps a plaintext header when asked", PlaintextHeader),
                ("attaches databases with different keys", AttachWithDifferentKeys),
                ("backs up an encrypted database", BackupEncrypted),
                ("serializes an encrypted database", SerializeEncrypted),
                ("detects a tampered page", DetectsTampering),
                ("handles concurrent writers in WAL mode", ConcurrentWalWriters),
                ("has FTS3/FTS4, GEOPOLY and SOUNDEX", MoreCompileTimeFeatures),
                ("runs callbacks on an encrypted database", CallbacksOnEncrypted),
                // Last: memory security is process-wide and cannot be turned off again.
                ("enables cipher_memory_security", MemorySecurity),
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

            log($"SQLCipher checks: {checks.Count - failed}/{checks.Count} passed");
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

        // The mobile heads exist to test the CoreCLR runtime that .NET 11 uses on
        // iOS and Android, so fail loudly if the app actually ran on Mono.
        static void RunsOnCoreClr(string dir)
        {
            Require(Type.GetType("Mono.Runtime") == null,
                "running on Mono: " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        }

        static void ReportsExpectedSqliteVersion(string dir)
        {
            using (var db = ugly.open(":memory:"))
            {
                string version = db.query_scalar<string>("SELECT sqlite_version();");
                string expected = Metadata("ExpectedSqliteVersion");
                if (expected.Length > 0)
                    RequireEqual(expected, version);
                RequireEqual(version, raw.sqlite3_libversion().utf8_to_string());
            }
        }

        static void CompileOptions(string dir)
        {
            using (var db = ugly.open(":memory:"))
            {
                var options = new HashSet<string>(db.query_one_column<string>("PRAGMA compile_options;"));
                foreach (var required in new[]
                {
                    "HAS_CODEC", "TEMP_STORE=2", "THREADSAFE=1",
                    "ENABLE_FTS3", "ENABLE_FTS3_PARENTHESIS", "ENABLE_FTS4", "ENABLE_FTS5",
                    "ENABLE_RTREE", "ENABLE_GEOPOLY", "ENABLE_COLUMN_METADATA", "ENABLE_MATH_FUNCTIONS",
                    "ENABLE_PREUPDATE_HOOK", "ENABLE_SESSION", "SOUNDEX",
                })
                    Require(options.Contains(required), "compile option missing: " + required);
            }
        }

        static void CryptoProvider(string dir)
        {
            using (var db = OpenWithKey(Path.Combine(dir, "a.db"), Key))
            {
                string provider = db.query_scalar<string>("PRAGMA cipher_provider;");
                bool apple = OperatingSystem.IsIOS() || OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsTvOS();
                RequireEqual(apple ? "commoncrypto" : "openssl", provider);
                Require(!string.IsNullOrEmpty(db.query_scalar<string>("PRAGMA cipher_provider_version;")),
                    "cipher_provider_version is empty");
            }
        }

        static void CApiKeys(string dir)
        {
            string path = Path.Combine(dir, "a.db");
            byte[] key = Encoding.UTF8.GetBytes(Key);
            byte[] newKey = Encoding.UTF8.GetBytes("rekeyed through the C API");

            using (var db = ugly.open(path))
            {
                RequireEqual(raw.SQLITE_OK, raw.sqlite3_key(db, key));
                db.exec("CREATE TABLE t (id INTEGER PRIMARY KEY, value TEXT);");
                db.exec("INSERT INTO t (value) VALUES ('c api');");
            }
            Require(!HasPlainHeader(path), "sqlite3_key wrote a plaintext database");

            // sqlite3_key and PRAGMA key derive the same key from the same passphrase.
            using (var db = OpenWithKey(path, Key))
                RequireEqual("c api", db.query_scalar<string>("SELECT value FROM t WHERE id = 1;"));

            using (var db = ugly.open(path))
            {
                RequireEqual(raw.SQLITE_OK, raw.sqlite3_key_v2(db, utf8z.FromString("main"), key));
                RequireEqual(raw.SQLITE_OK, raw.sqlite3_rekey(db, newKey));
            }
            using (var db = ugly.open(path))
            {
                raw.sqlite3_key(db, newKey);
                RequireEqual("c api", db.query_scalar<string>("SELECT value FROM t WHERE id = 1;"));
            }
            RequireThrows(() =>
            {
                using (var db = ugly.open(path))
                {
                    raw.sqlite3_key(db, key);
                    db.query_scalar<string>("SELECT value FROM t WHERE id = 1;");
                }
            }, "the old key still works after sqlite3_rekey");
        }

        static void CustomCipherSettings(string dir)
        {
            string[] settings =
            {
                "PRAGMA cipher_page_size = 8192;",
                "PRAGMA kdf_iter = 10000;",
                "PRAGMA cipher_hmac_algorithm = HMAC_SHA256;",
                "PRAGMA cipher_kdf_algorithm = PBKDF2_HMAC_SHA256;",
            };
            string path = Path.Combine(dir, "a.db");
            using (var db = OpenWithKey(path, Key))
            {
                foreach (var pragma in settings)
                    db.exec(pragma);
                db.exec("CREATE TABLE t (id INTEGER PRIMARY KEY, value TEXT);");
                db.exec("INSERT INTO t (value) VALUES ('custom settings');");
                RequireEqual("8192", db.query_scalar<string>("PRAGMA cipher_page_size;"));
                RequireEqual("10000", db.query_scalar<string>("PRAGMA kdf_iter;"));
                RequireEqual("HMAC_SHA256", db.query_scalar<string>("PRAGMA cipher_hmac_algorithm;"));
                RequireEqual("PBKDF2_HMAC_SHA256", db.query_scalar<string>("PRAGMA cipher_kdf_algorithm;"));
            }

            RequireThrows(() =>
            {
                using (var db = OpenWithKey(path, Key))
                    db.query_scalar<string>("SELECT value FROM t WHERE id = 1;");
            }, "the database opened with the default settings");

            using (var db = OpenWithKey(path, Key))
            {
                foreach (var pragma in settings)
                    db.exec(pragma);
                RequireEqual("custom settings", db.query_scalar<string>("SELECT value FROM t WHERE id = 1;"));
            }
        }

        static void PlaintextHeader(string dir)
        {
            // Used on iOS so the OS can recognise a WAL database in a shared container.
            string path = Path.Combine(dir, "a.db");
            string salt;
            using (var db = OpenWithKey(path, Key))
            {
                db.exec("PRAGMA cipher_plaintext_header_size = 32;");
                db.exec("CREATE TABLE t (id INTEGER PRIMARY KEY, value TEXT);");
                db.exec("INSERT INTO t (value) VALUES ('plaintext header');");
                salt = db.query_scalar<string>("PRAGMA cipher_salt;");
            }
            Require(HasPlainHeader(path), "the file does not start with the plaintext SQLite header");
            Require(!string.IsNullOrEmpty(salt), "PRAGMA cipher_salt is empty");

            using (var db = OpenWithKey(path, Key))
            {
                db.exec("PRAGMA cipher_plaintext_header_size = 32;");
                db.exec($"PRAGMA cipher_salt = \"x'{salt}'\";");
                RequireEqual("plaintext header", db.query_scalar<string>("SELECT value FROM t WHERE id = 1;"));
            }
        }

        static void MemorySecurity(string dir)
        {
            using (var db = OpenWithKey(Path.Combine(dir, "a.db"), Key))
            {
                db.exec("PRAGMA cipher_memory_security = ON;");
                db.exec("CREATE TABLE t (x);");
                db.exec("INSERT INTO t VALUES (1);");
                RequireEqual(1, db.query_scalar<int>("SELECT x FROM t;"));
                RequireEqual("1", db.query_scalar<string>("PRAGMA cipher_memory_security;"));
                // SQLCipher only allows enabling it: OFF is ignored.
                db.exec("PRAGMA cipher_memory_security = OFF;");
                RequireEqual("1", db.query_scalar<string>("PRAGMA cipher_memory_security;"));
            }
        }

        static void AttachWithDifferentKeys(string dir)
        {
            string main = CreateEncrypted(dir, "main.db", Key);
            string other = Path.Combine(dir, "other.db");
            const string otherKey = "another key entirely";
            using (var db = OpenWithKey(main, Key))
            {
                db.exec($"ATTACH DATABASE '{other}' AS other KEY '{otherKey}';");
                db.exec("CREATE TABLE other.u (id INTEGER PRIMARY KEY, extra TEXT);");
                db.exec("INSERT INTO other.u (extra) VALUES ('from the other file');");
                RequireEqual("hello sqlcipher|from the other file",
                    db.query_scalar<string>("SELECT t.value || '|' || u.extra FROM t JOIN other.u u ON u.id = t.id;"));
                db.exec("DETACH DATABASE other;");
            }
            using (var db = OpenWithKey(other, otherKey))
                RequireEqual("from the other file", db.query_scalar<string>("SELECT extra FROM u WHERE id = 1;"));
            RequireThrows(() =>
            {
                using (var db = OpenWithKey(other, Key))
                    db.query_scalar<string>("SELECT extra FROM u WHERE id = 1;");
            }, "the attached file opened with the main key");
        }

        static void BackupEncrypted(string dir)
        {
            string source = CreateEncrypted(dir, "a.db", Key);
            string dest = Path.Combine(dir, "backup.db");
            using (var src = OpenWithKey(source, Key))
            using (var dst = OpenWithKey(dest, Key))
            {
                var backup = raw.sqlite3_backup_init(dst, "main", src, "main");
                Require(backup != null, "sqlite3_backup_init failed: " + raw.sqlite3_errmsg(dst).utf8_to_string());
                RequireEqual(raw.SQLITE_DONE, raw.sqlite3_backup_step(backup, -1));
                RequireEqual(raw.SQLITE_OK, raw.sqlite3_backup_finish(backup));
            }
            Require(!HasPlainHeader(dest), "the backup is not encrypted");
            using (var db = OpenWithKey(dest, Key))
                RequireEqual("hello sqlcipher", db.query_scalar<string>("SELECT value FROM t WHERE id = 1;"));
        }

        static void SerializeEncrypted(string dir)
        {
            // sqlite3_serialize goes through the pager, so it yields the decrypted
            // pages: an in-memory copy that opens without a key.
            string source = CreateEncrypted(dir, "a.db", Key);
            using (var db = OpenWithKey(source, Key))
            using (var copy = ugly.open(":memory:"))
            {
                db.query_scalar<int>("SELECT count(*) FROM t;");
                IntPtr data = raw.sqlite3_serialize(db, "main", out long size, 0);
                Require(data != IntPtr.Zero && size > 0, "sqlite3_serialize returned nothing");
                RequireEqual(raw.SQLITE_OK, raw.sqlite3_deserialize(copy, "main", data, size, size,
                    raw.SQLITE_DESERIALIZE_FREEONCLOSE | raw.SQLITE_DESERIALIZE_RESIZEABLE));
                RequireEqual("hello sqlcipher", copy.query_scalar<string>("SELECT value FROM t WHERE id = 1;"));
            }
        }

        static void DetectsTampering(string dir)
        {
            string path = Path.Combine(dir, "a.db");
            using (var db = OpenWithKey(path, Key))
            {
                db.exec("CREATE TABLE t (id INTEGER PRIMARY KEY, value TEXT);");
                db.exec("BEGIN;");
                for (int i = 0; i < 200; i++)
                    db.exec("INSERT INTO t (value) VALUES (?);", new string('v', 100));
                db.exec("COMMIT;");
            }
            // Flip one byte in the middle of the second page.
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            {
                stream.Position = 4096 + 2000;
                int b = stream.ReadByte();
                stream.Position = 4096 + 2000;
                stream.WriteByte((byte)(b ^ 0xFF));
            }
            RequireThrows(() =>
            {
                using (var db = OpenWithKey(path, Key))
                    db.query_scalar<int>("SELECT sum(length(value)) FROM t;");
            }, "a tampered page was read without an error");
        }

        static void ConcurrentWalWriters(string dir)
        {
            const int threads = 4, rows = 200;
            string path = Path.Combine(dir, "a.db");
            using (var db = OpenWithKey(path, Key))
            {
                RequireEqual("wal", db.query_scalar<string>("PRAGMA journal_mode = WAL;"));
                db.exec("CREATE TABLE t (id INTEGER PRIMARY KEY, worker INTEGER, n INTEGER);");
            }
            var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
            var workers = Enumerable.Range(0, threads).Select(w => new System.Threading.Thread(() =>
            {
                try
                {
                    using (var db = OpenWithKey(path, Key))
                    {
                        raw.sqlite3_busy_timeout(db, 10000);
                        for (int i = 0; i < rows; i++)
                            db.exec("INSERT INTO t (worker, n) VALUES (?, ?);", w, i);
                    }
                }
                catch (Exception ex)
                {
                    errors.Enqueue(ex);
                }
            })).ToList();
            workers.ForEach(t => t.Start());
            workers.ForEach(t => t.Join());
            Require(errors.IsEmpty, "a writer failed: " + string.Join("; ", errors.Select(e => e.Message)));

            using (var db = OpenWithKey(path, Key))
            {
                RequireEqual(threads * rows, db.query_scalar<int>("SELECT count(*) FROM t;"));
                RequireEqual(threads, db.query_scalar<int>("SELECT count(DISTINCT worker) FROM t;"));
                RequireEqual("ok", db.query_scalar<string>("PRAGMA integrity_check;"));
            }
        }

        static void MoreCompileTimeFeatures(string dir)
        {
            using (var db = OpenWithKey(Path.Combine(dir, "a.db"), Key))
            {
                db.exec("CREATE VIRTUAL TABLE f3 USING fts3(body);");
                db.exec("CREATE VIRTUAL TABLE f4 USING fts4(body);");
                foreach (var table in new[] { "f3", "f4" })
                {
                    db.exec($"INSERT INTO {table} (body) VALUES ('alpha beta'), ('alpha gamma'), ('delta');");
                    // Parentheses and AND/OR need SQLITE_ENABLE_FTS3_PARENTHESIS.
                    RequireEqual(2, db.query_scalar<int>($"SELECT count(*) FROM {table} WHERE body MATCH '(beta OR gamma) AND alpha';"));
                }
                RequireEqual(1, db.query_scalar<int>("SELECT count(*) FROM f4 WHERE body MATCH 'delta';"));

                RequireEqual(1.0, db.query_scalar<double>("SELECT geopoly_area('[[0,0],[1,0],[1,1],[0,1],[0,0]]');"));
                db.exec("CREATE VIRTUAL TABLE shapes USING geopoly();");
                db.exec("INSERT INTO shapes (_shape) VALUES ('[[0,0],[2,0],[2,2],[0,2],[0,0]]');");
                RequireEqual(1, db.query_scalar<int>("SELECT count(*) FROM shapes WHERE geopoly_contains_point(_shape, 1, 1);"));

                RequireEqual("R163", db.query_scalar<string>("SELECT soundex('Robert');"));
            }
        }

        static void CallbacksOnEncrypted(string dir)
        {
            // Reverse P/Invoke from native code into managed callbacks, which is what
            // changes most between Mono and CoreCLR on mobile.
            using (var db = OpenWithKey(Path.Combine(dir, "a.db"), Key))
            {
                db.exec("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT);");

                int updates = 0, commits = 0;
                raw.sqlite3_update_hook(db, (object user, int op, utf8z database, utf8z table, long rowid) => updates++, null);
                raw.sqlite3_commit_hook(db, user => { commits++; return 0; }, null);

                raw.sqlite3_create_function(db, "twice", 1, null, (ctx, user, args) =>
                    raw.sqlite3_result_int64(ctx, 2 * raw.sqlite3_value_int64(args[0])));
                raw.sqlite3_create_collation(db, "reverse", null, (object user, string a, string b) => string.CompareOrdinal(b, a));

                db.exec("INSERT INTO t (name) VALUES ('a'), ('c'), ('b');");
                RequireEqual(3, updates);
                Require(commits >= 1, "the commit hook did not run");
                RequireEqual(42L, db.query_scalar<long>("SELECT twice(21);"));
                RequireEqual("c,b,a", db.query_scalar<string>("SELECT group_concat(name, ',') FROM (SELECT name FROM t ORDER BY name COLLATE reverse);"));

                raw.sqlite3_update_hook(db, (delegate_update)null, null);
                raw.sqlite3_commit_hook(db, null, null);
            }
        }

        static string Metadata(string key) =>
            typeof(SqlcipherChecks).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == key)?.Value ?? "";

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
