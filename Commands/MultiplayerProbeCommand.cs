using System;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TerrariaPlayerAgent.Common;

namespace TerrariaPlayerAgent.Commands;

// Deliberately separate from /tpa until multiplayer mechanics are verified.
public sealed class MultiplayerProbeCommand : ModCommand
{
    public override string Command => "tpamp";
    public override CommandType Type => CommandType.Chat;
    public override string Usage => "/tpamp spawn | status | goal <text> | diag | combat | team 0-5 | left/right 1-10 | stop | remove";
    public override string Description => "Experimental multiplayer player probe.";

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        if (Main.netMode != NetmodeID.MultiplayerClient)
        {
            caller.Reply("Use this probe in a multiplayer test world.");
            return;
        }
        string action = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        if (action == "goal")
        {
            string text = string.Join(" ", args, 1, args.Length - 1).Trim();
            if (text.Length is < 1 or > 500) { caller.Reply("Goal must contain 1-500 characters."); return; }
            ModContent.GetInstance<MultiplayerProbe>().RequestGoal(text);
            return;
        }
        if (action == "diag" && args.Length == 1)
        {
            ModContent.GetInstance<MultiplayerProbe>().PrintDiagnostics(caller);
            return;
        }
        int value = 0;
        bool valid = action is "spawn" or "status" or "stop" or "remove" or "combat"
            ? args.Length == 1
            : args.Length == 2 && int.TryParse(args[1], out value) &&
                (action == "team" ? value is >= 0 and <= 5 :
                    action is "left" or "right" && value is >= 1 and <= 10);
        if (!valid) { caller.Reply(Usage); return; }
        ModContent.GetInstance<MultiplayerProbe>().Request(action, value);
    }
}
