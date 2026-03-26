/*
 * Stub implementations for SQLite APIs that were removed in SQLite 3.46.0.
 *
 * SQLCipher 4.14.0 is based on SQLite 3.51.3 which no longer includes the
 * experimental snapshot APIs.  SQLitePCLRaw's provider still declares
 * P/Invoke entries for them.  On desktop (dynamic linking) missing symbols
 * only fail at runtime if called.  On iOS (static linking via __Internal)
 * every declared symbol must resolve at link time — so we provide these
 * no-op stubs that return SQLITE_ERROR.
 */

#ifndef SQLITE_ERROR
#define SQLITE_ERROR 1
#endif

typedef struct sqlite3 sqlite3;
typedef struct sqlite3_snapshot sqlite3_snapshot;

int sqlite3_snapshot_get(sqlite3 *db, const char *zSchema, sqlite3_snapshot **ppSnapshot) {
    if (ppSnapshot) *ppSnapshot = 0;
    return SQLITE_ERROR;
}

int sqlite3_snapshot_open(sqlite3 *db, const char *zSchema, sqlite3_snapshot *pSnapshot) {
    return SQLITE_ERROR;
}

int sqlite3_snapshot_recover(sqlite3 *db, const char *zDb) {
    return SQLITE_ERROR;
}

int sqlite3_snapshot_cmp(sqlite3_snapshot *p1, sqlite3_snapshot *p2) {
    return 0;
}

void sqlite3_snapshot_free(sqlite3_snapshot *pSnapshot) {
    /* no-op */
}
