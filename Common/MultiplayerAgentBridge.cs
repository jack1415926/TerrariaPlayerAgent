using System;
using System.IO;
using System.Globalization;
using System.Text.Json;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerrariaPlayerAgent.Common;

// The default bridge is read-only; goal-bound movement requires a separate opt-in.
// Clients must never consume it; only the authoritative server answers.
public sealed class MultiplayerAgentBridge : ModSystem
{
    public const string ServerAgentMode = "multiplayer_server";
    private int pollTicks;
    private string directory = "";
    private Goal? activeGoal;
    private string? latestGoalId;
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
        if (activeGoal != null && (!Matches(activeGoal) || !TryHost(out string session) || session != activeGoal.hostSession))
        {
            CancelGoal();
            Demo.RequestStop();
            Probe.NotifyController("Goal cancelled: the AI, controller or world changed.");
        }
        ProcessRequest();
        ReportGoals();
    }

    private string WorldId => ModContent.GetInstance<WorldSession>().Id;
    internal string MailboxDirectory => directory;
    internal string? CurrentGoalId => activeGoal?.id;
    internal void StopGoal() { CancelGoal(); Demo.RequestStop(); }

    internal bool QueueGoal(int sender, string text, out string message)
    {
        message = "Goal entry requires play.ps1 -MultiplayerAgent.";
        if (!ActionsEnabled || directory.Length == 0 || Main.netMode != NetmodeID.Server) return false;
        message = "Only the current AI controller can submit a goal.";
        if (!Probe.ControllerValid || Probe.ControllerSlot != sender) return false;
        message = "Prepare a living AI and a goal of 1-500 characters.";
        text = text.Trim();
        if (text.Length is < 1 or > 500 || Demo.Actor == null || Demo.Actor.dead) return false;
        message = "Start run-agent.ps1 -HostMode -AgentMode multiplayer_server first.";
        if (!TryHost(out string hostSession)) return false;
        CancelGoal();
        Demo.RequestStop();
        var goal = new Goal(Guid.NewGuid().ToString("N"), text, ServerAgentMode, Demo.StopRevision,
            WorldId, Demo.ActorGeneration, Probe.ControllerToken!, hostSession);
        try
        {
            string temporary = Path.Combine(directory, "goal.tmp");
            File.WriteAllText(temporary, JsonSerializer.Serialize(goal, Json));
            File.Move(temporary, Path.Combine(directory, "goal.json"), true);
            activeGoal = goal;
            latestGoalId = goal.id;
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

    internal void CancelGoal()
    {
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
        activeGoal = null;
        latestGoalId = null;
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
                status == "finished" ? $"模型已结束，请以实际行动结果为准：{detail}" : $"目标结束：{status}。{detail}";
            Probe.NotifyController($"[TPA {id[..Math.Min(id.Length, 6)]}] {message}");
            Mod.Logger.Info($"[TPA-MP] goal={id}, ack={ack}, status={status}");
            if (!ack || status is "rejected" or "stale") { activeGoal = null; latestGoalId = null; }
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
            else if (command == "test-goals" && ActionsEnabled && activeGoal == null &&
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
                string reply = Execute(command);
                Respond(id, new { id, ok = true, accepted = true, reply, state = Snapshot() });
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
        string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        double seconds = 2;
        if (parts.Length is < 2 or > 3 || parts[0] != "/tpa" || parts[1] is not ("left" or "right") ||
            (parts.Length == 3 && !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)) ||
            !double.IsFinite(seconds) || seconds <= 0 || seconds > 3)
            throw new InvalidOperationException("Only /tpa left/right [0-3 seconds] and /tpa stop are supported.");
        Demo.Move(parts[1] == "left" ? -1 : 1, (int)Math.Ceiling(seconds * 60));
        Probe.NotifyController($"[TPA] 正在向{(parts[1] == "left" ? "左" : "右")}移动，最多 {seconds:F1} 秒。");
        return $"Moving {parts[1]} for {seconds:F2}s.";
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
        int actorGeneration, string controllerToken, string hostSession);
}
