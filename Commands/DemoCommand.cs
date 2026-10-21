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
    public override string Command => "tpa";
    public override CommandType Type => CommandType.Chat;
    public override string Usage => "/tpa spawn | left/right [seconds] | jump | attack left/right [seconds] | stop | status | remove | equip wood/iron | enemy slime/zombie/eye | clear-enemies";
    public override string Description => "Control the independent player demo (single-player only).";

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        ExecutionCount++;
        LastAction = args.Length > 0 ? args[0] : "";
        LastCaller = caller.GetType().Name;
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
        bool attack = action == "attack";
        bool fixture = action is "equip" or "enemy" or "clear-enemies";
        if (args.Length > (attack ? 3 : movement || action is "equip" or "enemy" ? 2 : 1))
        {
            caller.Reply(Usage, Color.Orange);
            return;
        }
        var system = ModContent.GetInstance<DemoPlayerSystem>();
        if (fixture)
        {
            if (!ModContent.GetInstance<LocalTestBridge>().Enabled)
            {
                caller.Reply("Test equipment and enemies require -tpatest (scripts/play.ps1).", Color.Orange);
                return;
            }
            if (action == "clear-enemies") { system.ClearEnemies(); caller.Reply("Test enemies cleared."); }
            else if (args.Length != 2) caller.Reply(Usage, Color.Orange);
            else caller.Reply(action == "equip" ? system.Equip(args[1]) : system.SpawnEnemy(args[1]));
        }
        else if (action == "spawn")
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
        else if (movement || attack || action == "jump")
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
            if (attack && (args.Length < 2 || args[1] is not ("left" or "right")))
            {
                caller.Reply("Use /tpa attack left/right [seconds].", Color.Orange);
                return;
            }
            if (attack && system.Actor.HeldItem.type is not (ItemID.WoodenSword or ItemID.IronBroadsword))
            {
                caller.Reply("Equip a supported vanilla sword first: /tpa equip wood/iron.", Color.Orange);
                return;
            }
            double seconds = 2;
            int durationIndex = attack ? 2 : 1;
            if (args.Length > durationIndex && (!double.TryParse(args[durationIndex], NumberStyles.Float,
                CultureInfo.InvariantCulture, out seconds) || !double.IsFinite(seconds) || seconds <= 0 || seconds > 10))
            {
                caller.Reply("Duration must be greater than 0 and at most 10 seconds (example: /tpa right 2).", Color.Orange);
                return;
            }
            if (attack) system.Attack(args[1] == "left" ? -1 : 1, (int)Math.Ceiling(seconds * 60));
            else system.Move(action == "left" ? -1 : 1, (int)Math.Ceiling(seconds * 60));
            caller.Reply($"{(attack ? "Attacking " + args[1] : "Moving " + action)} for {seconds} simulation seconds.");
        }
        else
            caller.Reply(Usage, Color.Orange);
    }
}
