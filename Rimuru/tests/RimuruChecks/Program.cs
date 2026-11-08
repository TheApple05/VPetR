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
        if (args[0] == "--behavior") { CheckBehavior(); return; }
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
        var singles=new[]{"default","startup","shutdown","switch.up","switch.down","thirsty","hungry","eat","drink","gift","levelup"};
        var phased=new[]{"idle","sit","stretch","rest","inspect","lookaround","scarf","yawn","sway","touch.head","touch.body","sleep","say","focus","study","play","walk.left","walk.right","resting"};
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
        string Family(IGraph item) {
            var folder=new DirectoryInfo(item.GraphInfo.SourceFolder!);
            return new[]{"start","loop","end"}.Contains(folder.Name) ? folder.Parent!.Name : folder.Name;
        }
        var families=graph.GraphsALL.GroupBy(Family).Select(group=>group.First()).ToArray();
        Check(families.Length==25,"Expected 25 distinct pose families.");
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
            using var stream=File.Create(Path.Combine(rendered,Family(animation)+".png"));encoder.Save(stream);
        }
        CheckTransitions(graph,qa);
        var result=new{ok=true,readyGraphs=graph.GraphsALL.Count,actionChecks=checks,playedFamilies=families.Length,transitions=true};
        File.WriteAllText(output,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(File.ReadAllText(output));
    }

    private static void Pump(int milliseconds)
    {
        var frame=new DispatcherFrame();
        var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
    }

    private static void CheckTransitions(GraphCore graph,string output)
    {
        using var main=new VPet_Simulator.Core.Main(new GameCore { Graph=graph,Controller=new CheckController(),Save=new GameSave("Check") });
        main.EventTimer.Stop();
        var first=(PNGAnimation)graph.FindGraph("default",GraphInfo.AnimatType.Single,IGameSave.ModeType.Happy)!;
        var second=(PNGAnimation)graph.FindGraph("lookaround",GraphInfo.AnimatType.B_Loop,IGameSave.ModeType.Happy)!;
        BitmapSource Pixel(PNGAnimation animation)=>animation.GetType().GetMethod("GetFrameSource",BindingFlags.Instance|BindingFlags.NonPublic)!.CreateDelegate<Func<int,BitmapSource?>>(animation)(0)!;
        main.PetGrid.Child=new Image { Source=Pixel(second),Width=500,Height=500 };
        main.PetGrid2.Child=new Image { Source=Pixel(first),Width=500,Height=500 };
        main.PetGrid2.Visibility=Visibility.Visible;main.PetGrid2.Opacity=1;
        var host=new Window { Content=main,Width=500,Height=500,Left=-32000,Top=-32000,ShowInTaskbar=false,ShowActivated=false };
        host.Show();Pump(40);
        var show=typeof(VPet_Simulator.Core.Main).GetMethod("ShowGraphLayer",BindingFlags.NonPublic|BindingFlags.Instance)!;
        show.Invoke(main,new object[]{main.PetGrid,main.PetGrid2,true});Pump(70);
        Check(main.PetGrid.Opacity>0 && main.PetGrid.Opacity<1,"Incoming layer must blend instead of snapping.");
        Check(main.PetGrid2.Opacity>0 && main.PetGrid2.Opacity<1,"Outgoing layer must remain during the blend.");
        var frames=Path.Combine(output,"transition-frames");Directory.CreateDirectory(frames);
        for(var i=0;i<12;i++)
        {
            if(i==2)show.Invoke(main,new object[]{main.PetGrid2,main.PetGrid,true});
            var image=new RenderTargetBitmap(500,500,96,96,PixelFormats.Pbgra32);image.Render(main.MainGrid);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
            using(var stream=File.Create(Path.Combine(frames,$"{i:00}.png")))encoder.Save(stream);
            Pump(35);
        }
        Check(main.PetGrid2.Visibility==Visibility.Visible && main.PetGrid2.Opacity==1,"Interrupted fade must leave the latest layer visible.");
        Check(main.PetGrid.Visibility==Visibility.Hidden,"Old completion callbacks must not hide the new layer.");
        show.Invoke(main,new object[]{main.PetGrid,main.PetGrid2,false});
        Check(main.PetGrid.Visibility==Visibility.Visible && main.PetGrid2.Visibility==Visibility.Hidden,"Same-state loops should switch without fading.");
        var layered=main.FindDisplayBorder(first);Pump(220);
        Check(layered.Visibility==Visibility.Visible && layered.Opacity==1,"Layered food/prop animations must reset opacity after a fade.");
        main.DisplayType=first.GraphInfo;
        main.LastInteractionTime=DateTime.Now.AddSeconds(-60);
        var idle=typeof(VPet_Simulator.Core.Main).GetMethod("DisplayContextualIdle",BindingFlags.NonPublic|BindingFlags.Instance)!;
        foreach(var state in new[]{VPet_Simulator.Core.Main.WorkingState.Work,VPet_Simulator.Core.Main.WorkingState.Sleep})
        {
            main.State=state;idle.Invoke(main,null);
            Check(main.IdleReason==null,"Idle must not interrupt work or sleep.");
        }
        main.State=VPet_Simulator.Core.Main.WorkingState.Nomal;main.isPress=true;idle.Invoke(main,null);
        Check(main.IdleReason==null,"Idle must not interrupt dragging.");
        main.isPress=false;main.LastInteractionTime=DateTime.Now;idle.Invoke(main,null);
        Check(main.IdleReason==null,"Actual recent-click gate failed.");
        main.LastInteractionTime=DateTime.Now.AddSeconds(-60);idle.Invoke(main,null);
        Check(main.IdleReason!=null && main.DisplayType.Type==GraphInfo.GraphType.Idel,"Eligible idle must play a contextual animation.");
        host.Close();
        Console.WriteLine("Transitions passed: intermediate opacity, rapid interruption, stable final visibility, same-state bypass.");
    }

    private sealed class CheckController:IController
    {
        public void MoveWindows(double x,double y) { }
        public double GetWindowsDistanceLeft()=>1000;
        public double GetWindowsDistanceRight()=>1000;
        public double GetWindowsDistanceUp()=>1000;
        public double GetWindowsDistanceDown()=>1000;
        public double ZoomRatio=>1;
        public int PressLength=>300;
        public void ShowPanel() { }
        public void ResetPosition() { }
        public bool CheckPosition()=>false;
        public bool EnableFunction=>false;
        public int InteractionCycle=>20;
        public bool RePositionActive { get;set; }
    }

    private static void CheckBehavior()
    {
        (string Name,string Reason)? Pick(bool available=true,double quiet=60,double cooldown=60,
            double energy=.9,double mood=.9,double food=100,double drink=100,int hour=14,string? last=null,int roll=0)
            => ContextualIdle.Choose(available,quiet,cooldown,energy,mood,food,drink,hour,last,roll);
        Check(Pick(quiet:5)==null,"Recent interaction must suppress idle actions.");
        Check(Pick(available:false)==null,"Work, sleep, drag and non-default graphs must be protected.");
        Check(Pick(cooldown:10)==null,"Idle cooldown must prevent spam.");
        Check(Pick(energy:.1)?.Name=="rest","Low energy should rest.");
        Check(Pick(hour:23)?.Name=="yawn","Nighttime should favor quiet animations.");
        Check(Pick()?.Name=="sway","Cheerful daytime should allow happy sway.");
        Check(Pick(quiet:300,roll:4)?.Name=="walk","Long idle should sometimes explore.");
        Check(Pick(quiet:300)?.Name=="stretch","Long idle should trigger a stretch break.");
        Check(Pick(drink:5)?.Name=="thirsty" && Pick(food:5)?.Name=="hungry","Needs should drive waiting, without granting food/drink.");
        foreach(var name in new[]{"rest","yawn","sit","lookaround","scarf","sway","stretch","inspect","idle"})
            for(var roll=0;roll<20;roll++) Check(Pick(last:name,roll:roll)?.Name!=name,"Do not repeat the last idle.");
        Console.WriteLine("Behavior policy passed: grace, activity guards, cooldown, fatigue, night, mood, long idle, needs and repetition.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
