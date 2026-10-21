using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexPresence;

public sealed class WindowPresentationState
{
    public double Width { get; set; } = 640;
    public double Height { get; set; } = 820;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public string LastTab { get; set; } = "Overview";
    public string ThemeMode { get; set; } = "inherit";
    public int InterfaceRevision { get; set; }
    private static readonly SemaphoreSlim writes = new(1, 1);
    public static WindowPresentationState Load(string root)
    {
        try { var path = Path.Combine(root, "Data", "window-state.json"); return File.Exists(path) ? JsonSerializer.Deserialize<WindowPresentationState>(File.ReadAllText(path)) ?? new() : new(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public async Task Save(string root)
    {
        var json = JsonSerializer.Serialize(this);
        await writes.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                var path = Path.Combine(root, "Data", "window-state.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                File.Move(temporary, path, true);
            }).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        finally { writes.Release(); }
    }
}
