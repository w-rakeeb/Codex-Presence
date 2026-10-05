use std::fs::File;
use std::io::{BufRead, BufReader, Read, Seek, SeekFrom};
use std::path::Path;
use std::time::{Duration, Instant, SystemTime};

use anyhow::{Context, Result};
use chrono::{DateTime, Utc};
use serde_json::Value;

use crate::config::PricingConfig;
use crate::model;

use super::activity::SessionAccumulator;
use super::{
    CachedSessionEntry, CodexSessionSnapshot, ContextWindowSnapshot, GitBranchCache,
    ReasoningEffort, SessionParseCache,
};

#[cfg(test)]
pub(super) fn parse_session_file(
    jsonl_path: &Path,
    last_activity: SystemTime,
    git_cache: &mut GitBranchCache,
    pricing_config: &PricingConfig,
) -> Result<Option<CodexSessionSnapshot>> {
    let file = File::open(jsonl_path)
        .with_context(|| format!("failed to open session file {}", jsonl_path.display()))?;
    let mut reader = BufReader::new(file);
    let mut accumulator = SessionAccumulator::default();
    let mut partial_line_buffer = String::new();
    parse_new_lines(&mut reader, &mut accumulator, &mut partial_line_buffer)?;
    Ok(accumulator.build_snapshot(jsonl_path, last_activity, git_cache, pricing_config))
}

pub(super) fn parse_session_file_cached(
    jsonl_path: &Path,
    metadata: &std::fs::Metadata,
    last_activity: SystemTime,
    git_cache: &mut GitBranchCache,
    parse_cache: &mut SessionParseCache,
    pricing_config: &PricingConfig,
) -> Result<Option<CodexSessionSnapshot>> {
    let modified = metadata.modified().unwrap_or(last_activity);
    let file_len = metadata.len();
    let key = jsonl_path.to_path_buf();
    let cached = parse_cache
        .entries
        .entry(key)
        .or_insert_with(|| CachedSessionEntry::new(modified));

    let should_reset = cached.cursor > file_len
        || modified < cached.modified
        || (modified != cached.modified && file_len <= cached.file_len);
    if should_reset {
        cached.reset(modified);
    }

    if cached.file_len == file_len
        && cached.modified == modified
        && let Some(snapshot) = cached.snapshot.clone()
    {
        return Ok(Some(snapshot));
    }

    let mut file = File::open(jsonl_path)
        .with_context(|| format!("failed to open session file {}", jsonl_path.display()))?;
    let head_len = if cached.head.is_empty() {
        file_len.min(512) as usize
    } else {
        cached.head.len().min(file_len.min(512) as usize)
    };
    let mut head = vec![0; head_len];
    file.read_exact(&mut head)?;
    let mut tail = vec![0; cached.tail.len()];
    if !tail.is_empty() {
        file.seek(SeekFrom::Start(
            cached.cursor.saturating_sub(tail.len() as u64),
        ))?;
        file.read_exact(&mut tail)?;
    }
    if (!cached.head.is_empty() && head != cached.head) || tail != cached.tail {
        cached.reset(modified);
    }
    cached.head = head;
    file.seek(SeekFrom::Start(cached.cursor))
        .with_context(|| format!("failed to seek session file {}", jsonl_path.display()))?;
    let mut reader = BufReader::new(file);
    parse_new_lines(
        &mut reader,
        &mut cached.accumulator,
        &mut cached.partial_line_buffer,
    )?;
    cached.cursor = reader.stream_position().unwrap_or(file_len);
    let tail_len = cached.cursor.min(512) as usize;
    cached.tail.resize(tail_len, 0);
    reader.seek(SeekFrom::Start(cached.cursor - tail_len as u64))?;
    reader.read_exact(&mut cached.tail)?;
    cached.file_len = file_len;
    cached.modified = modified;

    let snapshot =
        cached
            .accumulator
            .build_snapshot(jsonl_path, last_activity, git_cache, pricing_config);
    cached.snapshot = snapshot.clone();
    Ok(snapshot)
}

pub(super) fn parse_inactive_usage(
    path: &Path,
    file_len: u64,
    modified: SystemTime,
    git_cache: &mut GitBranchCache,
    pricing: &PricingConfig,
) -> Result<Option<CodexSessionSnapshot>> {
    let mut file = File::open(path)?;
    let mut header = Vec::new();
    (&mut file).take(65_536).read_to_end(&mut header)?;
    let header = String::from_utf8_lossy(&header);
    let metadata = header
        .lines()
        .next()
        .and_then(|line| serde_json::from_str::<Value>(line).ok())
        .filter(|value| value["type"] == "session_meta");
    let mut size = 262_144_u64.min(file_len);
    loop {
        let offset = file_len.saturating_sub(size);
        file.seek(SeekFrom::Start(offset))?;
        let mut bytes = Vec::new();
        file.read_to_end(&mut bytes)?;
        let text = String::from_utf8_lossy(&bytes);
        let mut accumulator = SessionAccumulator::default();
        if let Some(metadata) = &metadata {
            accumulator.apply_event(metadata);
        }
        for line in text.lines().skip(usize::from(offset > 0)) {
            if line.contains("\"rate_limits\"")
                && let Ok(event) = serde_json::from_str::<Value>(line)
                && !event["payload"]["rate_limits"].is_null()
            {
                accumulator.apply_event(&event);
            }
        }
        let snapshot = accumulator.build_usage_snapshot(path, modified, git_cache, pricing);
        let global = snapshot.as_ref().is_some_and(|snapshot| {
            snapshot
                .rate_limit_envelopes
                .iter()
                .any(|envelope| envelope.scope == super::RateLimitScope::GlobalAccount)
        });
        if !global && offset > 0 && size >= 4_194_304 {
            file.seek(SeekFrom::Start(0))?;
            accumulator = SessionAccumulator::default();
            if let Some(metadata) = &metadata {
                accumulator.apply_event(metadata);
            }
            let mut reader = BufReader::new(&mut file);
            let mut line = String::new();
            loop {
                line.clear();
                if reader.read_line(&mut line)? == 0 {
                    break;
                }
                if line.contains("\"rate_limits\"")
                    && let Ok(event) = serde_json::from_str::<Value>(&line)
                    && !event["payload"]["rate_limits"].is_null()
                {
                    accumulator.apply_event(&event);
                }
            }
            return Ok(accumulator.build_usage_snapshot(path, modified, git_cache, pricing));
        }
        if global || offset == 0 {
            return Ok(snapshot);
        }
        size = size.saturating_mul(4).min(file_len).min(4_194_304);
    }
}

pub(super) fn parse_new_lines(
    reader: &mut BufReader<File>,
    accumulator: &mut SessionAccumulator,
    partial_line_buffer: &mut String,
) -> Result<()> {
    let mut line = String::new();
    loop {
        line.clear();
        let bytes = reader.read_line(&mut line)?;
        if bytes == 0 {
            break;
        }

        let line_has_terminator = line.ends_with('\n');
        let combined = if partial_line_buffer.is_empty() {
            line.to_string()
        } else {
            let mut pending = std::mem::take(partial_line_buffer);
            pending.push_str(&line);
            pending
        };

        let trimmed = combined.trim();
        if trimmed.is_empty() {
            continue;
        }
        match serde_json::from_str::<Value>(trimmed) {
            Ok(parsed) => accumulator.apply_event(&parsed),
            Err(_) if !line_has_terminator => {
                partial_line_buffer.push_str(&combined);
                break;
            }
            Err(_) => continue,
        }
    }
    Ok(())
}

pub(super) fn compute_session_delta(
    latest_total: Option<u64>,
    previous_total: Option<u64>,
    fallback_last_turn: Option<u64>,
) -> Option<u64> {
    match (latest_total, previous_total) {
        (Some(latest), Some(previous)) => Some(latest.saturating_sub(previous)),
        _ => fallback_last_turn,
    }
}

pub(super) fn total_tokens_from_info(payload: &Value) -> Option<u64> {
    uint_at(payload, &["info", "total_token_usage", "total_tokens"])
}

pub(super) fn last_tokens_from_info(payload: &Value) -> Option<u64> {
    uint_at(payload, &["info", "last_token_usage", "total_tokens"])
}

pub(super) fn total_input_tokens_from_info(payload: &Value) -> Option<u64> {
    uint_at(payload, &["info", "total_token_usage", "input_tokens"])
}

pub(super) fn total_cached_input_tokens_from_info(payload: &Value) -> Option<u64> {
    uint_at(
        payload,
        &["info", "total_token_usage", "cached_input_tokens"],
    )
}

pub(super) fn total_output_tokens_from_info(payload: &Value) -> Option<u64> {
    uint_at(payload, &["info", "total_token_usage", "output_tokens"])
}

pub(super) fn model_context_window_from_info(payload: &Value) -> Option<u64> {
    uint_at(payload, &["info", "model_context_window"])
}

pub(super) fn turn_context_reasoning_effort(payload: &Value) -> Option<ReasoningEffort> {
    if let Some(raw) = str_at(payload, &["effort"]) {
        return ReasoningEffort::parse(Some(raw.as_str()));
    }
    let nested = str_at(
        payload,
        &["collaboration_mode", "settings", "reasoning_effort"],
    );
    ReasoningEffort::parse(nested.as_deref())
}

pub(super) fn last_input_tokens_from_info(payload: &Value) -> Option<u64> {
    uint_at(payload, &["info", "last_token_usage", "input_tokens"])
}

pub(super) fn last_cached_input_tokens_from_info(payload: &Value) -> Option<u64> {
    uint_at(
        payload,
        &["info", "last_token_usage", "cached_input_tokens"],
    )
}

pub(super) fn last_output_tokens_from_info(payload: &Value) -> Option<u64> {
    uint_at(payload, &["info", "last_token_usage", "output_tokens"])
}

pub(super) fn build_context_window_snapshot(
    model_id: Option<&str>,
    event_window_tokens: Option<u64>,
    last_turn_tokens: Option<u64>,
    session_total_tokens: Option<u64>,
) -> Option<ContextWindowSnapshot> {
    #[cfg(not(test))]
    let resolved = model::resolve_context_window(model_id.unwrap_or(""), event_window_tokens)?;
    #[cfg(test)]
    let resolved = model::resolve_context_window_from_cache_path(
        model_id.unwrap_or(""),
        event_window_tokens,
        std::path::Path::new("__codex_presence_no_models_cache__.json"),
    )?;
    let window_tokens = resolved.effective_tokens;
    if window_tokens == 0 {
        return None;
    }
    // Context usage must track active-turn usage first; session totals are cumulative and can
    // greatly exceed context windows in long sessions.
    let used_tokens = if let Some(last_turn_tokens) = last_turn_tokens {
        last_turn_tokens
    } else {
        session_total_tokens.filter(|tokens| *tokens <= window_tokens)?
    }
    .min(window_tokens);

    let remaining_tokens = window_tokens.saturating_sub(used_tokens);
    let remaining_percent =
        ((remaining_tokens as f64 / window_tokens as f64) * 100.0).clamp(0.0, 100.0);
    Some(ContextWindowSnapshot {
        raw_window_tokens: resolved.raw_tokens,
        window_tokens,
        effective_percent: resolved.effective_percent,
        used_tokens,
        remaining_tokens,
        remaining_percent,
        source: resolved.source,
        raw_source: resolved.raw_source,
    })
}

pub(super) fn max_datetime(
    left: Option<DateTime<Utc>>,
    right: Option<DateTime<Utc>>,
) -> Option<DateTime<Utc>> {
    match (left, right) {
        (Some(a), Some(b)) => Some(if a >= b { a } else { b }),
        (Some(a), None) => Some(a),
        (None, Some(b)) => Some(b),
        (None, None) => None,
    }
}

pub(super) fn parse_utc_timestamp(text: String) -> Option<DateTime<Utc>> {
    DateTime::parse_from_rfc3339(&text)
        .map(|dt| dt.with_timezone(&Utc))
        .ok()
}

pub(super) fn fetch_git_branch(project_path: &Path) -> Option<String> {
    let repository = std::env::var_os("GIT_DIR").is_some()
        || project_path
            .ancestors()
            .any(|path| path.join(".git").exists())
        || (project_path.join("HEAD").is_file() && project_path.join("objects").is_dir());
    if !repository {
        return None;
    }
    let output = git_query(project_path, &["--abbrev-ref", "HEAD"])?;

    if !output.status.success() {
        return None;
    }

    let branch = String::from_utf8_lossy(&output.stdout).trim().to_string();
    if branch == "HEAD" {
        let output = git_query(project_path, &["--short", "HEAD"])?;
        if !output.status.success() {
            return None;
        }
        let short = String::from_utf8_lossy(&output.stdout).trim().to_string();
        return (!short.is_empty()).then_some(short);
    }
    (!branch.is_empty()).then_some(branch)
}

fn git_query(project_path: &Path, arguments: &[&str]) -> Option<std::process::Output> {
    use std::process::Stdio;
    let mut child = crate::util::silent_command("git")
        .arg("-C")
        .arg(project_path)
        .arg("rev-parse")
        .args(arguments)
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::null())
        .spawn()
        .ok()?;
    let deadline = Instant::now() + Duration::from_millis(500);
    loop {
        match child.try_wait() {
            Ok(Some(_)) => return child.wait_with_output().ok(),
            Ok(None) if Instant::now() < deadline => std::thread::sleep(Duration::from_millis(10)),
            _ => {
                let _ = child.kill();
                let _ = child.wait();
                return None;
            }
        }
    }
}

pub(super) fn str_at(value: &Value, path: &[&str]) -> Option<String> {
    let mut cursor = value;
    for key in path {
        cursor = cursor.get(*key)?;
    }
    cursor.as_str().map(|s| s.to_string())
}

pub(super) fn uint_at(value: &Value, path: &[&str]) -> Option<u64> {
    let mut cursor = value;
    for key in path {
        cursor = cursor.get(*key)?;
    }
    cursor
        .as_u64()
        .or_else(|| cursor.as_i64().and_then(|n| (n >= 0).then_some(n as u64)))
}
