# e_sqlcipher package tests

Runtime tests for the e_sqlcipher packages on every platform they ship for.
The same code runs in three heads:

| Head | Platforms | How to run |
|------|-----------|------------|
| `desktop` | Windows, macOS, Linux | `dotnet run --project desktop` |
| `ios` | iOS simulator | `./run_ios_simulator.sh` |
| `android` | Android device or emulator | `./run_android.sh` |

Each head runs, through `shared/TestRunner.cs`:

1. the core SQLitePCLRaw test suite (`src/common/tests_xunit.cs`, about 137 tests, with the
   crypto and SQLCipher fixture tests enabled), using the reflection runner in
   `src/common/fake_xunit.cs`;
2. the SQLCipher checks in `shared/SqlcipherChecks.cs`.

Each run ends with `E_SQLCIPHER_TESTS: PASSED` or `E_SQLCIPHER_TESTS: FAILED ...`, and the
desktop head and both scripts exit non-zero on failure. The iOS and Android heads build with
`UseMonoRuntime=false`, and a check fails if the app runs on Mono: they test CoreCLR.

## Packages under test

`nuget.config` in this folder takes `e_sqlcipher` and every `SQLitePCLRaw.*` package from
`../../nupkgs` and everything else (Microsoft.Data.Sqlite.Core) from nuget.org, which is how
an application consumes them. Put the packages to validate in `nupkgs/` (the CI artifact
`nupkg-all`, or a local build) before running.

NuGet caches packages by id and version: after rebuilding a package without changing its
version, delete it from `~/.nuget/packages` or the old build is tested again.

## Expected versions

Pass the versions the native library must report, so a stale library is caught:

```bash
dotnet run --project desktop -p:ExpectedSqlcipherVersion=4.20.0 -p:ExpectedSqliteVersion=3.54.1
./run_ios_simulator.sh 4.20.0 3.54.1
./run_android.sh 4.20.0 3.54.1
```

Without them the version checks only require the library to answer.

## What the SQLCipher checks cover

- Keys: `PRAGMA key`, raw hex key, `rekey`, `sqlite3_key` / `sqlite3_key_v2` / `sqlite3_rekey`,
  wrong and missing key.
- Format: no plaintext header, `cipher_plaintext_header_size` + `cipher_salt`, custom
  `cipher_page_size` / `kdf_iter` / HMAC and KDF algorithms, `cipher_integrity_check`,
  tamper detection, `cipher_memory_security`, the crypto provider (OpenSSL or CommonCrypto).
- Data movement: `sqlcipher_export` both ways, `ATTACH ... KEY` with different keys, the backup
  API, `sqlite3_serialize` / `deserialize`.
- Concurrency: WAL with many pages, four concurrent writers in WAL mode.
- Compile options: FTS3/FTS4 (with parentheses), FTS5, JSON1, RTREE, GEOPOLY, SOUNDEX, math
  functions, session / preupdate hook / column metadata in `PRAGMA compile_options`.
- Callbacks on an encrypted database (functions, collations, update and commit hooks).
- Compatibility: a database created by SQLCipher 4.14.0 (`shared/fixtures`), and in the core
  suite one from SQLCipher 4.0 plus a `cipher_compatibility = 3` database upgraded with
  `cipher_migrate`.
- Microsoft.Data.Sqlite with `Password=`.

## Notes

- The iOS head has no UI: the tests run in `Main` and the process exits
  (`ValidateSceneManifest=false`).
- The Android head is an instrumentation, started with `am instrument -w`.
- Hosted CI runs Android on an x86_64 emulator only: run `./run_android.sh` against an arm64
  device or emulator (on an Apple Silicon Mac, the Android Studio emulator) before a release.
