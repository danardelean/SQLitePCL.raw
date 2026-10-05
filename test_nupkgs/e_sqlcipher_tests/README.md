# e_sqlcipher package tests

Runtime checks for the e_sqlcipher packages on every platform they ship for.
One set of checks (`shared/SqlcipherChecks.cs`) runs in three heads:

| Head | Platforms | How to run |
|------|-----------|------------|
| `desktop` | Windows, macOS, Linux | `dotnet run --project desktop` |
| `ios` | iOS simulator | `./run_ios_simulator.sh` |
| `android` | Android device or emulator | `./run_android.sh` |

Each run ends with `E_SQLCIPHER_TESTS: PASSED n/n` or `E_SQLCIPHER_TESTS: FAILED ...`,
and the desktop head and both scripts exit non-zero on failure.

## Packages under test

`nuget.config` in this folder takes the four e_sqlcipher packages (`e_sqlcipher`,
`SQLitePCLRaw.provider.e_sqlcipher`, `SQLitePCLRaw.config.e_sqlcipher`,
`SQLitePCLRaw.bundle_e_sqlcipher`) from `../../nupkgs` and everything else from nuget.org,
which is how an application consumes them. Put the packages to validate in `nupkgs/`
(the CI artifact `nupkg-all`, or a local build) before running.

NuGet caches packages by id and version: after rebuilding a package without changing its
version, delete it from `~/.nuget/packages` or the old build is tested again.

## Expected SQLCipher version

Pass the version the native library must report, so a stale library is caught:

```bash
dotnet run --project desktop -p:ExpectedSqlcipherVersion=4.19.0
./run_ios_simulator.sh 4.19.0
./run_android.sh 4.19.0
```

Without it the version check only requires `PRAGMA cipher_version` to answer.

## What is checked

Encryption round trip, no plaintext header, wrong and missing key, raw hex key, `rekey`,
`sqlcipher_export` in both directions, `cipher_integrity_check`, WAL with many pages,
FTS5 / JSON1 / RTREE / math functions, a database created by SQLCipher 4.14.0
(`shared/fixtures`), and Microsoft.Data.Sqlite with `Password=`.

## Notes

- The iOS head has no UI: the checks run in `Main` and the process exits.
- The Android head is an instrumentation, started with `am instrument -w`.
- Hosted CI runs Android on an x86_64 emulator only: run `./run_android.sh` against an arm64
  device or emulator before a release.
