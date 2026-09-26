-- SQLite schema version 1
PRAGMA user_version=1;
CREATE TABLE IF NOT EXISTS cache_entries (cache_key TEXT PRIMARY KEY, kind TEXT NOT NULL, status TEXT NOT NULL, payload TEXT NOT NULL, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, retry_after_utc TEXT);

CREATE TABLE IF NOT EXISTS source_files (stable_identity TEXT NOT NULL, source_path TEXT NOT NULL, size_bytes INTEGER NOT NULL, last_write_utc TEXT NOT NULL, sha256 TEXT NOT NULL, duration REAL NOT NULL, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, PRIMARY KEY(stable_identity,source_path));

CREATE TABLE IF NOT EXISTS fingerprints (sha256 TEXT PRIMARY KEY, duration REAL NOT NULL, fingerprint TEXT NOT NULL, algorithm TEXT NOT NULL, generator_version TEXT NOT NULL, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL);

CREATE TABLE IF NOT EXISTS identifications (cache_key TEXT PRIMARY KEY, stable_identity TEXT NOT NULL, lookup_status TEXT NOT NULL, acoustid_score REAL, acoustid TEXT, recording_id TEXT, release_id TEXT, artist TEXT, album_artist TEXT, album TEXT, title TEXT, track_number INTEGER, disc_number INTEGER, year INTEGER, evidence TEXT, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, retry_after_utc TEXT);

