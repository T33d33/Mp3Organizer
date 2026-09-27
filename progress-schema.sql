-- Persistent progress, source folders and imported playlists, schema version 3
PRAGMA user_version=3;
CREATE TABLE IF NOT EXISTS music_files (id TEXT PRIMARY KEY, source_root TEXT NOT NULL, original_path TEXT NOT NULL, current_path TEXT NOT NULL COLLATE NOCASE UNIQUE, file_size INTEGER NOT NULL, last_write_time_utc TEXT NOT NULL, sha256 TEXT NOT NULL, artist TEXT NOT NULL, album TEXT NOT NULL, title TEXT NOT NULL, track_number INTEGER NOT NULL, year INTEGER NOT NULL, bitrate INTEGER NOT NULL, status TEXT NOT NULL, last_processed_utc TEXT, error_message TEXT NOT NULL, basic_json TEXT NOT NULL, effective_json TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS processing_history (sequence INTEGER PRIMARY KEY AUTOINCREMENT, file_id TEXT NOT NULL, event_utc TEXT NOT NULL, status TEXT NOT NULL, reason TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS llm_events (sequence INTEGER PRIMARY KEY AUTOINCREMENT, run_id TEXT NOT NULL, event_utc TEXT NOT NULL, kind TEXT NOT NULL, source_path TEXT NOT NULL, detail TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS run_targets (source_root TEXT PRIMARY KEY COLLATE NOCASE, target TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS recognition_evidence (file_id TEXT PRIMARY KEY, recognition_confidence REAL NOT NULL, auto_recognized INTEGER NOT NULL, year_source TEXT NOT NULL, year_confidence REAL NOT NULL, year_enriched INTEGER NOT NULL, candidates_json TEXT NOT NULL, review_reasons_json TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS ix_music_status ON music_files(status);
CREATE INDEX IF NOT EXISTS ix_music_sha ON music_files(sha256);
CREATE TABLE IF NOT EXISTS source_folders (id INTEGER PRIMARY KEY AUTOINCREMENT, source_root TEXT NOT NULL COLLATE NOCASE, original_path TEXT NOT NULL, current_path TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL, UNIQUE(source_root,current_path));
CREATE TABLE IF NOT EXISTS source_occurrences (id TEXT PRIMARY KEY,folder_id INTEGER NOT NULL,file_id TEXT NOT NULL,original_path TEXT NOT NULL,current_path TEXT NOT NULL COLLATE NOCASE,present INTEGER NOT NULL,UNIQUE(folder_id,current_path));
CREATE TABLE IF NOT EXISTS folder_order (folder_id INTEGER NOT NULL,position INTEGER NOT NULL,occurrence_id TEXT NOT NULL,PRIMARY KEY(folder_id,position));
CREATE TABLE IF NOT EXISTS canonical_mappings (file_id TEXT NOT NULL,target TEXT NOT NULL COLLATE NOCASE,relative_path TEXT NOT NULL,source_hash TEXT NOT NULL,target_hash TEXT NOT NULL,size INTEGER NOT NULL,PRIMARY KEY(file_id,target));
CREATE TABLE IF NOT EXISTS metadata_policy_audit (file_id TEXT NOT NULL,policy INTEGER NOT NULL,decision TEXT NOT NULL,previous_json TEXT NOT NULL,event_utc TEXT NOT NULL,PRIMARY KEY(file_id,policy));
CREATE TABLE IF NOT EXISTS source_playlists (id INTEGER PRIMARY KEY AUTOINCREMENT,source_root TEXT NOT NULL COLLATE NOCASE,path TEXT NOT NULL COLLATE NOCASE,name TEXT NOT NULL,present INTEGER NOT NULL,error TEXT NOT NULL,UNIQUE(source_root,path));
CREATE TABLE IF NOT EXISTS source_playlist_entries (playlist_id INTEGER NOT NULL,position INTEGER NOT NULL,original_entry TEXT NOT NULL,file_id TEXT NOT NULL,problem TEXT NOT NULL,result TEXT NOT NULL,target_path TEXT NOT NULL,PRIMARY KEY(playlist_id,position));
