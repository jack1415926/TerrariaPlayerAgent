using System;
using System.Globalization;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TerrariaPlayerAgent.Common;

namespace TerrariaPlayerAgent.Commands;

public sealed class DemoCommand : ModCommand
{
    public override string Command => "tpa";
    public override CommandType Type => CommandType.Chat;
    public override string Usage => "/tpa spawn | left [seconds] | right [seconds] | jump | stop | status | remove";
    public override string Description => "Control the independent player demo (single-player only).";

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        if (Main.netMode != NetmodeID.SinglePlayer)
        {
            caller.Reply("TPA demo currently supports single-player only.", Color.Orange);
            return;
        }
        if (args.Length == 0)
        {
            caller.Reply(Usage);
            return;
        }

        string action = args[0].ToLowerInvariant();
        bool movement = action is "left" or "right";
        if (args.Length > (movement ? 2 : 1))
        {
            caller.Reply(Usage, Color.Orange);
            return;
        }
        var system = ModContent.GetInstance<DemoPlayerSystem>();
        if (action == "spawn")
            caller.Reply(system.Spawn(caller.Player));
        else if (action == "status")
            caller.Reply(system.Status(caller.Player));
        else if (action == "remove")
        {
            system.Remove();
            caller.Reply("Demo removed.");
        }
        else if (action == "stop")
        {
            system.Stop();
            caller.Reply("Demo input stopped (gravity and inertia still apply).");
        }
        else if (movement || action == "jump")
        {
            if (system.Actor == null || system.Actor.dead)
            {
                caller.Reply("No living demo. Use /tpa remove, then /tpa spawn.", Color.Orange);
                return;
            }
            if (action == "jump")
            {
                system.Jump();
                caller.Reply("Jump requested.");
                return;
            }
            double seconds = 2;
            if (args.Length == 2 && (!double.TryParse(args[1], NumberStyles.Float,
                CultureInfo.InvariantCulture, out seconds) || !double.IsFinite(seconds) || seconds <= 0 || seconds > 10))
            {
                caller.Reply("Duration must be greater than 0 and at most 10 seconds (example: /tpa right 2).", Color.Orange);
                return;
            }
            system.Move(action == "left" ? -1 : 1, (int)Math.Ceiling(seconds * 60));
            caller.Reply($"Moving {action} for {seconds} simulation seconds.");
        }
        else
            caller.Reply(Usage, Color.Orange);
    }
}
