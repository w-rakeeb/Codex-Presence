use std::collections::HashMap;
use std::fs;
use std::io::{BufRead, BufReader};
use std::path::{Path, PathBuf};
use std::time::{Duration, Instant, SystemTime};

use rusqlite::{Connection, OpenFlags};

#[derive(Debug, Default)]
pub(super) struct ChatTitles {
    titles: HashMap<String, String>,
    stamps: Vec<(PathBuf, u64, Option<SystemTime>)>,
    checked_at: Option<Instant>,
    loaded_at: Option<Instant>,
}

impl ChatTitles {
    pub fn get(&self, id: &str) -> Option<&str> {
        self.titles.get(id).map(String::as_str)
    }

    pub fn refresh(&mut self, home: &Path) {
        if self
            .checked_at
            .is_some_and(|time| time.elapsed() < Duration::from_millis(500))
        {
            return;
        }
        self.checked_at = Some(Instant::now());
        let database = fs::read_dir(home)
            .ok()
            .into_iter()
            .flatten()
            .filter_map(Result::ok)
            .filter_map(|entry| {
                let name = entry.file_name().to_string_lossy().into_owned();
                let version = name
                    .strip_prefix("state_")?
                    .strip_suffix(".sqlite")?
                    .parse::<u32>()
                    .ok()?;
                Some((version, entry.path()))
            })
            .max_by_key(|(version, _)| *version)
            .map(|(_, path)| path);
        let index = home.join("session_index.jsonl");
        let mut paths = vec![index.clone()];
        if let Some(path) = &database {
            paths.push(path.clone());
            paths.push(PathBuf::from(format!("{}-wal", path.display())));
        }
        let stamps = paths
            .into_iter()
            .filter_map(|path| {
                fs::metadata(&path)
                    .ok()
                    .map(|meta| (path, meta.len(), meta.modified().ok()))
            })
            .collect::<Vec<_>>();
        if self.stamps == stamps
            && self
                .loaded_at
                .is_some_and(|time| time.elapsed() < Duration::from_secs(10))
        {
            return;
        }
        let mut titles = HashMap::new();
        if let Ok(file) = fs::File::open(index) {
            for line in BufReader::new(file).lines().map_while(Result::ok) {
                if let Ok(value) = serde_json::from_str::<serde_json::Value>(&line)
                    && let (Some(id), Some(title)) =
                        (value["id"].as_str(), value["thread_name"].as_str())
                    && let Some(title) = clean_title(title)
                {
                    titles.insert(id.to_string(), title);
                }
            }
        }
        if let Some(path) = database {
            let _ = load_database(&path, &mut titles);
        }
        self.titles = titles;
        self.stamps = stamps;
        self.loaded_at = Some(Instant::now());
    }
}

fn clean_title(value: &str) -> Option<String> {
    let value: String = value
        .split_whitespace()
        .collect::<Vec<_>>()
        .join(" ")
        .chars()
        .filter(|ch| !ch.is_control())
        .take(120)
        .collect();
    (!value.is_empty()).then_some(value)
}

fn load_database(path: &Path, titles: &mut HashMap<String, String>) -> rusqlite::Result<()> {
    let connection = Connection::open_with_flags(
        path,
        OpenFlags::SQLITE_OPEN_READ_ONLY | OpenFlags::SQLITE_OPEN_NO_MUTEX,
    )?;
    connection.busy_timeout(Duration::from_millis(50))?;
    let mut columns = connection.prepare("PRAGMA table_info(threads)")?;
    let has_name = columns
        .query_map([], |row| row.get::<_, String>(1))?
        .filter_map(Result::ok)
        .any(|name| name == "name");
    let query = if has_name {
        "SELECT id, COALESCE(NULLIF(name, ''), title) FROM threads"
    } else {
        "SELECT id, title FROM threads"
    };
    let mut statement = connection.prepare(query)?;
    for row in statement
        .query_map([], |row| {
            Ok((row.get::<_, String>(0)?, row.get::<_, String>(1)?))
        })?
        .filter_map(Result::ok)
    {
        if let Some(title) = clean_title(&row.1) {
            titles.insert(row.0, title);
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn current_database_name_and_renames_override_the_index_without_writing() {
        let home = tempfile::tempdir().unwrap();
        fs::write(
            home.path().join("session_index.jsonl"),
            "{\"id\":\"chat\",\"thread_name\":\"Old title\"}\n",
        )
        .unwrap();
        let database = home.path().join("state_5.sqlite");
        let writer = Connection::open(&database).unwrap();
        writer.execute_batch("PRAGMA journal_mode=WAL; CREATE TABLE threads(id TEXT, title TEXT, name TEXT); INSERT INTO threads VALUES('chat', 'Generated title', 'Renamed chat');").unwrap();
        let before = fs::read(&database).unwrap();
        let mut titles = ChatTitles::default();
        titles.refresh(home.path());
        assert_eq!(titles.get("chat"), Some("Renamed chat"));
        assert_eq!(before, fs::read(&database).unwrap());
        writer
            .execute("UPDATE threads SET name='New name' WHERE id='chat'", [])
            .unwrap();
        titles.checked_at = None;
        titles.refresh(home.path());
        assert_eq!(titles.get("chat"), Some("New name"));
    }

    #[test]
    fn index_fallback_uses_the_latest_name_and_ignores_invalid_rows() {
        let home = tempfile::tempdir().unwrap();
        fs::write(home.path().join("session_index.jsonl"), "broken\n{\"id\":\"chat\",\"thread_name\":\"Old\"}\n{\"id\":\"chat\",\"thread_name\":\"  Latest \\n title \"}\n").unwrap();
        let mut titles = ChatTitles::default();
        titles.refresh(home.path());
        assert_eq!(titles.get("chat"), Some("Latest title"));
        assert_eq!(titles.get("missing"), None);
    }
}
