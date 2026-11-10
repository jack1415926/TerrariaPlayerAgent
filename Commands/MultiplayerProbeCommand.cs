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
    public override string Usage => "/tpamp spawn | status | team 0-5 | left/right 1-10 | stop | remove";
    public override string Description => "Experimental multiplayer player probe.";

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        if (Main.netMode != NetmodeID.MultiplayerClient)
        {
            caller.Reply("Use this probe in a multiplayer test world.");
            return;
        }
        string action = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        int value = 0;
        bool valid = action is "spawn" or "status" or "stop" or "remove"
            ? args.Length == 1
            : args.Length == 2 && int.TryParse(args[1], out value) &&
                (action == "team" ? value is >= 0 and <= 5 :
                    action is "left" or "right" && value is >= 1 and <= 10);
        if (!valid) { caller.Reply(Usage); return; }
        ModContent.GetInstance<MultiplayerProbe>().Request(action, value);
    }
}
