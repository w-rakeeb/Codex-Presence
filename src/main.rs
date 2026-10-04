use std::process::ExitCode;

use anyhow::Result;
use clap::Parser;

use codex_discord_presence::app::{self, AppMode};
use codex_discord_presence::cli::{Cli, Commands};
use codex_discord_presence::config::{self, PresenceConfig};
use codex_discord_presence::process_guard;
use codex_discord_presence::util::setup_tracing;

fn main() -> ExitCode {
    codex_discord_presence::power::apply_from_env("CODEX_PRESENCE_EFFICIENCY_MODE");
    match run() {
        Ok(code) => ExitCode::from(code),
        Err(err) => {
            eprintln!("codex-discord-presence error: {err:#}");
            ExitCode::from(1)
        }
    }
}

fn run() -> Result<u8> {
    setup_tracing();
    let cli = Cli::parse();
    match &cli.command {
        Some(Commands::ConfigGet) => {
            let path = config::config_path();
            let mut value = if path.exists() {
                serde_json::from_str::<PresenceConfig>(&std::fs::read_to_string(path)?)?
            } else {
                PresenceConfig::default()
            };
            value.normalize_for_runtime();
            println!("{}", serde_json::to_string(&value)?);
            return Ok(0);
        }
        Some(Commands::ConfigCheck) => {
            let mut value: PresenceConfig = serde_json::from_reader(std::io::stdin())?;
            value.normalize_for_runtime();
            println!("{}", serde_json::to_string(&value)?);
            return Ok(0);
        }
        _ => {}
    }
    let config = PresenceConfig::load_or_init()?;

    match cli.command {
        Some(Commands::DesktopBridge { observe }) => {
            let _guard = match process_guard::acquire_single_instance()? {
                process_guard::AcquireState::Acquired(guard) => guard,
                process_guard::AcquireState::AlreadyRunning { pid } => {
                    anyhow::bail!(
                        "Presence is already running (PID {pid:?}). Close that instance first."
                    );
                }
            };
            app::run_desktop_bridge(config, config::runtime_settings(), observe)?;
            Ok(0)
        }
        Some(Commands::ConfigGet | Commands::ConfigCheck) => unreachable!(),
        Some(Commands::TerminalView) => {
            let _guard = match process_guard::acquire_single_instance()? {
                process_guard::AcquireState::Acquired(guard) => guard,
                process_guard::AcquireState::AlreadyRunning { pid } => anyhow::bail!(
                    "Presence is already running (PID {pid:?}). Stop it before opening the terminal view."
                ),
            };
            app::run(config, AppMode::SmartForeground, config::runtime_settings())?;
            Ok(0)
        }
        Some(Commands::Status) => {
            app::print_status(&config)?;
            Ok(0)
        }
        Some(Commands::Doctor) => app::doctor(&config),
        Some(Commands::DiscordProof { output }) => {
            app::run_discord_proof(&config, output.as_deref())?;
            Ok(0)
        }
        Some(Commands::Codex { args }) => {
            let acquired = process_guard::acquire_or_takeover_single_instance()?;
            if let Some(pid) = acquired.takeover_pid {
                println!("Existing instance detected (PID {pid}); takeover completed.");
            }
            let _guard = acquired.guard;
            let runtime = config::runtime_settings();
            app::run(config, AppMode::CodexChild { args }, runtime)?;
            Ok(0)
        }
        None => {
            let acquired = process_guard::acquire_or_takeover_single_instance()?;
            if let Some(pid) = acquired.takeover_pid {
                println!("Existing instance detected (PID {pid}); takeover completed.");
            }
            let _guard = acquired.guard;
            let runtime = config::runtime_settings();
            app::run(config, AppMode::SmartForeground, runtime)?;
            Ok(0)
        }
    }
}
