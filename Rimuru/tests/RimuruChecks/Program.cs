using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LinePutScript;
using VPet_Simulator.Core;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args[0] == "--mod") { CheckMod(args[1], args[2]); return; }
        var source = Path.GetFullPath(args[0]);
        var output = Path.GetFullPath(args[1]);
        var baseline = args.Contains("--baseline");
        var fixture = Path.Combine(Path.GetDirectoryName(output)!, "fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        File.Copy(Path.Combine(source, "003_140.png"), Path.Combine(fixture, "001_1.png"));
        File.Copy(Path.Combine(source, "000_280.png"), Path.Combine(fixture, "000_1.png"));
        GraphCore.CachePath = Path.Combine(fixture, "cache");
        using var graph = Load(fixture);
        var animation = (PNGAnimation)graph.GraphsALL.Single();
        var getFrame = typeof(PNGAnimation).GetMethod("GetFrameSource", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<int, BitmapSource?>>(animation);
        var first = getFrame(0)!;
        var second = getFrame(1)!;
        var reused = ReferenceEquals(first, getFrame(0)) && ReferenceEquals(second, getFrame(1));
        if (!baseline) Check(reused, "Loop frames should reuse the same frozen bitmap objects.");
        Check(first.IsFrozen && second.IsFrozen, "Playback bitmaps must remain safe across threads.");

        var image = new Image();
        for (var pass = 0; pass < 2; pass++)
        {
            var frame = new DispatcherFrame();
            var playback = animation.Run(image, () => image.Dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
            playback.Start();
            Dispatcher.PushFrame(frame);
        }
        var retainedAfterPlayback = ReferenceEquals(first, getFrame(0));
        if (!baseline) Check(retainedAfterPlayback, "Stopping a loop should retain its warm frames until idle eviction.");

        getFrame(0); getFrame(1);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        for (var pass = 0; pass < 10000; pass++) { getFrame(0); getFrame(1); }
        clock.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        animation.CleanupIdleCache(long.MaxValue);
        var afterEviction = getFrame(0)!;
        Check(!ReferenceEquals(first, afterEviction), "Idle cleanup should release frame objects and permit reloading.");

        var oldCache = animation.Path;
        File.SetLastWriteTimeUtc(Path.Combine(fixture, "000_1.png"), DateTime.UtcNow.AddSeconds(2));
        using var changed = Load(fixture);
        var invalidated = changed.GraphsALL.Single().Path != oldCache;
        if (!baseline) Check(invalidated, "Editing a source frame must invalidate the disk sprite cache.");
        if (!baseline)
        {
            var cache=changed.GraphsALL.Single().Path!;
            File.WriteAllBytes(cache,Array.Empty<byte>());
            using var repaired=Load(fixture);
            Check(new FileInfo(cache).Length>57,"A truncated sprite cache must be rebuilt.");
        }
        animation.Dispose();
        Check(getFrame(0) == null, "Disposed animations must not reload pixels.");
        var result = new { baseline, reused, retainedAfterPlayback, invalidated, frameRequests = 20000,
            frameLookupMilliseconds = clock.Elapsed.TotalMilliseconds, frameLookupAllocatedBytes = allocated };
        File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(File.ReadAllText(output));
    }

    private static GraphCore Load(string folder)
    {
        var config = new GraphCore.Config(new LpsDocument("pet#check:|\n"));
        var graph = new GraphCore(500, Dispatcher.CurrentDispatcher, config);
        PNGAnimation.LoadGraph(graph, new DirectoryInfo(folder), new Line($"pnganimation#check:|startuppath#{folder}:|graph#Default:|mode#Nomal:|animat#Single:|"));
        WaitReady(graph);
        Check(graph.GraphsALL.Count == 1 && !graph.GraphsALL[0].IsFail, "Fixture animation must load.");
        return graph;
    }

    private static void WaitReady(GraphCore graph)
    {
        var clock = Stopwatch.StartNew();
        while (graph.GraphsALL.Any(item => !item.IsReady && !item.IsFail))
        {
            Check(clock.Elapsed.TotalSeconds < 20, "Animation loading timed out.");
            Thread.Sleep(10);
        }
        Check(graph.GraphsALL.Count > 0 && graph.GraphsALL.All(item => item.IsReady && !item.IsFail), "Every animation must load.");
    }

    private static void CheckMod(string folder, string output)
    {
        folder=Path.GetFullPath(folder);output=Path.GetFullPath(output);
        var qa=Path.GetDirectoryName(output)!;
        GraphCore.CachePath=Path.Combine(qa,"native-check-cache");
        var loader=new PetLoader(new LpsDocument(File.ReadAllText(Path.Combine(folder,"pet","rimuru.lps"))),new DirectoryInfo(Path.Combine(folder,"pet")));
        using var graph=loader.Graph(500,Dispatcher.CurrentDispatcher);
        WaitReady(graph);
        var singles=new[]{"default","startup","shutdown","idle","sit","stretch","rest","inspect","switch.up","switch.down","thirsty","hungry","eat","drink","gift","levelup"};
        var phased=new[]{"touch.head","touch.body","sleep","say","focus","study","play","walk.left","walk.right","resting"};
        var checks=0;
        foreach(var mood in Enum.GetValues<IGameSave.ModeType>())
        {
            foreach(var name in singles)
            {
                Check(graph.FindGraph(name,GraphInfo.AnimatType.Single,mood)?.IsReady==true,$"Missing action {name}/{mood}");checks++;
            }
            foreach(var name in phased.Append("raised"))
                foreach(var variant in new[]{GraphInfo.AnimatType.A_Start,GraphInfo.AnimatType.B_Loop,GraphInfo.AnimatType.C_End})
                {
                    Check(graph.FindGraph(name,variant,mood)?.IsReady==true,$"Missing phase {name}/{variant}/{mood}");checks++;
                }
            Check(graph.FindGraph("raised",GraphInfo.AnimatType.Single,mood)?.GraphInfo.Type==GraphInfo.GraphType.Raised_Dynamic,"Carried animation must retain the dynamic entry.");
            foreach(var type in new[]{GraphInfo.GraphType.StateONE,GraphInfo.GraphType.StateTWO})
                Check(graph.FindGraphs("resting",GraphInfo.AnimatType.B_Loop,mood).Any(item=>item.GraphInfo.Type==type),"Both resting phases must share a reachable name.");
        }
        var families=graph.GraphsALL.GroupBy(item=>item.GraphInfo.SourceFolder).Select(group=>group.First()).ToArray();
        Check(families.Length==21,"Expected 21 distinct pose families.");
        var rendered=Path.Combine(qa,"native-rendered");Directory.CreateDirectory(rendered);
        foreach(var animation in families)
        {
            var image=new Image();
            var frame=new DispatcherFrame();
            ((Picture.IImageRun)animation).Run(image,()=>image.Dispatcher.BeginInvoke(new Action(()=>frame.Continue=false))).Start();
            Dispatcher.PushFrame(frame);
            image.Measure(new Size(500,500));image.Arrange(new Rect(0,0,500,500));
            var bitmap=new RenderTargetBitmap(500,500,96,96,PixelFormats.Pbgra32);bitmap.Render(image);
            var bytes=new byte[500*500*4];bitmap.CopyPixels(bytes,2000,0);
            Check(Enumerable.Range(0,500*500).Any(i=>bytes[i*4+3]>16),"Rendered action must contain artwork.");
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream=File.Create(Path.Combine(rendered,Path.GetFileName(animation.GraphInfo.SourceFolder)+".png"));encoder.Save(stream);
        }
        var result=new{ok=true,readyGraphs=graph.GraphsALL.Count,actionChecks=checks,playedFamilies=families.Length};
        File.WriteAllText(output,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(File.ReadAllText(output));
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
