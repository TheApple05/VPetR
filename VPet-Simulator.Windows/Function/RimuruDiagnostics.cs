using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows;

internal static class RimuruDiagnostics
{
    internal static void Save(MainWindow window)
    {
        if (!window.Args.ContainsLine("diagnostic")) return;
        var graph = window.Core.Graph!;
        var report = new
        {
            language = window.Set.Language,
            character = window.Set.PetGraph,
            graphCount = graph.GraphsALL.Count,
            graphs = graph.GraphsALL.Select(item => new
            {
                name = item.GraphInfo.Name,
                type = item.GraphInfo.Type.ToString(),
                mode = item.GraphInfo.ModeType.ToString(),
                animation = item.GraphInfo.Animat.ToString(),
                ready = item.IsReady,
                failed = item.IsFail,
                error = item.FailMessage
            }),
            errors = window.Main.ErrorMessage.ToArray(),
            mods = window.CoreMODs.Select(mod => new { mod.Name, mod.SuccessLoad, mod.ErrorMessage })
        };
        File.WriteAllText(Path.Combine(ExtensionValue.BaseDirectory, "rimuru-diagnostics.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        if (window.Main.ErrorMessage.Count != 0) return;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var width = Math.Max(1, (int)Math.Ceiling(window.MGrid.ActualWidth));
            var height = Math.Max(1, (int)Math.Ceiling(window.MGrid.ActualHeight));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window.MGrid);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(ExtensionValue.BaseDirectory, "rimuru-live-control.png"));
            encoder.Save(stream);
        };
        timer.Start();
    }
}
