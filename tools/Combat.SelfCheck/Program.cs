using System;
using System.IO;
using TerrariaPlayerAgent.Common;

namespace TerrariaPlayerAgent.SelfCheck;

// Offline checks for the manual multiplayer attack policy and its lifecycle contracts.
// The source checks prove call order; they do not execute Terraria or simulate a hit.
internal static class Program
{
    private static int failures, checks;

    private static int Main()
    {
        CheckCommandParsing();
        CheckDirectionsAndValidation();
        CheckDemoLifecycleContracts();
        CheckProbeAndNativeContracts();

        Console.WriteLine(failures == 0
            ? $"SELF-CHECK OK ({checks} checks)"
            : $"SELF-CHECK FAILED ({failures}/{checks})");
        return failures == 0 ? 0 : 1;
    }

    private static void CheckCommandParsing()
    {
        Check("attack left defaults to one second",
            MultiplayerAttackPolicy.TryParseCommand(new[] { "attack", "left" }, out string left, out int leftSeconds) &&
            left == "attack_left" && leftSeconds == 1);
        Check("attack right 1 is accepted",
            MultiplayerAttackPolicy.TryParseCommand(new[] { "attack", "right", "1" }, out string right, out int rightSeconds) &&
            right == "attack_right" && rightSeconds == 1);
        Check("attack duration 2 is rejected",
            !MultiplayerAttackPolicy.TryParseCommand(new[] { "attack", "left", "2" }, out _, out _));
        Check("attack duration 0 is rejected",
            !MultiplayerAttackPolicy.TryParseCommand(new[] { "attack", "left", "0" }, out _, out _));
        Check("attack direction is required",
            !MultiplayerAttackPolicy.TryParseCommand(new[] { "attack" }, out _, out _));
        Check("attack direction must be left or right",
            !MultiplayerAttackPolicy.TryParseCommand(new[] { "attack", "up", "1" }, out _, out _));
        Check("attack rejects extra arguments",
            !MultiplayerAttackPolicy.TryParseCommand(new[] { "attack", "left", "1", "x" }, out _, out _));
        Check("non-attack action is rejected",
            !MultiplayerAttackPolicy.TryParseCommand(new[] { "left", "1" }, out _, out _));
    }

    private static void CheckDirectionsAndValidation()
    {
        Check("attack_left maps to -1",
            MultiplayerAttackPolicy.TryDirection("attack_left", out int left) && left == -1);
        Check("attack_right maps to 1",
            MultiplayerAttackPolicy.TryDirection("attack_right", out int right) && right == 1);
        Check("unknown action has no direction",
            !MultiplayerAttackPolicy.TryDirection("left", out _));

        Check("valid controller, AI, item and range are accepted",
            MultiplayerAttackPolicy.Validate(true, true, true, true, StartingKit.CopperShortsword, -1, 1) == null);
        Check("non-controller is rejected",
            MultiplayerAttackPolicy.Validate(false, true, true, true, StartingKit.CopperShortsword, 1, 1) != null);
        Check("controller mismatch is rejected",
            MultiplayerAttackPolicy.Validate(true, false, true, true, StartingKit.CopperShortsword, 1, 1) != null);
        Check("dead or missing AI is rejected",
            MultiplayerAttackPolicy.Validate(true, true, false, true, StartingKit.CopperShortsword, 1, 1) != null);
        Check("non-authoritative actor is rejected",
            MultiplayerAttackPolicy.Validate(true, true, true, false, StartingKit.CopperShortsword, 1, 1) != null);
        Check("non-copper item is rejected",
            MultiplayerAttackPolicy.Validate(true, true, true, true, 0, 1, 1) != null);
        Check("invalid direction is rejected",
            MultiplayerAttackPolicy.Validate(true, true, true, true, StartingKit.CopperShortsword, 0, 1) != null);
        Check("invalid duration is rejected",
            MultiplayerAttackPolicy.Validate(true, true, true, true, StartingKit.CopperShortsword, 1, 2) != null);
    }

    private static void CheckDemoLifecycleContracts()
    {
        string? root = FindRepoRoot();
        Check("repository root is reachable", root != null);
        if (root == null) return;
        string demo = File.ReadAllText(Path.Combine(root, "Common", "DemoPlayerSystem.cs"));

        string attack = MethodBody(demo, "internal void Attack(int direction, int ticks)");
        int attackStop = attack.IndexOf("Stop();", StringComparison.Ordinal);
        int attackSet = attack.IndexOf("attackTicks = ticks;", StringComparison.Ordinal);
        Check("Attack clears prior input before setting a new attack", attackStop >= 0 && attackSet > attackStop);
        Check("Attack sets the bounded tick count", attack.Contains("attackTicks = ticks;", StringComparison.Ordinal));

        string stop = MethodBody(demo, "internal void Stop()");
        Check("Stop clears attack ticks", stop.Contains("attackTicks", StringComparison.Ordinal) &&
            stop.Contains("ClearInput", StringComparison.Ordinal));
        string requestStop = MethodBody(demo, "internal void RequestStop()");
        Check("RequestStop clears current input", requestStop.Contains("Stop();", StringComparison.Ordinal));
        string remove = MethodBody(demo, "internal void Remove()");
        Check("Remove stops current input", remove.Contains("RequestStop();", StringComparison.Ordinal));
        Check("Death stops current input",
            MethodBody(demo, "internal void RecordDeath(PlayerDeathReason reason)").Contains("Stop();", StringComparison.Ordinal));
        Check("Respawn stops current input",
            MethodBody(demo, "internal void RecordRespawn()").Contains("Stop();", StringComparison.Ordinal));

        string update = MethodBody(demo, "public override void PreUpdatePlayers()");
        Check("only positive attackTicks produce attack input",
            update.Contains("attackTicks > 0", StringComparison.Ordinal) &&
            update.Contains("controlUseItem = true", StringComparison.Ordinal));
        string itemHit = MethodBody(demo, "public override void OnHitNPCWithItem(Item item, NPC target, NPC.HitInfo hit, int damageDone)");
        Check("item hit recording uses the authoritative actor check",
            itemHit.Contains("NativeCombat.IsAuthoritativeActor(Player)", StringComparison.Ordinal));
    }

    private static void CheckProbeAndNativeContracts()
    {
        string? root = FindRepoRoot();
        if (root == null) return;
        string probe = File.ReadAllText(Path.Combine(root, "Common", "MultiplayerProbe.cs"));
        int start = probe.IndexOf("action is \"attack_left\" or \"attack_right\"", StringComparison.Ordinal);
        int end = start < 0 ? -1 : probe.IndexOf("else if (action == \"stop\")", start, StringComparison.Ordinal);
        string section = start >= 0 && end > start ? probe[start..end] : "";
        Check("probe validates the attack policy", section.Contains("MultiplayerAttackPolicy.Validate(", StringComparison.Ordinal));
        int cancel = section.IndexOf("CancelGoal();", StringComparison.Ordinal);
        int stop = section.IndexOf("Demo.RequestStop();", StringComparison.Ordinal);
        int attack = section.IndexOf("Demo.Attack(", StringComparison.Ordinal);
        int record = section.IndexOf("Demo.RecordAttackStart(", StringComparison.Ordinal);
        Check("accepted attack cancels the old goal first", cancel >= 0 && cancel < stop);
        Check("accepted attack clears input before applying attack", stop >= 0 && stop < attack);
        Check("accepted attack records its input event", attack >= 0 && attack < record);

        string bridge = File.ReadAllText(Path.Combine(root, "Common", "MultiplayerAgentBridge.cs"));
        string queue = MethodBody(bridge, "internal bool QueueGoal(int sender, string text, out string message)");
        Check("new natural-language goal stops old input", queue.Contains("Demo.RequestStop();", StringComparison.Ordinal));

        string command = File.ReadAllText(Path.Combine(root, "Commands", "MultiplayerProbeCommand.cs"));
        Check("command routes attack through the probe",
            command.Contains("MultiplayerAttackPolicy.TryParseCommand(args", StringComparison.Ordinal) &&
            command.Contains("Request(attackAction, seconds)", StringComparison.Ordinal));

        string combat = File.ReadAllText(Path.Combine(root, "Common", "NativeCombat.cs"));
        Check("server projectile guard is hooked",
            combat.Contains("IL_Player.ItemCheck_Shoot += ProjectileStart;", StringComparison.Ordinal) &&
            combat.Contains("IL_Player.ItemCheck_Shoot -= ProjectileStart;", StringComparison.Ordinal));
        Check("client mirrors are not authoritative",
            combat.Contains("Main.netMode is NetmodeID.SinglePlayer or NetmodeID.Server", StringComparison.Ordinal));
        Check("server copper shortsword is the ranged rule",
            combat.Contains("ItemID.CopperShortsword", StringComparison.Ordinal) &&
            combat.Contains("OwnerRule.Ranged", StringComparison.Ordinal));
        Check("single-player melee and mining rules remain",
            combat.Contains("IsSinglePlayerMelee", StringComparison.Ordinal) &&
            combat.Contains("ItemID.WoodenSword or ItemID.IronBroadsword", StringComparison.Ordinal) &&
            combat.Contains("NativeMining.Active(player)", StringComparison.Ordinal));
        Check("authority requires object ownership and an unclaimed slot",
            combat.Contains("ReferenceEquals(Main.player[player.whoAmI], player)", StringComparison.Ordinal) &&
            combat.Contains("Netplay.Clients[player.whoAmI].State == 0", StringComparison.Ordinal));

        string hitProbe = File.ReadAllText(Path.Combine(root, "Common", "CombatHitProbe.cs"));
        Check("server projectile hits are recorded on the authoritative side",
            hitProbe.Contains("OnHitByProjectile", StringComparison.Ordinal) &&
            hitProbe.Contains("Main.netMode != NetmodeID.Server", StringComparison.Ordinal) &&
            hitProbe.Contains("RecordProjectileHit", StringComparison.Ordinal));
    }

    private static string? FindRepoRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "TerrariaPlayerAgent.csproj")))
                return dir.FullName;
        return null;
    }

    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0) return "";
        int open = source.IndexOf('{', start);
        if (open < 0) return "";
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[start..(i + 1)];
        }
        return "";
    }

    private static void Check(string name, bool ok)
    {
        checks++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
        if (!ok) failures++;
    }
}
