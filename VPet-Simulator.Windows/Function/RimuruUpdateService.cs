using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows;

internal static class RimuruUpdateService
{
    private static DispatcherTimer? timer;
    private static DateTime nextCheck;
    private static string? handledToken;
    private sealed record Request(string Token, string Version, int HelperPid);

    internal static void Start(MainWindow window)
    {
        window.RimuruUpdateReady = true;
        if (timer != null) return;
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => Tick();
        timer.Start();
        Tick();
    }

    private static void Tick()
    {
        var root = ExtensionValue.BaseDirectory;
        if (File.Exists(Path.Combine(root, "disable-auto-updates.txt"))) return;
        var windows = App.MainWindows.ToArray();
        if (windows.Length == 0 || windows.Any(w => !w.RimuruUpdateReady)) return;
        var requestPath = Path.Combine(root, $"rimuru-update-{Environment.ProcessId}.request.json");
        if (File.Exists(requestPath))
        {
            Request? request = null;
            try
            {
                request = JsonSerializer.Deserialize<Request>(File.ReadAllText(requestPath));
                if (request == null || request.Token == handledToken || !Guid.TryParseExact(request.Token, "N", out _) ||
                    !Version.TryParse(request.Version, out var target) ||
                    target <= Version.Parse(File.ReadAllText(Path.Combine(root, "rimuru-version.txt")).Trim())) return;
                using var helper = Process.GetProcessById(request.HelperPid);
                if (helper.ProcessName is not ("powershell" or "pwsh") ||
                    File.GetLastWriteTimeUtc(requestPath) < helper.StartTime.ToUniversalTime()) return;
                handledToken = request.Token;
                var running = windows.Select(w => (Window:w, Auto:w.AutoSaveTimer.Enabled, Events:w.Main.EventTimer.Enabled)).ToArray();
                try
                {
                    foreach (var item in running)
                    {
                        item.Window.AutoSaveTimer.Stop();
                        item.Window.Main.EventTimer.Stop();
                    }
                    foreach (var window in windows)
                    {
                        window.SaveForUpdate();
                        var save = Path.Combine(root, "Saves", $"Save{window.PrefixSave}_{window.Set.SaveTimes}.lps");
                        var setting = Path.Combine(root, $"Setting{window.PrefixSave}.lps");
                        if (!File.Exists(save) || new FileInfo(save).Length == 0 || File.ReadAllText(setting) != window.Set.ToString())
                            throw new IOException("The update save could not be verified.");
                    }
                    WriteReply(root, request, new { request.Token, request.Version, request.HelperPid, Saved = true, Arguments = App.Args,
                        Saves = windows.Select(w => $"Saves/Save{w.PrefixSave}_{w.Set.SaveTimes}.lps").ToArray() });
                    foreach (var window in windows) window.ClosingForUpdate = true;
                    timer!.Stop();
                    Application.Current.Shutdown();
                }
                catch
                {
                    foreach (var item in running)
                    {
                        item.Window.ClosingForUpdate = false;
                        item.Window.AutoSaveTimer.Enabled = item.Auto;
                        item.Window.Main.EventTimer.Enabled = item.Events;
                    }
                    throw;
                }
            }
            catch (Exception error)
            {
                try { File.WriteAllText(Path.Combine(root, "rimuru-update.log"), "Automatic restart cancelled: " + error.Message); } catch { }
                if (request != null && request.Token == handledToken)
                    try { WriteReply(root, request, new { request.Token, request.Version, request.HelperPid, Saved = false, Error = error.Message }); } catch { }
            }
        }
        if (DateTime.UtcNow < nextCheck || windows.All(w => w.Args.ContainsLine("diagnostic"))) return;
        nextCheck = DateTime.UtcNow.AddHours(6);
        try
        {
            var script = Path.Combine(root, "Update-Rimuru.ps1");
            if (File.Exists(script)) Process.Start(new ProcessStartInfo("powershell.exe")
            {
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"",
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root
            });
        }
        catch (Exception error) { try { File.WriteAllText(Path.Combine(root, "rimuru-update.log"), error.Message); } catch { } }
    }

    private static void WriteReply(string root, Request request, object reply)
    {
        var path = Path.Combine(root, $"rimuru-update-{Environment.ProcessId}.ack.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(reply));
        File.Move(path + ".tmp", path, true);
    }
}
