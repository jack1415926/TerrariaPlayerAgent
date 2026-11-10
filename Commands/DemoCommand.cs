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
    internal int ExecutionCount { get; private set; }
    internal string LastAction { get; private set; } = "";
    internal string LastCaller { get; private set; } = "";
    internal bool LastAccepted { get; private set; }
    public override string Command => "tpa";
    public override CommandType Type => CommandType.Chat;
    public override string Usage => "/tpa spawn | left/right [seconds] | goto pixelX [seconds] | home [seconds] | range tiles/off | jump [left/right [seconds]] | down [seconds] | attack left/right [seconds] | select slot | mine tileX tileY [seconds] | goal text | stop | status | remove | equip wood/iron/pick | enemy slime/zombie/eye | clear-enemies";
    public override string Description => "Control the independent player demo (single-player only).";

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        ExecutionCount++;
        LastAccepted = true;
        LastAction = args.Length > 0 ? args[0] : "";
        LastCaller = caller.GetType().Name;
        if (Main.netMode != NetmodeID.SinglePlayer)
        {
            Reject(caller, "TPA demo currently supports single-player only.");
            return;
        }
        if (args.Length == 0)
        {
            LastAccepted = false;
            caller.Reply(Usage);
            return;
        }

        string action = args[0].ToLowerInvariant();
        if (action == "goal")
        {
            // Goal text keeps its spaces, so it comes from the raw input instead of args.
            string text = input.Trim();
            if (text.StartsWith('/')) text = text[1..];
            int firstSpace = text.IndexOf(' ');
            int secondSpace = firstSpace < 0 ? -1 : text.IndexOf(' ', firstSpace + 1);
            text = secondSpace < 0 ? "" : text[(secondSpace + 1)..].Trim();
            if (text.Length == 0 || text.Length > 500)
                Reject(caller, "Use /tpa goal <text>, 1-500 characters.");
            else if (!ModContent.GetInstance<LocalTestBridge>().QueueGoal(text, out string queued))
                Reject(caller, queued);
            else caller.Reply(queued);
            return;
        }
        bool movement = action is "left" or "right";
        bool attack = action == "attack";
        bool runJump = action == "jump" && args.Length > 1;
        bool fixture = action is "equip" or "enemy" or "clear-enemies";
        if (args.Length > (action == "mine" ? 4 : attack || runJump || action == "goto" ? 3 : movement || action is "equip" or "enemy" or "down" or "select" or "range" or "home" ? 2 : 1))
        {
            Reject(caller, Usage);
            return;
        }
        var system = ModContent.GetInstance<DemoPlayerSystem>();
        if (fixture)
        {
            if (!ModContent.GetInstance<LocalTestBridge>().Enabled)
            {
                Reject(caller, "Test equipment and enemies require -tpatest (scripts/play.ps1).");
                return;
            }
            if (action == "clear-enemies") { system.ClearEnemies(); caller.Reply("Test enemies cleared."); }
            else if (args.Length != 2) Reject(caller, Usage);
            else if (system.Actor == null || system.Actor.dead)
                Reject(caller, "No living demo. Spawn one or wait for automatic respawn.");
            else if ((action == "equip" && args[1] is not ("wood" or "iron" or "pick")) ||
                (action == "enemy" && args[1] is not ("slime" or "zombie" or "eye")))
                Reject(caller, Usage);
            else caller.Reply(action == "equip" ? system.Equip(args[1]) : system.SpawnEnemy(args[1]));
        }
        else if (action == "spawn")
        {
            caller.Reply(system.Spawn(caller.Player));
            LastAccepted = system.Actor != null;
        }
        else if (action == "status")
            caller.Reply(system.Status(caller.Player));
        else if (action == "range")
        {
            int tiles = 0;
            if (args.Length != 2 || (args[1] != "off" && (!int.TryParse(args[1], out tiles) || tiles < 1)) ||
                !system.SetRange(tiles)) Reject(caller, "Use /tpa range 1-100 (tiles) or off on a living AI.");
            else caller.Reply(tiles == 0 ? "Activity range disabled." : $"Activity range set to current X +/- {tiles} tiles; this point is home.");
        }
        else if (action is "goto" or "home")
        {
            double seconds = 10;
            float x = 0;
            int durationIndex = action == "goto" ? 2 : 1;
            if ((action == "goto" && (args.Length < 2 || !float.TryParse(args[1], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out x) || !float.IsFinite(x))) ||
                (args.Length > durationIndex && !double.TryParse(args[durationIndex], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out seconds)) || !double.IsFinite(seconds) || seconds <= 0 || seconds > 10)
                Reject(caller, "Use /tpa goto worldPixelX [seconds] or home [seconds], > 0 and <= 10 seconds.");
            else if (!system.MoveTo(action == "goto" ? x : null, (int)Math.Ceiling(seconds * 60), out string message))
                Reject(caller, message);
            else caller.Reply(message);
        }
        else if (action == "select")
        {
            if (args.Length != 2 || !int.TryParse(args[1], out int slot) || !system.SelectItem(slot))
                Reject(caller, "Select a nonempty inventory slot 0-49 on a living AI.");
            else caller.Reply($"Selected AI inventory slot {slot}.");
        }
        else if (action == "mine")
        {
            double seconds = 2;
            if (system.Actor == null || system.Actor.dead)
                Reject(caller, "No living demo. Spawn one or wait for automatic respawn.");
            else if (args.Length < 3 || !int.TryParse(args[1], out int x) || !int.TryParse(args[2], out int y) ||
                (args.Length > 3 && !double.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)) ||
                !double.IsFinite(seconds) || seconds <= 0 || seconds > 10)
                Reject(caller, "Use /tpa mine tileX tileY [seconds], > 0 and <= 10 seconds.");
            else if (!ModContent.GetInstance<NativeMining>().Start(system.Actor, x, y,
                (int)Math.Ceiling(seconds * 60), out string message))
                Reject(caller, message);
            else caller.Reply(message);
        }
        else if (action == "remove")
        {
            ModContent.GetInstance<LocalTestBridge>().CancelPendingGoal();
            system.Remove();
            caller.Reply("Demo removed.");
        }
        else if (action == "stop")
        {
            ModContent.GetInstance<LocalTestBridge>().CancelPendingGoal();
            system.RequestStop();
            caller.Reply("Demo input stopped (gravity and inertia still apply).");
        }
        else if (movement || attack || action is "jump" or "down")
        {
            if (system.Actor == null || system.Actor.dead)
            {
                Reject(caller, system.Actor == null ? "No living demo. Use /tpa spawn." :
                    "No living demo. Waiting for automatic respawn.");
                return;
            }
            if (action == "jump" && !runJump)
            {
                system.Jump();
                caller.Reply("Jump requested.");
                return;
            }
            if ((attack || runJump) && (args.Length < 2 || args[1] is not ("left" or "right")))
            {
                Reject(caller, "Use /tpa attack or jump left/right [seconds].");
                return;
            }
            if (attack && system.Actor.HeldItem.type is not (ItemID.WoodenSword or ItemID.IronBroadsword))
            {
                Reject(caller, "Equip a supported vanilla sword first: /tpa equip wood/iron.");
                return;
            }
            double seconds = action == "down" ? 0.25 : 2;
            int durationIndex = attack || runJump ? 2 : 1;
            if (args.Length > durationIndex && (!double.TryParse(args[durationIndex], NumberStyles.Float,
                CultureInfo.InvariantCulture, out seconds) || !double.IsFinite(seconds) || seconds <= 0 || seconds > 10))
            {
                Reject(caller, "Duration must be greater than 0 and at most 10 seconds (example: /tpa right 2).");
                return;
            }
            int ticks = (int)Math.Ceiling(seconds * 60);
            if (attack) system.Attack(args[1] == "left" ? -1 : 1, ticks);
            else if (action == "down") system.Drop(ticks);
            else
            {
                system.Move((runJump ? args[1] : action) == "left" ? -1 : 1, ticks);
                if (runJump) system.Jump();
            }
            caller.Reply($"{(attack ? "Attacking " + args[1] : runJump ? "Jumping " + args[1] : "Moving " + action)} for {seconds} simulation seconds.");
        }
        else
            Reject(caller, Usage);
    }

    private void Reject(CommandCaller caller, string message)
    {
        LastAccepted = false;
        caller.Reply(message, Color.Orange);
    }
}
