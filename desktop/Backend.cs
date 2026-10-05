using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CodexPresence;

public sealed class Preferences
{
    public string CodexHome { get; set; } = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    public int PollSeconds { get; set; } = 1;
    public int StaleSeconds { get; set; } = 90;
    public int StickySeconds { get; set; } = 3600;
    public bool IncludeWsl { get; set; }
    public bool EfficiencyMode { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool StartEngineOnOpen { get; set; }
    public bool StartInBackground { get; set; }
    public bool StartWithWindows { get; set; }
    public bool MonitorOnly { get; set; }
    public bool HideTrayIcon { get; set; }
    public bool StartWithChatGpt { get; set; }
    public bool PreferOtherApps { get; set; }
    public bool DetectSteamGames { get; set; } = true;
    public string PriorityApplications { get; set; } = "";
    public string InterfaceStyle { get; set; } = "minimal";
    public string ColorMode { get; set; } = "dark";
    public string ColorTheme { get; set; } = "neutral";

    public bool SameMonitoring(Preferences other) => CodexHome == other.CodexHome && PollSeconds == other.PollSeconds && StaleSeconds == other.StaleSeconds && StickySeconds == other.StickySeconds && IncludeWsl == other.IncludeWsl && EfficiencyMode == other.EfficiencyMode && MonitorOnly == other.MonitorOnly;

    public static Preferences Load(string root) => Load(root, out _);

    public static Preferences Load(string root, out string? warning)
    {
        warning = null;
        var path = Path.Combine(root, "Data", "app-settings.json");
        try
        {
            var value = File.Exists(path) ? JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path)) ?? throw new JsonException("Settings must be an object.") : new();
            if (string.IsNullOrWhiteSpace(value.CodexHome) || !Path.IsPathFullyQualified(value.CodexHome)) throw new JsonException("Codex home must be an absolute folder path.");
            value.CodexHome = Path.GetFullPath(value.CodexHome);
            if (value.PollSeconds is < 1 or > 60 || value.StaleSeconds is < 1 or > 86400 || value.StickySeconds is < 60 or > 86400)
            {
                value.PollSeconds = Math.Clamp(value.PollSeconds, 1, 60);
                value.StaleSeconds = Math.Clamp(value.StaleSeconds, 1, 86400);
                value.StickySeconds = Math.Clamp(value.StickySeconds, 60, 86400);
                warning = "Monitoring values were outside their supported ranges. Review Settings before saving. The original file is unchanged.";
            }
            value.PriorityApplications ??= "";
            return value;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            warning = "Could not read desktop settings. Safe defaults are active; the original file is unchanged. Review Settings or restore a backup. " + error.Message;
            return new Preferences();
        }
    }

    public void Save(string root)
    {
        var folder = Path.Combine(root, "Data");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "app-settings.json");
        var contents = JsonSerializer.Serialize(this, Backend.JsonOptions);
        if (File.Exists(path) && File.ReadAllText(path) == contents) return;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, contents, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                var backups = Path.Combine(folder, "Backups", "Preferences");
                Directory.CreateDirectory(backups);
                File.Copy(path, Path.Combine(backups, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".json"));
                File.Replace(temporary, path, null);
            }
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void ApplyStartup()
    {
        using var key = StartWithWindows || StartWithChatGpt
            ? Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")
            : Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (key == null) return;
        if (StartWithWindows || StartWithChatGpt)
            key.SetValue("CodexPresenceDesktop", "\"" + Environment.ProcessPath + "\" " + (StartWithWindows ? "--startup" : "--watch"));
        else if (key.GetValue("CodexPresenceDesktop") is string command && command.Contains(Environment.ProcessPath ?? "\0", StringComparison.OrdinalIgnoreCase))
            key.DeleteValue("CodexPresenceDesktop", false);
    }
}

public sealed class Backend
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string Root { get; }
    public Preferences Options { get; set; }
    public string Executable => Path.Combine(Root, "Runtime", "codex-discord-rich-presence-windows-x64.exe");
    public string ConfigPath => Path.Combine(Options.CodexHome, "discord-presence-config.json");
    public string BackupDirectory { get; }
    public bool Running => process is { HasExited: false };
    public int? ProcessId => Running ? process!.Id : null;
    public JsonObject? Config { get; private set; }
    public JsonObject? Snapshot => Volatile.Read(ref snapshot);
    public event Action<JsonObject>? Updated;
    public event Action<string>? Logged;
    public event Action? Exited;
    public string? LastError { get; private set; }
    public bool PriorityHeld { get; private set; }
    private readonly long applicationStartEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private Process? process;
    private JsonObject? snapshot;
    private Task? completion;
    private string? baseHash;
    private readonly SemaphoreSlim writes = new(1, 1);

    public Backend(string root, Preferences options, bool verification = false) { Root = root; Options = options; BackupDirectory = Path.Combine(root, "Data", verification ? "Verification\\Backups" : "Backups"); }

    public ProcessStartInfo CreateStartInfo(string arguments)
    {
        var info = new ProcessStartInfo(Executable, arguments)
        {
            WorkingDirectory = Root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        info.Environment["CODEX_HOME"] = Options.CodexHome;
        info.Environment["CODEX_PRESENCE_STARTED_AT"] = applicationStartEpoch.ToString(CultureInfo.InvariantCulture);
        info.Environment["CODEX_PRESENCE_SURFACE"] = "desktop";
        info.Environment["CODEX_PRESENCE_POLL_SECONDS"] = Options.PollSeconds.ToString(CultureInfo.InvariantCulture);
        info.Environment["CODEX_PRESENCE_STALE_SECONDS"] = Options.StaleSeconds.ToString(CultureInfo.InvariantCulture);
        info.Environment["CODEX_PRESENCE_ACTIVE_STICKY_SECONDS"] = Options.StickySeconds.ToString(CultureInfo.InvariantCulture);
        info.Environment["CODEX_PRESENCE_INCLUDE_WSL"] = Options.IncludeWsl ? "1" : "0";
        info.Environment["CODEX_PRESENCE_EFFICIENCY_MODE"] = Options.EfficiencyMode ? "1" : "0";
        return info;
    }

    public Task<string> Command(string arguments, string? input = null) => RunCommand(CreateStartInfo(arguments), input, TimeSpan.FromSeconds(30));

    internal static async Task<string> RunCommand(ProcessStartInfo start, string? input, TimeSpan limit)
    {
        using var child = new Process { StartInfo = start };
        child.Start();
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(limit);
        try
        {
            if (input != null) await child.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
            child.StandardInput.Close();
            await child.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!child.HasExited) child.Kill(true);
            await child.WaitForExitAsync();
            await Task.WhenAll(output, error);
            throw new IOException("The request timed out. Check that the Codex folder is accessible, then try again.");
        }
        var result = await output;
        var problem = await error;
        if (child.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(problem) ? result.Trim() : problem.Trim());
        return result;
    }

    public ProcessStartInfo CreateTerminalStartInfo(string arguments = "terminal-view")
    {
        var info = CreateStartInfo(arguments);
        info.CreateNoWindow = false;
        info.RedirectStandardInput = info.RedirectStandardOutput = info.RedirectStandardError = false;
        info.StandardOutputEncoding = info.StandardErrorEncoding = null;
        return info;
    }

    private string? HashFile() => File.Exists(ConfigPath) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ConfigPath))) : null;

    public async Task LoadConfig()
    {
        var before = await Task.Run(HashFile);
        var value = JsonNode.Parse(await Command("config-get"))!.AsObject();
        var after = await Task.Run(HashFile);
        if (before != after) throw new IOException("Settings changed while loading. Reload and try again.");
        Config = value;
        baseHash = after;
    }

    public async Task SaveConfig(JsonObject value)
    {
        await writes.WaitAsync();
        try
        {
            var validated = JsonNode.Parse(await Command("config-check", value.ToJsonString()))!.AsObject();
            if (HashFile() != baseHash) throw new IOException("Another app changed these settings. Reload before saving so its changes are preserved.");
            if (File.Exists(ConfigPath) && JsonNode.DeepEquals(JsonNode.Parse(await File.ReadAllTextAsync(ConfigPath)), validated))
            {
                Config = validated;
                return;
            }
            Directory.CreateDirectory(Options.CodexHome);
            Directory.CreateDirectory(BackupDirectory);
            var temporary = ConfigPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, validated.ToJsonString(JsonOptions), new UTF8Encoding(false));
                if (File.Exists(ConfigPath))
                {
                    var backup = Path.Combine(BackupDirectory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".json");
                    File.Copy(ConfigPath, backup);
                    if (HashFile() != baseHash) throw new IOException("Another app changed these settings during the backup. Reload and try again.");
                    File.Replace(temporary, ConfigPath, null);
                }
                else File.Move(temporary, ConfigPath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            baseHash = HashFile();
            Config = validated;
        }
        finally { writes.Release(); }
    }

    public async Task Start()
    {
        if (Running) return;
        if (completion != null) await completion;
        process?.Dispose();
        process = null;
        var child = new Process { StartInfo = CreateStartInfo("desktop-bridge" + (Options.MonitorOnly ? " --observe" : "") + (PriorityHeld ? " --priority-hold" : "")), EnableRaisingEvents = true };
        Volatile.Write(ref snapshot, null);
        LastError = null;
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try { if (!child.Start()) throw new IOException("The presence engine could not start."); }
        catch { child.Dispose(); throw; }
        process = child;
        var snapshots = Task.Run(() => ReadSnapshots(child, ready));
        var errors = Task.Run(() => ReadErrors(child));
        completion = CompleteProcess(child, snapshots, errors, ready);
        try { await ready.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
        catch (TimeoutException) { throw new IOException("The engine is still scanning sessions. Check Tools for progress, or stop it and try again."); }
    }

    public async Task SetPriorityHold(bool held)
    {
        if (PriorityHeld == held) return;
        var child = process;
        if (child == null || child.HasExited) { PriorityHeld = held; return; }
        try { await child.StandardInput.WriteLineAsync(held ? "yield" : "resume"); await child.StandardInput.FlushAsync(); PriorityHeld = held; }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        {
            LastError = error.Message;
            Logged?.Invoke("Priority control: " + error.Message);
        }
    }

    private async Task ReadSnapshots(Process child, TaskCompletionSource<bool> ready)
    {
        try
        {
            while (await child.StandardOutput.ReadLineAsync() is { } line)
            {
                try
                {
                    if (JsonNode.Parse(line) is not JsonObject value) { Logged?.Invoke(line); continue; }
                    Volatile.Write(ref snapshot, value);
                    Updated?.Invoke(value);
                    ready.TrySetResult(true);
                }
                catch (JsonException) { Logged?.Invoke(line); }
            }
        }
        catch (IOException error) { Logged?.Invoke(error.Message); }
    }

    private async Task ReadErrors(Process child)
    {
        while (await child.StandardError.ReadLineAsync() is { } line)
        {
            LastError = line;
            Logged?.Invoke(line);
        }
    }

    private async Task CompleteProcess(Process child, Task snapshots, Task errors, TaskCompletionSource<bool> ready)
    {
        await child.WaitForExitAsync();
        try { await Task.WhenAll(snapshots, errors); }
        catch (IOException error) { LastError = error.Message; }
        if (child.ExitCode == 0) LastError = null;
        if (!ready.Task.IsCompleted)
            ready.TrySetException(new IOException(LastError ?? "The presence engine exited before it became ready."));
        Logged?.Invoke(child.ExitCode == 0 ? "Engine stopped." : LastError ?? "Engine failed to start.");
        Exited?.Invoke();
    }

    public async Task Stop()
    {
        var child = process;
        if (child == null || child.HasExited) return;
        try { await child.StandardInput.WriteLineAsync("quit"); child.StandardInput.Close(); }
        catch (IOException) { }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await child.WaitForExitAsync(timeout.Token); if (completion != null) await completion.WaitAsync(timeout.Token); }
        catch (OperationCanceledException) { throw new IOException("The engine is still finishing a request. Wait a moment and try Stop again."); }
    }
}
