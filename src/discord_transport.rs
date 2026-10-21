use anyhow::{Context, Result, anyhow, bail};
use discord_rich_presence::activity::Activity;
use serde_json::{Value, json};
use std::ffi::c_void;
use std::fs::{File, OpenOptions};
use std::io;
use std::os::windows::fs::OpenOptionsExt;
use std::os::windows::io::{AsRawHandle, FromRawHandle, OwnedHandle};
use std::sync::atomic::{AtomicU64, Ordering};
use std::time::{Duration, Instant};

const IO_TIMEOUT: Duration = Duration::from_secs(2);
const MAX_FRAME_SIZE: usize = 1024 * 1024;
static NONCE: AtomicU64 = AtomicU64::new(0);

#[repr(C)]
struct Overlapped {
    internal: usize,
    internal_high: usize,
    offset: u32,
    offset_high: u32,
    event: *mut c_void,
}

#[link(name = "kernel32")]
unsafe extern "system" {
    fn CreateEventW(
        attributes: *const c_void,
        manual: i32,
        initial: i32,
        name: *const u16,
    ) -> *mut c_void;
    fn ReadFile(
        handle: *mut c_void,
        buffer: *mut c_void,
        length: u32,
        transferred: *mut u32,
        overlapped: *mut Overlapped,
    ) -> i32;
    fn WriteFile(
        handle: *mut c_void,
        buffer: *const c_void,
        length: u32,
        transferred: *mut u32,
        overlapped: *mut Overlapped,
    ) -> i32;
    fn WaitForSingleObject(handle: *mut c_void, milliseconds: u32) -> u32;
    fn GetOverlappedResult(
        handle: *mut c_void,
        overlapped: *mut Overlapped,
        transferred: *mut u32,
        wait: i32,
    ) -> i32;
    fn CancelIoEx(handle: *mut c_void, overlapped: *mut Overlapped) -> i32;
}

pub(crate) struct DiscordIpcClient {
    client_id: String,
    socket: Option<File>,
}

impl DiscordIpcClient {
    pub(crate) fn new(id: impl AsRef<str>) -> Self {
        Self {
            client_id: id.as_ref().to_string(),
            socket: None,
        }
    }

    pub(crate) fn connect(&mut self) -> Result<()> {
        for index in 0..10 {
            if let Ok(socket) = OpenOptions::new()
                .read(true)
                .write(true)
                .custom_flags(0x40000000)
                .open(format!(r"\\.\pipe\discord-ipc-{index}"))
            {
                self.socket = Some(socket);
                return self.handshake();
            }
        }
        bail!("Discord desktop is not available")
    }

    fn handshake(&mut self) -> Result<()> {
        self.send(json!({"v":1,"client_id":self.client_id}), 0)?;
        let (opcode, reply) = self.recv()?;
        if opcode != 1 || reply["evt"] != "READY" {
            bail!(
                "Discord rejected the application: {}",
                response_message(&reply)
            );
        }
        Ok(())
    }

    fn transfer(&self, buffer: &mut [u8], writing: bool) -> Result<()> {
        let socket = self
            .socket
            .as_ref()
            .ok_or_else(|| anyhow!("Discord is not connected"))?;
        let deadline = Instant::now() + IO_TIMEOUT;
        let mut offset = 0;
        while offset < buffer.len() {
            let event = unsafe { CreateEventW(std::ptr::null(), 1, 0, std::ptr::null()) };
            if event.is_null() {
                return Err(io::Error::last_os_error().into());
            }
            let event = unsafe { OwnedHandle::from_raw_handle(event) };
            let mut operation = Overlapped {
                internal: 0,
                internal_high: 0,
                offset: 0,
                offset_high: 0,
                event: event.as_raw_handle(),
            };
            let mut transferred = 0;
            let data = &mut buffer[offset..];
            let result = unsafe {
                if writing {
                    WriteFile(
                        socket.as_raw_handle(),
                        data.as_ptr().cast(),
                        data.len() as u32,
                        &mut transferred,
                        &mut operation,
                    )
                } else {
                    ReadFile(
                        socket.as_raw_handle(),
                        data.as_mut_ptr().cast(),
                        data.len() as u32,
                        &mut transferred,
                        &mut operation,
                    )
                }
            };
            if result == 0 {
                let error = io::Error::last_os_error();
                if error.raw_os_error() != Some(997) {
                    return Err(error.into());
                }
                let remaining = deadline.saturating_duration_since(Instant::now());
                let wait = unsafe {
                    WaitForSingleObject(
                        event.as_raw_handle(),
                        remaining.as_millis().min(u128::from(u32::MAX)) as u32,
                    )
                };
                if wait != 0 {
                    unsafe {
                        CancelIoEx(socket.as_raw_handle(), &mut operation);
                        GetOverlappedResult(
                            socket.as_raw_handle(),
                            &mut operation,
                            &mut transferred,
                            1,
                        );
                    }
                    bail!("Discord did not respond within two seconds");
                }
                if unsafe {
                    GetOverlappedResult(socket.as_raw_handle(), &mut operation, &mut transferred, 0)
                } == 0
                {
                    return Err(io::Error::last_os_error().into());
                }
            }
            if result != 0
                && unsafe {
                    GetOverlappedResult(socket.as_raw_handle(), &mut operation, &mut transferred, 0)
                } == 0
            {
                return Err(io::Error::last_os_error().into());
            }
            if transferred == 0 {
                bail!("Discord closed the connection");
            }
            offset += transferred as usize;
            if offset < buffer.len() && Instant::now() >= deadline {
                bail!("Discord transfer timed out");
            }
        }
        Ok(())
    }

    pub(crate) fn send(&mut self, value: Value, opcode: u8) -> Result<()> {
        let data = serde_json::to_vec(&value)?;
        if data.len() > MAX_FRAME_SIZE {
            bail!("Discord activity frame is too large");
        }
        let mut packet = Vec::with_capacity(data.len() + 8);
        packet.extend_from_slice(&u32::from(opcode).to_le_bytes());
        packet.extend_from_slice(&(data.len() as u32).to_le_bytes());
        packet.extend_from_slice(&data);
        self.transfer(&mut packet, true)
    }

    pub(crate) fn recv(&mut self) -> Result<(u32, Value)> {
        let mut header = [0_u8; 8];
        self.transfer(&mut header, false)?;
        let opcode = u32::from_le_bytes(header[..4].try_into()?);
        let length = u32::from_le_bytes(header[4..].try_into()?) as usize;
        if length > MAX_FRAME_SIZE {
            bail!("Discord reply frame is too large");
        }
        let mut data = vec![0; length];
        self.transfer(&mut data, false)?;
        Ok((
            opcode,
            serde_json::from_slice(&data).context("Discord reply could not be read")?,
        ))
    }

    fn publish(&mut self, activity: Value) -> Result<()> {
        let nonce = format!(
            "presence-{}-{}",
            std::process::id(),
            NONCE.fetch_add(1, Ordering::Relaxed)
        );
        self.send(json!({"cmd":"SET_ACTIVITY","args":{"pid":std::process::id(),"activity":activity},"nonce":nonce}), 1)?;
        let deadline = Instant::now() + IO_TIMEOUT;
        for _ in 0..16 {
            let (opcode, reply) = self.recv()?;
            if opcode == 3 {
                self.send(reply, 4)?;
            } else if opcode == 2 {
                bail!(
                    "Discord closed the connection: {}",
                    response_message(&reply)
                );
            } else if reply["nonce"].as_str() == Some(nonce.as_str()) {
                return validate_reply(opcode, &reply, &nonce);
            }
            if Instant::now() >= deadline {
                break;
            }
        }
        bail!("Discord did not acknowledge the activity")
    }

    pub(crate) fn set_activity(&mut self, activity: Activity<'_>) -> Result<()> {
        self.publish(serde_json::to_value(activity)?)
    }
    pub(crate) fn clear_activity(&mut self) -> Result<()> {
        self.publish(Value::Null)
    }
    pub(crate) fn close(&mut self) -> Result<()> {
        self.socket = None;
        Ok(())
    }
}

fn response_message(reply: &Value) -> &str {
    reply["data"]["message"]
        .as_str()
        .or_else(|| reply["message"].as_str())
        .unwrap_or("unrecognized response")
}

fn validate_reply(opcode: u32, reply: &Value, nonce: &str) -> Result<()> {
    if opcode != 1 || reply["cmd"] != "SET_ACTIVITY" || reply["nonce"].as_str() != Some(nonce) {
        bail!("Discord sent an unexpected activity reply");
    }
    if reply["evt"] == "ERROR" {
        bail!("Discord rejected the activity: {}", response_message(reply));
    }
    if !reply["evt"].is_null() || !(reply["data"].is_object() || reply["data"].is_null()) {
        bail!("Discord did not accept the activity");
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn rejects_errors_and_mismatched_acknowledgments() {
        let good = json!({"cmd":"SET_ACTIVITY","nonce":"one","evt":null,"data":{"name":"Codex"}});
        assert!(validate_reply(1, &good, "one").is_ok());
        assert!(validate_reply(1, &good, "two").is_err());
        assert!(validate_reply(2, &good, "one").is_err());
        let bad = json!({"cmd":"SET_ACTIVITY","nonce":"one","evt":"ERROR","data":{"code":4000,"message":"state is not allowed to be empty"}});
        assert!(
            validate_reply(1, &bad, "one")
                .unwrap_err()
                .to_string()
                .contains("state is not allowed to be empty")
        );
    }
    use std::io::{Read, Write};
    use std::thread;

    #[link(name = "kernel32")]
    unsafe extern "system" {
        fn CreateNamedPipeW(
            name: *const u16,
            mode: u32,
            pipe_mode: u32,
            max: u32,
            out_size: u32,
            in_size: u32,
            timeout: u32,
            security: *const c_void,
        ) -> *mut c_void;
        fn ConnectNamedPipe(handle: *mut c_void, overlapped: *mut c_void) -> i32;
    }

    fn mock(
        handler: impl FnOnce(&mut File) + Send + 'static,
    ) -> (DiscordIpcClient, thread::JoinHandle<()>) {
        let name = format!(
            r"\\.\pipe\codex-presence-test-{}-{}",
            std::process::id(),
            NONCE.fetch_add(1, Ordering::Relaxed)
        );
        let wide: Vec<u16> = name.encode_utf16().chain(Some(0)).collect();
        let handle =
            unsafe { CreateNamedPipeW(wide.as_ptr(), 3, 0, 1, 4096, 4096, 0, std::ptr::null()) };
        assert_ne!(handle as isize, -1);
        let mut server = unsafe { File::from_raw_handle(handle) };
        let worker = thread::spawn(move || {
            let result = unsafe { ConnectNamedPipe(server.as_raw_handle(), std::ptr::null_mut()) };
            assert!(result != 0 || io::Error::last_os_error().raw_os_error() == Some(535));
            handler(&mut server);
        });
        let socket = OpenOptions::new()
            .read(true)
            .write(true)
            .custom_flags(0x40000000)
            .open(name)
            .unwrap();
        (
            DiscordIpcClient {
                client_id: "test".into(),
                socket: Some(socket),
            },
            worker,
        )
    }

    fn read_packet(socket: &mut File) -> (u32, Value) {
        let mut header = [0; 8];
        socket.read_exact(&mut header).unwrap();
        let opcode = u32::from_le_bytes(header[..4].try_into().unwrap());
        let size = u32::from_le_bytes(header[4..].try_into().unwrap()) as usize;
        let mut body = vec![0; size];
        socket.read_exact(&mut body).unwrap();
        (opcode, serde_json::from_slice(&body).unwrap())
    }

    fn write_packet(socket: &mut File, opcode: u32, value: Value) {
        let body = serde_json::to_vec(&value).unwrap();
        socket.write_all(&opcode.to_le_bytes()).unwrap();
        socket
            .write_all(&(body.len() as u32).to_le_bytes())
            .unwrap();
        socket.write_all(&body).unwrap();
    }

    #[test]
    fn repeated_publication_drains_large_acknowledgments_and_clears() {
        let (mut client, worker) = mock(|socket| {
            assert_eq!(read_packet(socket).0, 0);
            write_packet(socket, 1, json!({"evt":"READY","data":{}}));
            for index in 0..129 {
                let (_, request) = read_packet(socket);
                if index == 128 {
                    assert!(request["args"]["activity"].is_null());
                }
                let data = if index == 128 {
                    Value::Null
                } else {
                    json!({"name":"Codex App","padding":"x".repeat(8192)})
                };
                write_packet(
                    socket,
                    1,
                    json!({"cmd":"SET_ACTIVITY","evt":null,"nonce":request["nonce"],"data":data}),
                );
            }
        });
        client.handshake().unwrap();
        for _ in 0..128 {
            client
                .set_activity(Activity::new().name("Codex App").details("Thinking"))
                .unwrap();
        }
        client.clear_activity().unwrap();
        client.close().unwrap();
        worker.join().unwrap();
    }

    #[test]
    fn a_rejected_activity_never_reports_success() {
        let (mut client, worker) = mock(|socket| {
            let (_, request) = read_packet(socket);
            write_packet(
                socket,
                1,
                json!({"cmd":"SET_ACTIVITY","evt":"ERROR","nonce":request["nonce"],"data":{"code":4000,"message":"state is not allowed to be empty"}}),
            );
        });
        let result = client.set_activity(Activity::new().state(""));
        assert!(
            result
                .unwrap_err()
                .to_string()
                .contains("state is not allowed to be empty")
        );
        worker.join().unwrap();
    }

    #[test]
    fn stalled_writes_are_cancelled_without_waiting_for_the_peer() {
        let (sender, receiver) = std::sync::mpsc::channel::<()>();
        let (mut client, worker) = mock(move |_| {
            let _ = receiver.recv_timeout(Duration::from_secs(4));
        });
        let start = Instant::now();
        assert!(
            client
                .send(json!({"payload":"x".repeat(128 * 1024)}), 1)
                .unwrap_err()
                .to_string()
                .contains("two seconds")
        );
        assert!(start.elapsed() < Duration::from_secs(4));
        sender.send(()).unwrap();
        worker.join().unwrap();
    }

    #[test]
    fn handshake_rejections_and_stalled_replies_are_bounded() {
        let (mut client, worker) = mock(|socket| {
            read_packet(socket);
            write_packet(
                socket,
                2,
                json!({"code":4000,"message":"Invalid client_id"}),
            );
        });
        assert!(
            client
                .handshake()
                .unwrap_err()
                .to_string()
                .contains("Invalid client_id")
        );
        worker.join().unwrap();
        let (sender, receiver) = std::sync::mpsc::channel::<()>();
        let (mut client, worker) = mock(move |socket| {
            read_packet(socket);
            let _ = receiver.recv_timeout(Duration::from_secs(4));
        });
        let start = Instant::now();
        assert!(
            client
                .handshake()
                .unwrap_err()
                .to_string()
                .contains("two seconds")
        );
        assert!(start.elapsed() < Duration::from_secs(4));
        sender.send(()).unwrap();
        worker.join().unwrap();
    }
}
