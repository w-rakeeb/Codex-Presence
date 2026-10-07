using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;

namespace CodexPresence;

public sealed class UserActionException : InvalidOperationException
{
    public UserActionException(string message) : base(message) { }
}

public static class UserFeedback
{
    public static string Describe(Exception error)
    {
        if (error is UserActionException) return error.Message;
        var message = error.Message;
        if (message.StartsWith("Could not open the original terminal:", StringComparison.Ordinal)) return "Could not open the terminal. Check that the complete Runtime folder is present, then try again. Details are in Tools.";
        foreach (var prefix in new[] { "Another app changed", "Settings changed while loading", "The request timed out", "The engine is still", "Save or reload", "The original terminal uses", "Press Q in the original terminal", "Desktop settings could not be applied." })
            if (message.StartsWith(prefix, StringComparison.Ordinal))
                return prefix == "Desktop settings could not be applied." ? "Could not apply desktop settings. Your previous settings were restored where possible. See Tools for details." : message;
        return error switch
        {
            UnauthorizedAccessException => "Access was denied. Check that the app and Codex folders allow changes, then try again.",
            FileNotFoundException or DirectoryNotFoundException => "A required file or folder is missing. Check the app's Runtime folder and the Codex home path in Settings.",
            JsonException => "Could not read the settings format. Reload your settings or restore a backup. Details are in Tools.",
            Win32Exception { NativeErrorCode: 2 or 3 } => "The required app could not be found. Check the Runtime folder or extract the complete release again.",
            Win32Exception { NativeErrorCode: 5 } => "Windows blocked this action. Check file access and security settings, then try again.",
            IOException => "Could not access a file. Close apps using the settings file and try again. Details are in Tools.",
            _ => "Could not complete this action. See Tools for details, then try again."
        };
    }
}
