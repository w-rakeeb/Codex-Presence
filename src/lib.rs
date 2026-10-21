pub mod app;
pub mod cli;
pub mod config;
pub mod cost;
pub mod discord;
#[cfg(windows)]
mod discord_transport;
pub mod manual;
pub mod metrics;
pub mod model;
pub mod opencode;
pub mod power;
pub mod process_guard;
pub mod session;
pub mod telemetry;
pub mod ui;
pub mod util;
