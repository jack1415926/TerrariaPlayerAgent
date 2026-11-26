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
        CheckTestEquipGate();
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
        Check("iron broadsword swing is accepted",
            MultiplayerAttackPolicy.Validate(true, true, true, true, MultiplayerAttackPolicy.IronBroadsword, 1, 1) == null);
        Check("dummy goal cannot substitute copper shortsword for the requested iron broadsword",
            MultiplayerAttackPolicy.ValidateDummyGoal("切换铁阔剑，然后攻击傀儡", StartingKit.CopperShortsword) != null);
        Check("dummy goal requires the copper shortsword to be selected",
            MultiplayerAttackPolicy.ValidateDummyGoal("攻击最近的傀儡", MultiplayerAttackPolicy.IronBroadsword) != null);
        Check("dummy goal accepts attacking with the selected copper shortsword",
            MultiplayerAttackPolicy.ValidateDummyGoal("用手上的铜短剑打中傀儡一次", StartingKit.CopperShortsword) == null);
        Check("unsupported attack item is rejected",
            MultiplayerAttackPolicy.Validate(true, true, true, true, 24, 1, 1) != null);
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

    private static void CheckTestEquipGate()
    {
        string name = MultiplayerTestEquipPolicy.EnvironmentVariable;
        string? previous = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, null);
            Check("test equipment defaults off", !MultiplayerTestEquipPolicy.Enabled);
            Environment.SetEnvironmentVariable(name, "0");
            Check("test equipment is off for value 0", !MultiplayerTestEquipPolicy.Enabled);
            Environment.SetEnvironmentVariable(name, "1");
            Check("test equipment is on only for value 1", MultiplayerTestEquipPolicy.Enabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, previous);
        }

        string? root = FindRepoRoot();
        Check("repository root is reachable for the test gate", root != null);
        if (root == null) return;

        string probe = File.ReadAllText(Path.Combine(root, "Common", "MultiplayerProbe.cs"));
        int start = probe.IndexOf("action == \"equip_iron\"", StringComparison.Ordinal);
        int end = start < 0 ? -1 : probe.IndexOf("else if (action is \"attack_left\"", start, StringComparison.Ordinal);
        string section = start >= 0 && end > start ? probe[start..end] : "";
        int gate = section.IndexOf("MultiplayerTestEquipPolicy.Enabled", StringComparison.Ordinal);
        int cancel = section.IndexOf("CancelGoal();", StringComparison.Ordinal);
        int equip = section.IndexOf("Demo.Equip(\"iron\")", StringComparison.Ordinal);
        Check("server rejects equip before controller or inventory work", gate >= 0 && gate < cancel && gate < equip);
        Check("disabled equip uses the explicit server message",
            section.Contains("MultiplayerTestEquipPolicy.DisabledMessage", StringComparison.Ordinal));

        string command = File.ReadAllText(Path.Combine(root, "Commands", "MultiplayerProbeCommand.cs"));
        Check("client command does not read the server test flag",
            !command.Contains("TPA_MP_TEST_EQUIP", StringComparison.Ordinal));
        string play = File.ReadAllText(Path.Combine(root, "scripts", "play.ps1"));
        Check("play.ps1 exposes an explicit test-equip switch",
            play.Contains("[switch]$MultiplayerTestEquip", StringComparison.Ordinal));
        int condition = play.IndexOf("if ($MultiplayerTestEquip)", StringComparison.Ordinal);
        int environment = play.IndexOf("TPA_MP_TEST_EQUIP", StringComparison.Ordinal);
        Check("play.ps1 enables test equip only under its switch", condition >= 0 && environment > condition);
        Check("normal MultiplayerAgent alone does not enable test equip",
            !play.Contains("if ($MultiplayerAgent) { $environment.TPA_MP_TEST_EQUIP", StringComparison.Ordinal));
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

        string projectileHit = MethodBody(demo, "internal void RecordProjectileHit(Projectile projectile, NPC npc, int damage)");
        Check("projectile hit records the held item at hit, not as source",
            projectileHit.Contains("heldItemAtHit = Actor.HeldItem.type", StringComparison.Ordinal) &&
            !projectileHit.Contains("item = Actor.HeldItem.type", StringComparison.Ordinal));
        Check("projectile hit records owner and projectile type",
            projectileHit.Contains("projectileOwner = projectile.owner", StringComparison.Ordinal) &&
            projectileHit.Contains("projectile = projectile.type", StringComparison.Ordinal));
        Check("attack input, item hit and projectile hit remain distinct events",
            demo.Contains("kind = \"attack-input\"", StringComparison.Ordinal) &&
            demo.Contains("kind = \"item-hit\"", StringComparison.Ordinal) &&
            demo.Contains("kind = \"projectile-hit\"", StringComparison.Ordinal));
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
        Check("equip iron is an explicit test fixture",
            command.Contains("args[1] == \"iron\"", StringComparison.Ordinal) &&
            command.Contains("Request(\"equip_iron\")", StringComparison.Ordinal));
        Check("server equip fixture validates controller and authority",
            probe.Contains("action == \"equip_iron\"", StringComparison.Ordinal) &&
            probe.Contains("NativeCombat.IsAuthoritativeActor(Demo.Actor)", StringComparison.Ordinal) &&
            probe.Contains("Demo.Equip(\"iron\")", StringComparison.Ordinal));

        string combat = File.ReadAllText(Path.Combine(root, "Common", "NativeCombat.cs"));
        Check("server projectile guard is hooked",
            combat.Contains("IL_Player.ItemCheck_Shoot += ProjectileStart;", StringComparison.Ordinal) &&
            combat.Contains("IL_Player.ItemCheck_Shoot -= ProjectileStart;", StringComparison.Ordinal));
        Check("client mirrors are not authoritative",
            combat.Contains("Main.netMode is NetmodeID.SinglePlayer or NetmodeID.Server", StringComparison.Ordinal));
        Check("server copper shortsword is the ranged rule",
            combat.Contains("ItemID.CopperShortsword", StringComparison.Ordinal) &&
            combat.Contains("OwnerRule.Ranged", StringComparison.Ordinal));
        Check("server iron broadsword is the swing rule",
            combat.Contains("IsServerSwing", StringComparison.Ordinal) &&
            combat.Contains("ItemID.IronBroadsword", StringComparison.Ordinal) &&
            combat.Contains("ItemUseStyleID.Swing", StringComparison.Ordinal) &&
            combat.Contains("!player.HeldItem.noMelee", StringComparison.Ordinal) &&
            combat.Contains("player.HeldItem.shoot <= ProjectileID.None", StringComparison.Ordinal));
        Check("server swing is allowed by both input and melee owner rules",
            combat.Contains("OwnerRule.Melee => IsSinglePlayerMelee(player) || IsServerSwing(player)", StringComparison.Ordinal) &&
            combat.Contains("OwnerRule.Supported => IsSinglePlayerMelee(player) || IsServerSwing(player)", StringComparison.Ordinal));
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
