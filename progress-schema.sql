-- Persistent collection progress, schema version 1
PRAGMA user_version=1;
CREATE TABLE IF NOT EXISTS music_files (id TEXT PRIMARY KEY, source_root TEXT NOT NULL, original_path TEXT NOT NULL, current_path TEXT NOT NULL COLLATE NOCASE UNIQUE, file_size INTEGER NOT NULL, last_write_time_utc TEXT NOT NULL, sha256 TEXT NOT NULL, artist TEXT NOT NULL, album TEXT NOT NULL, title TEXT NOT NULL, track_number INTEGER NOT NULL, year INTEGER NOT NULL, bitrate INTEGER NOT NULL, status TEXT NOT NULL, last_processed_utc TEXT, error_message TEXT NOT NULL, basic_json TEXT NOT NULL, effective_json TEXT NOT NULL);

CREATE TABLE IF NOT EXISTS processing_history (sequence INTEGER PRIMARY KEY AUTOINCREMENT, file_id TEXT NOT NULL, event_utc TEXT NOT NULL, status TEXT NOT NULL, reason TEXT NOT NULL);

CREATE INDEX IF NOT EXISTS ix_music_status ON music_files(status);

CREATE INDEX IF NOT EXISTS ix_music_sha ON music_files(sha256);

