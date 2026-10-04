use std::collections::HashMap;
use std::fs;
use std::io::{BufReader, BufWriter, Write};
use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};

use super::CachedInactiveUsage;

const VERSION: u8 = 1;
const MAX_BYTES: u64 = 8 * 1024 * 1024;
const MAX_ENTRIES: usize = 10_000;

#[derive(Debug, Default)]
pub(super) struct HistoryCache {
    initialized: bool,
    path: Option<PathBuf>,
    roots: Vec<PathBuf>,
    pub changed: bool,
}

#[derive(Serialize, Deserialize)]
struct HistoryFile {
    version: u8,
    roots: Vec<PathBuf>,
    entries: HashMap<PathBuf, CachedInactiveUsage>,
}

impl HistoryCache {
    pub fn initialize(
        &mut self,
        roots: &[PathBuf],
        entries: &mut HashMap<PathBuf, CachedInactiveUsage>,
    ) {
        if self.initialized {
            return;
        }
        self.initialized = true;
        let home = crate::config::codex_home();
        if roots.contains(&home.join("sessions")) {
            self.load(
                home.join("discord-presence-history-cache.json"),
                roots,
                entries,
            );
        }
    }

    fn load(
        &mut self,
        path: PathBuf,
        roots: &[PathBuf],
        entries: &mut HashMap<PathBuf, CachedInactiveUsage>,
    ) {
        self.path = Some(path.clone());
        self.roots = roots.to_vec();
        let loaded = (|| {
            let file = fs::File::open(&path).ok()?;
            if file.metadata().ok()?.len() > MAX_BYTES {
                return None;
            }
            let cache: HistoryFile = serde_json::from_reader(BufReader::new(file)).ok()?;
            (cache.version == VERSION && cache.roots == roots && cache.entries.len() <= MAX_ENTRIES)
                .then_some(cache.entries)
        })();
        if let Some(loaded) = loaded {
            entries.extend(loaded.into_iter().filter(|(path, entry)| {
                roots.iter().any(|root| path.starts_with(root))
                    && entry
                        .snapshot
                        .as_ref()
                        .is_none_or(|snapshot| snapshot.source_file == *path)
            }));
        }
    }

    pub fn save(&mut self, entries: &HashMap<PathBuf, CachedInactiveUsage>) {
        let Some(path) = self.path.as_ref().filter(|_| self.changed) else {
            return;
        };
        if entries.len() > MAX_ENTRIES {
            return;
        }
        let saved = (|| -> anyhow::Result<()> {
            let mut temporary =
                tempfile::NamedTempFile::new_in(path.parent().unwrap_or_else(|| Path::new(".")))?;
            {
                let mut writer = BufWriter::new(temporary.as_file_mut());
                serde_json::to_writer(
                    &mut writer,
                    &HistoryFile {
                        version: VERSION,
                        roots: self.roots.clone(),
                        entries: entries.clone(),
                    },
                )?;
                writer.flush()?;
            }
            if temporary.as_file().metadata()?.len() > MAX_BYTES {
                return Ok(());
            }
            temporary.persist(path)?;
            Ok(())
        })();
        if saved.is_ok() {
            self.changed = false;
        }
    }
}

#[cfg(test)]
mod tests {
    use std::time::{Duration, SystemTime};

    use super::*;
    use crate::config::PricingConfig;
    use crate::session::{GitBranchCache, SessionParseCache, collect_active_sessions};

    #[test]
    fn cached_usage_survives_restart_and_changed_sources_are_reparsed() {
        let home = tempfile::TempDir::new().unwrap();
        let sessions = home.path().join("sessions");
        fs::create_dir(&sessions).unwrap();
        let source = sessions.join("old.jsonl");
        let write_source = |used: u8| {
            fs::write(&source, format!("{{\"type\":\"session_meta\",\"payload\":{{\"id\":\"old\",\"cwd\":\".\"}}}}\n{{\"type\":\"event_msg\",\"payload\":{{\"type\":\"token_count\",\"rate_limits\":{{\"limit_id\":\"codex\",\"primary\":{{\"used_percent\":{used},\"window_minutes\":300}}}}}}}}\n")).unwrap();
            fs::OpenOptions::new()
                .write(true)
                .open(&source)
                .unwrap()
                .set_modified(SystemTime::now() - Duration::from_secs(7200))
                .unwrap();
        };
        write_source(25);
        let cache_path = home.path().join("history.json");
        let roots = vec![sessions.clone()];
        let mut first = SessionParseCache::default();
        first
            .history
            .load(cache_path.clone(), &roots, &mut first.inactive_usage);
        first.history.initialized = true;
        let collect = |cache: &mut SessionParseCache| {
            collect_active_sessions(
                &sessions,
                Duration::from_secs(90),
                Duration::from_secs(3600),
                &mut GitBranchCache::new(Duration::from_secs(30)),
                cache,
                &PricingConfig::default(),
            )
            .unwrap()
        };
        assert!(collect(&mut first).is_empty());
        let before = fs::read(&cache_path).unwrap();
        let mut restarted = SessionParseCache::default();
        restarted
            .history
            .load(cache_path.clone(), &roots, &mut restarted.inactive_usage);
        restarted.history.initialized = true;
        assert_eq!(restarted.inactive_usage.len(), 1);
        assert!(collect(&mut restarted).is_empty());
        assert_eq!(fs::read(&cache_path).unwrap(), before);
        assert_eq!(
            restarted
                .latest_limits_source()
                .unwrap()
                .limits
                .primary()
                .unwrap()
                .remaining_percent,
            75.0
        );
        write_source(50);
        assert!(collect(&mut restarted).is_empty());
        assert_eq!(
            restarted
                .latest_limits_source()
                .unwrap()
                .limits
                .primary()
                .unwrap()
                .remaining_percent,
            50.0
        );
        fs::remove_file(&source).unwrap();
        assert!(collect(&mut restarted).is_empty());
        assert!(restarted.inactive_usage.is_empty());
        let mut pruned = HistoryCache::default();
        let mut entries = HashMap::new();
        pruned.load(cache_path, &roots, &mut entries);
        assert!(entries.is_empty());
    }

    #[test]
    fn invalid_or_wrong_root_caches_are_ignored() {
        let home = tempfile::TempDir::new().unwrap();
        let cache_path = home.path().join("history.json");
        let roots = vec![home.path().join("sessions")];
        let mut cache = HistoryCache::default();
        let mut entries = HashMap::new();
        fs::write(&cache_path, b"broken").unwrap();
        cache.load(cache_path.clone(), &roots, &mut entries);
        assert!(entries.is_empty());
        fs::write(
            &cache_path,
            serde_json::to_vec(&HistoryFile {
                version: VERSION,
                roots: vec![home.path().join("other")],
                entries: HashMap::new(),
            })
            .unwrap(),
        )
        .unwrap();
        cache.load(cache_path.clone(), &roots, &mut entries);
        assert!(entries.is_empty());
        fs::File::create(&cache_path)
            .unwrap()
            .set_len(MAX_BYTES + 1)
            .unwrap();
        cache.load(cache_path, &roots, &mut entries);
        assert!(entries.is_empty());
    }
}
