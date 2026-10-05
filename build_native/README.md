# e_sqlcipher - Native SQLCipher Builds

This package contains native builds of [SQLCipher](https://github.com/sqlcipher/sqlcipher) 4.19.0
(based on SQLite 3.53.4) for use with `SQLitePCLRaw.provider.e_sqlcipher`.

## Platforms

| RID | Library | Crypto Backend |
|-----|---------|----------------|
| linux-x64 | libe_sqlcipher.so | OpenSSL (static) |
| linux-arm64 | libe_sqlcipher.so | OpenSSL (static) |
| osx-x64 | libe_sqlcipher.dylib | CommonCrypto |
| osx-arm64 | libe_sqlcipher.dylib | CommonCrypto |
| win-x64 | e_sqlcipher.dll | OpenSSL |
| win-arm64 | e_sqlcipher.dll | OpenSSL |

## Compile-time options

All builds include these SQLite/SQLCipher features:

- `SQLITE_ENABLE_FTS3`, `FTS4`, `FTS5` (full-text search)
- `SQLITE_ENABLE_JSON1` (JSON functions)
- `SQLITE_ENABLE_RTREE`, `GEOPOLY` (spatial indexing)
- `SQLITE_ENABLE_COLUMN_METADATA`
- `SQLITE_ENABLE_MATH_FUNCTIONS`
- `SQLITE_ENABLE_LOAD_EXTENSION`
- `SQLITE_ENABLE_PREUPDATE_HOOK`, `SESSION`
- `SQLITE_SOUNDEX`
- `SQLITE_THREADSAFE=1`
- `SQLITE_TEMP_STORE=2`

## Usage

This package is typically consumed through `SQLitePCLRaw.bundle_e_sqlcipher`,
which brings in both this native library and the appropriate .NET provider.

```csharp
SQLitePCL.Batteries_V2.Init();
```

## Relationship with upstream SQLitePCLRaw

This fork tracks upstream `v3.0.5` and only adds SQLCipher support. `SQLitePCLRaw.core` and
`SQLitePCLRaw.provider.internal` are unmodified, so consumers take them from nuget.org.

Publish only these packages to the private feed:

| Package | Version |
|---------|---------|
| `e_sqlcipher` | 4.19.0 |
| `SQLitePCLRaw.provider.e_sqlcipher` | same as upstream (3.0.5) |
| `SQLitePCLRaw.config.e_sqlcipher` | same as upstream (3.0.5) |
| `SQLitePCLRaw.bundle_e_sqlcipher` | same as upstream (3.0.5) |

Do not publish the other `SQLitePCLRaw.*` packages the build produces: they would shadow the
official ones.

When moving to a new upstream release, set `OFFICIAL_BUILD_NUMBER` in `version_stamp/Program.cs`
to the last component of the assembly version of the official `SQLitePCLRaw.core` package
(3129 for 3.0.5). The SQLCipher assemblies are compiled against the locally built core; with a
higher number they fail to load the official one at runtime.
