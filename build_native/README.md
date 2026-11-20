# e_sqlcipher - Native SQLCipher Builds

This package contains native builds of [SQLCipher](https://github.com/sqlcipher/sqlcipher) 4.20.0
(based on SQLite 3.54.1) for use with `SQLitePCLRaw.provider.e_sqlcipher`.

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

This fork is based on upstream `v3.0.5`, moved to .NET 11 (`net11.0`, `net11.0-ios`,
`net11.0-android`, ...; `netstandard2.0` stays for older runtimes) and adds SQLCipher support.
Since the .NET 11 build changes `SQLitePCLRaw.core` and the providers too, every
`SQLitePCLRaw.*` package carries the fork version `3.0.6-net11`, so it never collides with
the official packages on nuget.org.

Publish all of these together:

| Package | Version |
|---------|---------|
| `e_sqlcipher` | 4.20.0 |
| `SQLitePCLRaw.*` (core, ugly, providers, config, bundles) | 3.0.6-net11 |

The version comes from `MAJOR/MINOR/PATCH_VERSION` and `FORK_SUFFIX` in
`version_stamp/Program.cs`.
