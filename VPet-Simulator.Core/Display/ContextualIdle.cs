using System;
using System.Linq;

namespace VPet_Simulator.Core;

public static class ContextualIdle
{
    public static (string Name, string Reason)? Choose(bool available, double quietSeconds,
        double cooldownSeconds, double energy, double mood, double food, double drink,
        int hour, string? last, int roll)
    {
        if (!available || quietSeconds < 20 || cooldownSeconds < 35) return null;
        string[] pool;
        string reason;
        if (drink < 20) { pool = new[] { "thirsty", "rest", "scarf" }; reason = "Waiting for a drink"; }
        else if (food < 20) { pool = new[] { "hungry", "sit", "rest" }; reason = "Waiting for a snack"; }
        else if (energy < .3) { pool = new[] { "rest", "yawn", "sit" }; reason = "Low energy"; }
        else if (hour >= 22 || hour < 6) { pool = new[] { "yawn", "scarf", "sit", "lookaround" }; reason = "Quiet nighttime idle"; }
        else if (quietSeconds >= 180) { pool = new[] { "stretch", "sit", "lookaround", "inspect", "walk" }; reason = "Taking a break after being idle"; }
        else if (mood >= .65) { pool = new[] { "sway", "scarf", "lookaround", "stretch" }; reason = "Feeling cheerful"; }
        else { pool = new[] { "lookaround", "scarf", "idle", "inspect" }; reason = "Keeping you company"; }
        var candidates = pool.Where(name => name != last).ToArray();
        return (candidates[(int)((uint)roll % (uint)candidates.Length)], reason);
    }
}

public partial class Main
{
    private DateTime lastContextualIdle = DateTime.MinValue;
    private string? lastContextualName;
    public string? IdleReason { get; private set; }

    private void DisplayContextualIdle()
    {
        var save = Core.Save!;
        var simulate = Core.Controller!.EnableFunction;
        var choice = ContextualIdle.Choose(State == WorkingState.Nomal && !isPress && DisplayType.Type == GraphInfo.GraphType.Default,
            (DateTime.Now - LastInteractionTime).TotalSeconds, (DateTime.UtcNow - lastContextualIdle).TotalSeconds,
            simulate ? save.Strength / Math.Max(1, save.StrengthMax) : 1,
            simulate ? save.Feeling / Math.Max(1, save.FeelingMax) : .8,
            simulate ? 100 * save.StrengthFood / Math.Max(1, save.StrengthMax) : 100,
            simulate ? 100 * save.StrengthDrink / Math.Max(1, save.StrengthMax) : 100,
            DateTime.Now.Hour, lastContextualName, Function.Rnd.Next());
        if (choice is not { } selected) return;
        if (Function.Rnd.Next(8) == 0)
            foreach (var interaction in RandomInteractionAction.ToArray())
                if (interaction())
                {
                    lastContextualIdle = DateTime.UtcNow;
                    IdleReason = "A companion interaction";
                    return;
                }
        if (selected.Name == "walk")
        {
            if (!DisplayMove()) return;
            lastContextualIdle = DateTime.UtcNow;
            lastContextualName = selected.Name;
            IdleReason = "Exploring after a long idle";
            return;
        }
        var graph = Core.Graph!;
        var start = graph.FindGraph(selected.Name, GraphInfo.AnimatType.A_Start, save.Mode);
        var single = graph.FindGraph(selected.Name, GraphInfo.AnimatType.Single, save.Mode);
        if (start == null && single == null) return;
        lastContextualIdle = DateTime.UtcNow;
        lastContextualName = selected.Name;
        IdleReason = selected.Reason;
        CountNomal = 0;
        if (start == null) Display(single, DisplayToNomal);
        else
        {
            var remaining = selected.Name is "sit" or "rest" ? 5 : 2;
            void Loop()
            {
                if (remaining-- == 0) DisplayCEndtoNomal(selected.Name);
                else Display(selected.Name, GraphInfo.AnimatType.B_Loop, Loop);
            }
            Display(start, Loop);
        }
    }
}
