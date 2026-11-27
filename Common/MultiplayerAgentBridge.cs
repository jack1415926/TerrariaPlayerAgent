using System;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerrariaPlayerAgent.Common;

// The default bridge is read-only; goal-bound actions require a separate opt-in.
// Clients must never consume it; only the authoritative server answers.
public sealed class MultiplayerAgentBridge : ModSystem
{
    public const string ServerAgentMode = "multiplayer_server";
    private int pollTicks;
    private string directory = "";
    private Goal? activeGoal;
    private string? latestGoalId;
    private CombatTarget? goalTarget;
    private object? goalHit;
    private bool targetLost, retreatStarted;
    private int goalAttackSequence, goalAttackAttempts, goalAttackDirection;
    private float combatOriginX;
    private readonly MultiplayerCompanion companion = new();
    private DemoPlayerSystem Demo => ModContent.GetInstance<DemoPlayerSystem>();
    private MultiplayerProbe Probe => ModContent.GetInstance<MultiplayerProbe>();
    internal bool Enabled => Program.LaunchParameters.ContainsKey("-tpatest") ||
        Environment.GetEnvironmentVariable("TPA_MP_READONLY_BRIDGE") == "1";
    internal bool ActionsEnabled => Enabled && Environment.GetEnvironmentVariable("TPA_MP_AGENT") == "1";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public override void OnModLoad()
    {
        if (!Enabled) return;
        directory = Path.Combine(Main.SavePath, "TPA");
        Directory.CreateDirectory(directory);
        Mod.Logger.Info($"[TPA-MP] mailbox={Path.GetFullPath(directory)}");
    }

    public override void PostUpdateEverything()
    {
        if (directory.Length == 0) return;
        // PostUpdateEverything also runs on clients; only the server may consume the mailbox.
        if (Main.netMode != NetmodeID.Server) return;
        if (++pollTicks % 3 != 0) return;
        if (activeGoal is { } goal)
        {
            bool sameIdentity = Matches(goal);
            if (!sameIdentity || !TryHost(out string session) || session != goal.hostSession)
            {
                // A bounded host publishes its terminal result before deleting its heartbeat.
                if (sameIdentity) ReportGoals();
                if (ReferenceEquals(activeGoal, goal))
                {
                    CancelGoal();
                    Demo.RequestStop();
                    Probe.NotifyController(sameIdentity ? "目标已取消：宿主已离线。" :
                        "目标已取消：AI、控制者或世界已变化。");
                }
            }
        }
        if (goalTarget != null && goalHit == null && !targetLost && ResolveTarget() == null)
        {
            targetLost = true;
            Demo.Stop();
        }
        ProcessRequest();
        ReportGoals();
        companion.Update(Demo, Probe);
    }

    private string WorldId => ModContent.GetInstance<WorldSession>().Id;
    internal string MailboxDirectory => directory;
    internal string? CurrentGoalId => activeGoal?.id;
    internal string? CompanionStatus => ActionsEnabled ? companion.Status : null;
    internal void StopGoal() { CancelGoal(); Demo.RequestStop(); }

    internal bool QueueGoal(int sender, string text, out string message)
    {
        message = "Goal entry requires play.ps1 -MultiplayerAgent.";
        if (!ActionsEnabled || directory.Length == 0 || Main.netMode != NetmodeID.Server) return false;
        message = "Only the current AI controller can submit a goal.";
        if (!Probe.ControllerValid || Probe.ControllerSlot != sender) return false;
        message = "Prepare a living AI and a goal of 1-500 characters.";
        text = text.Trim();
        if (text.Length is < 1 or > 500 || Demo.Actor is not { dead: false } actor ||
            !NativeCombat.IsAuthoritativeActor(actor)) return false;
        if (MultiplayerCompanion.Parse(text) is { } command)
        {
            if (command != MultiplayerCompanion.Command.Wait && Main.player[sender].dead)
            {
                message = "[TPA] 请先重生，再叫 AI 跟随或回来。";
                return false;
            }
            StopGoal();
            message = companion.Start(command, Demo, Probe.ControllerToken!);
            return true;
        }
        message = "Start run-agent.ps1 -HostMode -AgentMode multiplayer_server first.";
        if (!TryHost(out string hostSession)) return false;
        CombatTarget? target = null;
        int? weapon = null;
        bool wantsDummy = text.Contains("傀儡") || text.Contains("假人");
        bool wantsSlime = text.Contains("史莱姆");
        if ((wantsDummy || wantsSlime) && (text.Contains("打") || text.Contains("攻击") || text.Contains("刺") ||
            text.Contains("击中") || text.Contains("命中") || text.Contains("杀") || text.Contains("消灭")))
        {
            if (wantsDummy && wantsSlime)
            {
                message = "每个战斗目标只能指定一个傀儡或一只蓝史莱姆。";
                return false;
            }
            if (wantsSlime && ((!text.Contains("蓝史莱姆") && !text.Contains("蓝色史莱姆")) ||
                text.Contains("杀") || text.Contains("消灭") || text.Contains("打死")))
            {
                message = "活动敌怪目标目前支持「命中一只蓝史莱姆，然后退避」。";
                return false;
            }
            weapon = MultiplayerAttackPolicy.ResolveGoalWeapon(text, Demo.Actor.HeldItem.type);
            if (weapon == 0)
            {
                message = "请指定一把受支持的武器（铜短剑或铁阔剑），或持有其中一把后再下令。";
                return false;
            }
            if (!Demo.Actor.inventory.Take(50).Any(item => !item.IsAir && item.type == weapon))
            {
                message = $"AI 背包里没有{WeaponName(weapon.Value)}。";
                return false;
            }
            Vector2 origin = Demo.Actor.Center;
            int type = wantsDummy ? NPCID.TargetDummy : NPCID.BlueSlime;
            int range = wantsDummy ? 480 : 240;
            var npc = Main.npc.Where(n => n.active && n.type == type && n.life > 0 &&
                (wantsDummy || n.netID == NPCID.BlueSlime) &&
                Vector2.DistanceSquared(n.Center, origin) <= range * range)
                .OrderBy(n => Vector2.DistanceSquared(n.Center, origin)).ThenBy(n => n.whoAmI).FirstOrDefault();
            if (npc == null)
            {
                message = wantsDummy ? "没有发现已激活的固定傀儡。请让真人靠近傀儡，并让傀儡离 AI 不超过 480 像素。" :
                    "AI 周围 240 像素内没有蓝史莱姆。";
                return false;
            }
            target = new CombatTarget(npc.whoAmI, npc, type, npc.GetGlobalNPC<CombatHitProbe>().SpawnIdentity, npc.ai[0], npc.ai[1]);
        }
        CancelGoal();
        Demo.RequestStop();
        var goal = new Goal(Guid.NewGuid().ToString("N"), text, ServerAgentMode, Demo.StopRevision,
            WorldId, Demo.ActorGeneration, Probe.ControllerToken!, hostSession, target?.Slot, weapon);
        try
        {
            string temporary = Path.Combine(directory, "goal.tmp");
            File.WriteAllText(temporary, JsonSerializer.Serialize(goal, Json));
            File.Move(temporary, Path.Combine(directory, "goal.json"), true);
            activeGoal = goal;
            goalTarget = target;
            combatOriginX = Demo.Actor.position.X;
            latestGoalId = goal.id;
            if (target != null)
                Mod.Logger.Info($"[TPA-MP] goal={goal.id}, npc={target.Slot}, type={target.Type}, weapon={weapon}, distance={Vector2.Distance(Demo.Actor.Center, target.Npc.Center):F1}px");
            message = $"[TPA {goal.id[..6]}] 目标已提交，等待宿主接单。";
            return true;
        }
        catch (IOException)
        {
            message = "Goal mailbox is busy; previous input was cancelled. Submit again.";
            return false;
        }
    }

    private bool TryHost(out string hostSession)
    {
        hostSession = "";
        try
        {
            var file = new FileInfo(Path.Combine(directory, "host-ready.json"));
            if (!file.Exists || (DateTime.UtcNow - file.LastWriteTimeUtc).TotalSeconds is < 0 or >= 15) return false;
            using var doc = JsonDocument.Parse(File.ReadAllText(file.FullName));
            if (Text(doc.RootElement, "agentMode") != ServerAgentMode) return false;
            hostSession = Text(doc.RootElement, "hostSession");
            return Guid.TryParse(hostSession, out _);
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return false; }
    }

    private bool Matches(Goal goal) => Probe.ControllerValid && Demo.Actor is { active: true, dead: false } &&
        goal.worldSession == WorldId && goal.actorGeneration == Demo.ActorGeneration &&
        goal.controllerToken == Probe.ControllerToken && goal.stopRevision == Demo.StopRevision;

    private bool IsSlimeGoal => goalTarget?.Type == NPCID.BlueSlime;

    private static string WeaponName(int type) => type == ItemID.CopperShortsword ? "铜短剑" : "铁阔剑";

    private bool IsBoundTarget(NPC npc) => !targetLost && goalTarget is { } target &&
        ReferenceEquals(npc, target.Npc) && npc.type == target.Type &&
        ReferenceEquals(npc.GetGlobalNPC<CombatHitProbe>().SpawnIdentity, target.SpawnIdentity) &&
        (IsSlimeGoal ? npc.netID == NPCID.BlueSlime : npc.ai[0] == target.TileX && npc.ai[1] == target.TileY);

    private NPC? ResolveTarget() => goalTarget is { } target && Main.npc[target.Slot] is { active: true } npc &&
        IsBoundTarget(npc) ? npc : null;

    internal void ObserveAttackHit(NPC npc, int attackSequence, int itemType, int damage, object hit)
    {
        // Native hit hooks can report a killing blow after the NPC has gone inactive.
        if (activeGoal == null || !Matches(activeGoal) || goalHit != null || goalAttackAttempts == 0 || damage <= 0 ||
            attackSequence != goalAttackSequence || itemType != activeGoal.attackWeapon || !IsBoundTarget(npc)) return;
        goalHit = hit;
        Demo.Stop();
    }

    internal void CancelGoal()
    {
        companion.Clear();
        if (activeGoal != null)
        {
            string file = Path.Combine(directory, "goal.json");
            try
            {
                if (File.Exists(file))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(file));
                    if (Text(doc.RootElement, "id") == activeGoal.id) File.Delete(file);
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException) { }
        }
        ClearGoal();
    }

    private void ClearGoal()
    {
        activeGoal = null;
        latestGoalId = null;
        goalTarget = null;
        goalHit = null;
        targetLost = retreatStarted = false;
        goalAttackSequence = goalAttackAttempts = goalAttackDirection = 0;
    }

    public override void OnWorldUnload() => CancelGoal();

    private static string Text(JsonElement root, string key) => root.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private void ReportGoals()
    {
        foreach (string file in Directory.GetFiles(directory, "goal-ack-*.json")) ReportGoal(file, true);
        foreach (string file in Directory.GetFiles(directory, "goal-result-*.json")) ReportGoal(file, false);
    }

    private void ReportGoal(string file, bool ack)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            if (Text(root, "agentMode") != ServerAgentMode) return;
            string id = Text(root, "id"), status = Text(root, "status");
            File.Delete(file);
            ModContent.GetInstance<MultiplayerGoalAcceptance>().Observe(id, ack, status);
            if (id != latestGoalId || !Probe.ControllerValid) return;
            string detail = Text(root, ack ? "reason" : "summary");
            if (detail.Length > 500) detail = detail[..500];
            string message = ack && status == "accepted" ? "宿主已接单，正在处理。" :
                ack ? $"目标未执行：{status}。{detail}" :
                status == "succeeded" ? detail :
                status == "finished" ? $"模型已结束，请以实际行动结果为准：{detail}" : $"目标结束：{status}。{detail}";
            Probe.NotifyController($"[TPA {id[..Math.Min(id.Length, 6)]}] {message}");
            Mod.Logger.Info($"[TPA-MP] goal={id}, ack={ack}, status={status}");
            if (!ack || status is "rejected" or "stale") ClearGoal();
        }
        catch (Exception ex) when (ex is IOException or JsonException) { }
    }

    private void ProcessRequest()
    {
        string path = Path.Combine(directory, "request.json");
        if (!File.Exists(path)) return;

        string id = "";
        try
        {
            var request = JsonSerializer.Deserialize<Request>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Empty request.");
            File.Delete(path);
            id = request.id;
            if (!Guid.TryParse(id, out _) || request.command.Length > 700)
                throw new InvalidDataException("Invalid request id or command.");
            string command = request.command.Trim();
            if (command == "status")
                Respond(id, new { id, ok = true, accepted = true, reply = "Snapshot", state = Snapshot() });
            else if (command == "test-goals" && ActionsEnabled && activeGoal == null && !companion.Active &&
                request.agentMode == ServerAgentMode && Probe.ControllerValid &&
                request.controllerToken == Probe.ControllerToken && request.worldSession == WorldId &&
                request.actorGeneration == Demo.ActorGeneration && request.expectedStopRevision == Demo.StopRevision)
            {
                string testId = ModContent.GetInstance<MultiplayerGoalAcceptance>().Start();
                Respond(id, new { id, ok = true, accepted = true, reply = "Acceptance started", testId });
            }
            else if (!ActionsEnabled)
                throw new InvalidOperationException("Read-only mode; only status is allowed.");
            else if (!OwnsRequest(request))
                Respond(id, new { id, ok = true, accepted = false, interrupted = true,
                    reply = "Goal ownership expired; no action executed.", state = Snapshot() });
            else
            {
                try
                {
                    string reply = Execute(command);
                    Respond(id, new { id, ok = true, accepted = true, reply, state = Snapshot() });
                }
                catch (InvalidOperationException ex)
                {
                    Respond(id, new { id, ok = true, accepted = false, reply = ex.Message, state = Snapshot() });
                }
            }
        }
        catch (Exception ex)
        {
            // The request is consumed before execution: never replay a failed action.
            if (File.Exists(path)) File.Delete(path);
            Respond(id, new { id, ok = false, error = ex.Message });
        }
    }

    private bool OwnsRequest(Request request) => ActionsEnabled && request.agentMode == ServerAgentMode && activeGoal != null &&
        Matches(activeGoal) && request.expectedStopRevision == Demo.StopRevision && request.worldSession == WorldId &&
        request.actorGeneration == Demo.ActorGeneration && request.controllerToken == Probe.ControllerToken &&
        request.goalId == activeGoal.id && request.hostSession == activeGoal.hostSession &&
        TryHost(out string hostSession) && hostSession == activeGoal.hostSession;

    private string Execute(string command)
    {
        if (command == "/tpa stop") { Demo.Stop(); return "Input stopped."; }
        if (command == "/tpa retreat")
        {
            if (!IsSlimeGoal || goalHit == null || retreatStarted || Demo.Actor is not { } actor ||
                !NativeCombat.IsAuthoritativeActor(actor))
                throw new InvalidOperationException("退避仅在命中指定蓝史莱姆后可执行一次。");
            if (actor.itemAnimation > 0) throw new InvalidOperationException("请等待当前攻击动画结束后再退避。");
            int toward = ResolveTarget() is { } slime && Math.Abs(slime.Center.X - actor.Center.X) > 4f ?
                Math.Sign(slime.Center.X - actor.Center.X) : goalAttackDirection;
            float retreatX = actor.position.X - 48f * toward;
            if (Math.Abs(retreatX - combatOriginX) > 288f)
                throw new InvalidOperationException("退避终点已超出本次战斗范围。");
            if (!Demo.MoveTo(retreatX, 90, out string message)) throw new InvalidOperationException(message);
            retreatStarted = true;
            Probe.NotifyController("[TPA] 命中后正在退避。");
            return message;
        }
        if (targetLost || goalHit != null) throw new InvalidOperationException("该战斗目标已结束出招。");
        if (command == "/tpa approach")
        {
            if (ResolveTarget() is not { } npc || Demo.Actor is not { } player ||
                !NativeCombat.IsAuthoritativeActor(player) ||
                Math.Abs(IsSlimeGoal ? npc.Bottom.Y - player.Bottom.Y : npc.Center.Y - player.Center.Y) > 24f)
                throw new InvalidOperationException("靠近需要同层的指定目标。");
            float offset = npc.Center.X - player.Center.X;
            float step = offset - Math.Clamp(offset, -20f, 20f);
            if (IsSlimeGoal)
                step = Math.Clamp(step, -96f, 96f);
            float targetX = player.position.X + step;
            if (IsSlimeGoal && Math.Abs(targetX - combatOriginX) > 240f)
                throw new InvalidOperationException("蓝史莱姆已超出本次战斗范围。");
            if (!Demo.MoveTo(targetX, IsSlimeGoal ? 90 : 180, out string message)) throw new InvalidOperationException(message);
            Probe.NotifyController(IsSlimeGoal ? "[TPA] 正在靠近蓝史莱姆。" : "[TPA] 正在靠近傀儡。");
            return message;
        }
        string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts is ["/tpa", "select", var slotText])
        {
            if (activeGoal?.attackWeapon is not int weapon || Demo.Actor is not { itemAnimation: 0 } player ||
                !NativeCombat.IsAuthoritativeActor(player))
                throw new InvalidOperationException("换装需要有效的战斗目标，且须等待攻击动画结束。");
            if (!int.TryParse(slotText, NumberStyles.None, CultureInfo.InvariantCulture, out int slot) ||
                slot is < 0 or >= 50 || player.inventory[slot].IsAir || player.inventory[slot].type != weapon)
                throw new InvalidOperationException($"该槽位没有本目标指定的{WeaponName(weapon)}。");
            if (!Demo.SelectItem(slot)) throw new InvalidOperationException("无法选中该槽位。");
            return $"已选中{WeaponName(weapon)}（槽位 {slot}）。";
        }
        if (parts.Length >= 2 && parts[0] == "/tpa" && parts[1] == "attack")
        {
            if (ResolveTarget() is not { } npc || goalAttackAttempts >= 3 ||
                Demo.Actor is not { itemAnimation: 0 } actor || actor.HeldItem.type != activeGoal?.attackWeapon ||
                !MultiplayerAttackPolicy.TryParseCommand(parts[1..], out string action, out int duration) ||
                !MultiplayerAttackPolicy.TryDirection(action, out int direction))
                throw new InvalidOperationException("攻击需要指定目标、已就绪的指定武器和剩余次数。");
            float dx = npc.Center.X - actor.Center.X;
            if (IsSlimeGoal && (Math.Abs(actor.position.X - combatOriginX) > 240f ||
                Math.Abs(npc.Bottom.Y - actor.Bottom.Y) > 24f || Math.Abs(dx) > 44f || dx * direction < -4f))
                throw new InvalidOperationException("请根据蓝史莱姆的当前位置调整距离或攻击方向。");
            string? invalid = MultiplayerAttackPolicy.Validate(Probe.ControllerValid, true, !actor.dead,
                NativeCombat.IsAuthoritativeActor(actor), actor.HeldItem.type, direction, duration);
            if (invalid != null) throw new InvalidOperationException(invalid);
            Demo.Attack(direction, duration * 60);
            goalAttackAttempts++;
            goalAttackSequence = Demo.AttackSequence;
            goalAttackDirection = direction;
            Demo.RecordAttackStart(direction, duration * 60);
            return $"开始使用{WeaponName(actor.HeldItem.type)}攻击。";
        }
        if (IsSlimeGoal) throw new InvalidOperationException("蓝史莱姆目标使用 approach、attack 和 retreat 控制战斗范围。");
        double seconds = 2;
        if (parts.Length is < 2 or > 3 || parts[0] != "/tpa" || parts[1] is not ("left" or "right") ||
            (parts.Length == 3 && !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)) ||
            !double.IsFinite(seconds) || seconds <= 0 || seconds > 3)
            throw new InvalidOperationException("Only /tpa left/right [0-3 seconds] and /tpa stop are supported.");
        Demo.Move(parts[1] == "left" ? -1 : 1, (int)Math.Ceiling(seconds * 60));
        Probe.NotifyController($"[TPA] 正在向{(parts[1] == "left" ? "左" : "右")}移动，最多 {seconds:F1} 秒。");
        return $"Moving {parts[1]} for {seconds:F2}s.";
    }

    private object? ObserveTarget()
    {
        if (ResolveTarget() is not { } npc || Demo.Actor is not { } actor) return null;
        Vector2 offset = npc.Center - actor.Center;
        return new { slot = npc.whoAmI, x = npc.Center.X, y = npc.Center.Y,
            dx = offset.X, dy = offset.Y, groundDy = npc.Bottom.Y - actor.Bottom.Y,
            velocityX = npc.velocity.X, velocityY = npc.velocity.Y, life = npc.life, distancePixels = offset.Length() };
    }

    private object Snapshot() => new
    {
        agentMode = ServerAgentMode,
        netMode = Main.netMode,
        worldSession = ModContent.GetInstance<WorldSession>().Id,
        tick = Main.GameUpdateCount,
        stopRevision = ModContent.GetInstance<DemoPlayerSystem>().StopRevision,
        gameMenu = Main.gameMenu,
        paused = Main.gamePaused,
        focused = (bool?)null,
        player = (object?)null,
        controllerSlot = Probe.ControllerSlot,
        controllerToken = Probe.ControllerToken,
        goalId = activeGoal?.id,
        hostSession = activeGoal?.hostSession,
        attackTargetSlot = activeGoal?.attackTargetSlot,
        attackTargetKind = goalTarget == null ? null : IsSlimeGoal ? "slime" : "dummy",
        attackWeapon = activeGoal?.attackWeapon,
        attackHit = goalHit,
        attackTarget = ObserveTarget(),
        companionStatus = CompanionStatus,
        actor = ModContent.GetInstance<DemoPlayerSystem>().Snapshot()
    };

    private void Respond(string id, object response)
    {
        if (!Guid.TryParse(id, out _)) id = "invalid-request";
        string temporary = Path.Combine(directory, $"response-{id}.tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(response, Json));
        File.Move(temporary, Path.Combine(directory, $"response-{id}.json"), true);
    }

    private sealed record Request(string id, string command, long? expectedStopRevision = null,
        string? agentMode = null, string? worldSession = null, int? actorGeneration = null,
        string? controllerToken = null, string? goalId = null, string? hostSession = null);
    private sealed record Goal(string id, string text, string agentMode, long stopRevision, string worldSession,
        int actorGeneration, string controllerToken, string hostSession, int? attackTargetSlot, int? attackWeapon);
    private sealed record CombatTarget(int Slot, NPC Npc, int Type, object? SpawnIdentity, float TileX, float TileY);
}
