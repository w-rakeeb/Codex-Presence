using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using Microsoft.Win32;

namespace CodexPresence;

public static class PriorityMonitor
{
    private static readonly string[] Games = { "FortniteClient-Win64-Shipping", "VALORANT-Win64-Shipping", "cs2", "r5apex", "RocketLeague", "League of Legends", "Minecraft.Windows", "GenshinImpact", "StarRail", "ZenlessZoneZero", "Destiny2", "GTA5", "GTA5_Enhanced", "RDR2", "Overwatch", "dota2", "TslGame", "RainbowSix", "RainbowSix_Vulkan", "eldenring", "Cyberpunk2077", "Warframe", "FallGuys_client_game", "RobloxPlayerBeta", "DeadByDaylight-Win64-Shipping", "MarvelRivals-Win64-Shipping" };

    public static string? Match(IEnumerable<string> runningNames, string selectedApps, bool steamGameRunning)
    {
        if (steamGameRunning) return "Steam game";
        var selected = new HashSet<string>(Games, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in (selectedApps ?? "").Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var name = Path.GetFileName(entry.Trim().Trim('"'));
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            if (!string.IsNullOrWhiteSpace(name)) selected.Add(name);
        }
        return runningNames.FirstOrDefault(selected.Contains);
    }

    public static string? Find(Preferences options)
    {
        if (!options.PreferOtherApps) return null;
        var names = new List<string>();
        foreach (var process in Process.GetProcesses())
        {
            try { names.Add(process.ProcessName); }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
            finally { process.Dispose(); }
        }
        var steamGame = false;
        if (options.DetectSteamGames && names.Contains("steam", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                steamGame = Convert.ToUInt64(steam?.GetValue("RunningAppID") ?? 0, CultureInfo.InvariantCulture) != 0;
            }
            catch (SecurityException) { }
            catch (UnauthorizedAccessException) { }
            catch (FormatException) { }
            catch (InvalidCastException) { }
            catch (OverflowException) { }
        }
        return Match(names, options.PriorityApplications, steamGame);
    }
}
