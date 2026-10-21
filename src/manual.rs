use chrono::{Local, TimeZone, Timelike};
use serde::{Deserialize, Serialize};

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(deny_unknown_fields)]
pub struct ManualProfile {
    #[serde(default)]
    pub source: String,
    #[serde(default)]
    pub session_id: Option<String>,
    pub details: String,
    pub state: String,
    pub timestamp_mode: String,
    pub start_epoch: Option<i64>,
    pub end_epoch: Option<i64>,
}

impl ManualProfile {
    pub fn validate(&self, now: i64) -> Result<(), String> {
        if !matches!(self.source.as_str(), "" | "custom" | "recent") {
            return Err("Choose custom text or a recent Codex session.".into());
        }
        if self.source == "recent"
            && self
                .session_id
                .as_ref()
                .is_some_and(|id| id.len() > 256 || id.chars().any(char::is_control))
        {
            return Err("Choose a valid recent session.".into());
        }
        if self.source != "recent"
            && (self.details.trim().is_empty()
                || self.details.chars().count() > 128
                || self.state.chars().count() > 128
                || self
                    .details
                    .chars()
                    .chain(self.state.chars())
                    .any(char::is_control))
        {
            return Err("Use a nonempty activity line and at most 128 characters per line, without line breaks.".into());
        }
        if !matches!(
            self.timestamp_mode.as_str(),
            "connection" | "update" | "session" | "local" | "custom"
        ) {
            return Err("Choose a valid timestamp option.".into());
        }
        if self.timestamp_mode == "custom" {
            let start = self
                .start_epoch
                .filter(|value| *value > 0 && *value <= now)
                .ok_or("Choose a start time in the past or present.")?;
            if self
                .end_epoch
                .is_some_and(|end| end <= start || end <= now || end > 253402300799)
            {
                return Err("The end time must be later than the start time and now.".into());
            }
        }
        Ok(())
    }
}

pub struct ManualSession {
    pub profile: ManualProfile,
    pub started_at: i64,
    pub updated_at: i64,
}

impl ManualSession {
    pub fn timestamps(&self, connected_at: Option<i64>, now: i64) -> (Option<i64>, Option<i64>) {
        let start = match self.profile.timestamp_mode.as_str() {
            "connection" => connected_at,
            "update" => Some(self.updated_at),
            "session" => Some(self.started_at),
            "local" => Local
                .timestamp_opt(now, 0)
                .single()
                .map(|time| now - i64::from(time.num_seconds_from_midnight())),
            "custom" => self.profile.start_epoch,
            _ => None,
        };
        (
            start,
            if self.profile.timestamp_mode == "custom" {
                self.profile.end_epoch
            } else {
                None
            },
        )
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    fn profile(mode: &str) -> ManualProfile {
        ManualProfile {
            source: String::new(),
            session_id: None,
            details: "Thinking · Project".into(),
            state: "GPT model · High".into(),
            timestamp_mode: mode.into(),
            start_epoch: Some(100),
            end_epoch: None,
        }
    }
    #[test]
    fn legacy_profiles_remain_custom_and_recent_requires_a_valid_source() {
        let legacy = serde_json::json!({ "details": "Existing text", "state": "", "timestamp_mode": "session", "start_epoch": null, "end_epoch": null });
        let mut value: ManualProfile = serde_json::from_value(legacy).unwrap();
        assert_eq!(value.source, "");
        assert!(value.validate(200).is_ok());
        value.source = "recent".into();
        value.details.clear();
        assert!(value.validate(200).is_ok());
        value.session_id = Some("invalid\nidentifier".into());
        assert!(value.validate(200).is_err());
        value.source = "unknown".into();
        assert!(value.validate(200).is_err());
    }

    #[test]
    fn validates_text_and_timer_boundaries() {
        assert!(profile("custom").validate(200).is_ok());
        let mut value = profile("custom");
        value.start_epoch = Some(201);
        assert!(value.validate(200).is_err());
        value.start_epoch = Some(100);
        value.end_epoch = Some(200);
        assert!(value.validate(200).is_err());
        value.end_epoch = Some(300);
        assert!(value.validate(200).is_ok());
        value.details = "bad\nline".into();
        assert!(value.validate(200).is_err());
        value.details = "x".repeat(129);
        assert!(value.validate(200).is_err());
        assert!(profile("unknown").validate(200).is_err());
    }
    #[test]
    fn anchors_are_independent_and_connection_waits_for_discord() {
        let mut session = ManualSession {
            profile: profile("connection"),
            started_at: 100,
            updated_at: 150,
        };
        assert_eq!(session.timestamps(None, 200), (None, None));
        assert_eq!(session.timestamps(Some(180), 200), (Some(180), None));
        session.profile.timestamp_mode = "session".into();
        assert_eq!(session.timestamps(Some(180), 200), (Some(100), None));
        session.profile.timestamp_mode = "update".into();
        assert_eq!(session.timestamps(Some(180), 200), (Some(150), None));
        session.profile.timestamp_mode = "custom".into();
        session.profile.end_epoch = Some(300);
        assert_eq!(session.timestamps(None, 200), (Some(100), Some(300)));
    }
    #[test]
    fn local_clock_uses_local_midnight() {
        let session = ManualSession {
            profile: profile("local"),
            started_at: 100,
            updated_at: 150,
        };
        let now = 1800000000;
        let expected = i64::from(
            Local
                .timestamp_opt(now, 0)
                .unwrap()
                .num_seconds_from_midnight(),
        );
        assert_eq!(now - session.timestamps(None, now).0.unwrap(), expected);
    }
}
