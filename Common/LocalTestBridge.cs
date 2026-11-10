using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TerrariaPlayerAgent.Commands;

namespace TerrariaPlayerAgent.Common;

// Development-only, one request at a time. No sockets and no arbitrary commands.
public sealed class LocalTestBridge : ModSystem
{
    private int pollTicks;
    private string directory = "";
    private string? pendingGoal;
    private long pendingGoalStopRevision;
    private string pendingGoalWorldSession = "";
    private string? latestGoalId;
    internal bool Enabled => Program.LaunchParameters.ContainsKey("-tpatest");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public override void OnModLoad()
    {
        if (!Enabled || Main.dedServ) return;
        directory = Path.Combine(Main.SavePath, "TPA");
        Directory.CreateDirectory(directory);
    }

    public override void PostUpdateInput()
    {
        if (directory.Length == 0) return;
        ModContent.GetInstance<EnterDiagnostics>().Sample();
        ModContent.GetInstance<NativeChatInput>().Tick();
        Poll();
    }

    private void Poll()
    {
        if (directory.Length == 0) return;
        if (++pollTicks % 3 != 0) return;
        // Stop has its own mailbox so it cannot be blocked by an Agent status/action request.
        ProcessRequest("stop.json", true);
        ProcessRequest("request.json", false);
        PublishGoal();
        ReportGoalFiles();
    }

    internal bool QueueGoal(string text, out string message)
    {
        if (!Enabled || directory.Length == 0)
        {
            message = "Goal entry requires -tpatest (scripts/play.ps1).";
            return false;
        }
        if (Main.gameMenu)
        {
            message = "Enter a single-player world first.";
            return false;
        }
        if (!HostAlive())
        {
            message = "Start the Agent host first: scripts/run-agent.ps1 -HostMode.";
            return false;
        }
        // A new goal preempts the running one and replaces any goal still waiting to be sent.
        // The stamp is taken now: a stop that lands before the file is published must not look valid.
        ModContent.GetInstance<DemoPlayerSystem>().RequestStop();
        pendingGoal = text;
        pendingGoalStopRevision = ModContent.GetInstance<DemoPlayerSystem>().StopRevision;
        pendingGoalWorldSession = ModContent.GetInstance<WorldSession>().Id;
        message = "Goal queued for the host.";
        return true;
    }

    internal void CancelPendingGoal()
    {
        if (directory.Length == 0) return;
        pendingGoal = null;
        pendingGoalStopRevision = 0;
        pendingGoalWorldSession = "";
        TryDelete(Path.Combine(directory, "goal.tmp"));
    }

    private bool HostAlive()
    {
        try
        {
            var file = new FileInfo(Path.Combine(directory, "host-ready.json"));
            return file.Exists && (DateTime.UtcNow - file.LastWriteTimeUtc).TotalSeconds < 15;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private void PublishGoal()
    {
        if (pendingGoal == null) return;
        string id = Guid.NewGuid().ToString("N");
        string temporary = Path.Combine(directory, "goal.tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(new
        {
            id,
            text = pendingGoal,
            stopRevision = pendingGoalStopRevision,
            worldSession = pendingGoalWorldSession
        }));
        try
        {
            File.Move(temporary, Path.Combine(directory, "goal.json"), true);
            pendingGoal = null;
            latestGoalId = id;
        }
        catch (IOException)
        {
            // Keep it queued; the previous goal file is still unread.
            TryDelete(temporary);
        }
    }

    public override void OnWorldUnload()
    {
        CancelPendingGoal();
        latestGoalId = null;
    }

    private void ReportGoalFiles()
    {
        foreach (string file in Directory.GetFiles(directory, "goal-ack-*.json")) ReportGoalFile(file, true);
        foreach (string file in Directory.GetFiles(directory, "goal-result-*.json")) ReportGoalFile(file, false);
    }

    private void ReportGoalFile(string file, bool ack)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            JsonElement root = document.RootElement;
            string Status(string name) =>
                root.TryGetProperty(name, out JsonElement value) ? value.GetString() ?? "" : "";
            string id = Status("id");
            string tag = id.Length >= 6 ? id[..6] : id;
            string status = Status("status");
            if (!ack && latestGoalId != null && id != latestGoalId)
            {
                // A result from an earlier goal must not look like the current goal's summary.
                File.Delete(file);
                Main.NewText($"[TPA] Ignored a late result for goal {tag}: {status}.", Color.Orange);
                return;
            }
            string text = GoalMessage(ack, status, ack ? Status("reason") : Status("summary"));
            File.Delete(file);
            Main.NewText($"[TPA {tag}] {text}", Color.LightGreen);
        }
        catch (IOException)
        {
            // Re-read on the next poll.
        }
        catch (JsonException)
        {
            TryDelete(file);
        }
    }

    private static string GoalMessage(bool ack, string status, string detail) => ack
        ? status switch
        {
            "accepted" => "Goal accepted; the host is working on it.",
            "duplicate" => "This goal id was already handled; duplicate ignored.",
            "stale" => "This goal is no longer valid; it will not run.",
            "rejected" => $"Goal rejected: {detail}",
            _ => $"{status}: {detail}"
        }
        : status switch
        {
            "finished" => $"Goal finished (model ended normally): {detail}",
            "budget_exhausted" => "Turn budget used up; verify the result before trusting it.",
            "interrupted" => "Goal interrupted.",
            "unknown" => "Outcome unknown; observe before retrying.",
            "failed" => $"Goal failed: {detail}",
            _ => $"{status}: {detail}"
        };

    private static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (IOException) { }
    }

    private void ProcessRequest(string filename, bool stopOnly)
    {
        string path = Path.Combine(directory, filename);
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
            if (stopOnly && request.command.Trim() != "/tpa stop")
                throw new InvalidDataException("The stop mailbox only accepts /tpa stop.");
            if (request.expectedStopRevision is long expected &&
                expected != ModContent.GetInstance<DemoPlayerSystem>().StopRevision)
            {
                Respond(id, new { id, ok = true, accepted = false, interrupted = true,
                    reply = "Task cancelled by a newer stop/remove. No command executed.", state = Snapshot() });
                return;
            }
            string reply = Execute(request.command.Trim());
            bool accepted = !request.command.Trim().StartsWith("/tpa ", StringComparison.Ordinal) ||
                ModContent.GetInstance<DemoCommand>().LastAccepted;
            Respond(id, new { id, ok = true, accepted, reply, state = Snapshot() });
        }
        catch (Exception ex)
        {
            // The request is consumed before execution: do not replay a failed action.
            if (File.Exists(path)) File.Delete(path);
            Respond(id, new { id, ok = false, error = ex.Message });
        }
    }

    private string Execute(string command)
    {
        var diagnostics = ModContent.GetInstance<EnterDiagnostics>();
        if (command == "status") return "Snapshot";
        if (command == "trace-start") { diagnostics.Start(); return "Enter trace started."; }
        if (command == "trace-stop") { diagnostics.Stop(); return "Enter trace stopped."; }
        if (command == "chat-close") { Main.ClosePlayerChat(); return "Chat closed."; }
        if (Main.gameMenu || Main.netMode != NetmodeID.SinglePlayer)
            throw new InvalidOperationException("Enter a single-player test world first.");
        if (command == "chat-open")
        {
            Main.OpenPlayerChat();
            return "Chat opened through the game API, not through a keyboard event.";
        }
        if (command == "enter")
        {
            diagnostics.PressEnter();
            return "Queued a game-local SDL Enter press for six input updates; not an OS keyboard event.";
        }
        if (command == "chat-type-status")
        {
            ModContent.GetInstance<NativeChatInput>().BeginStatus();
            return "Typing the fixed status command through this game's WM_CHAR handler.";
        }
        if (command == "chat-submit-status")
        {
            ModContent.GetInstance<NativeChatInput>().SubmitStatus();
            return "Queued the fixed status command for native chat submission.";
        }
        string[] tokens = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 3 && tokens[0] == "tile" && int.TryParse(tokens[1], out int tileX) &&
            int.TryParse(tokens[2], out int tileY))
            return JsonSerializer.Serialize(NativeMining.Inspect(tileX, tileY));
        string[] allowed = { "spawn", "left", "right", "jump", "down", "stop", "status", "remove", "equip", "enemy", "clear-enemies", "attack", "select", "mine", "goto", "home", "range", "goal" };
        if (tokens.Length < 2 || tokens[0] != "/tpa" || !allowed.Contains(tokens[1]))
            throw new InvalidOperationException("Only /tpa demo commands and documented diagnostics are allowed.");
        var caller = new TestCaller(Main.LocalPlayer);
        ModContent.GetInstance<DemoCommand>().Action(caller, command, tokens.Skip(1).ToArray());
        return string.Join("\n", caller.Messages);
    }

    private object Snapshot() => new
    {
        gameMenu = Main.gameMenu,
        netMode = Main.netMode,
        paused = Main.gamePaused,
        focused = Main.hasFocus,
        worldSession = ModContent.GetInstance<WorldSession>().Id,
        tick = Main.GameUpdateCount,
        stopRevision = ModContent.GetInstance<DemoPlayerSystem>().StopRevision,
        input = ModContent.GetInstance<EnterDiagnostics>().Snapshot(),
        chatProbe = new
        {
            typing = ModContent.GetInstance<NativeChatInput>().Typing,
            executions = ModContent.GetInstance<DemoCommand>().ExecutionCount,
            lastAction = ModContent.GetInstance<DemoCommand>().LastAction,
            lastCaller = ModContent.GetInstance<DemoCommand>().LastCaller
        },
        player = Main.gameMenu ? null : new
        {
            slot = Main.LocalPlayer.whoAmI,
            x = Main.LocalPlayer.position.X,
            y = Main.LocalPlayer.position.Y,
            life = Main.LocalPlayer.statLife,
            selectedItem = Main.LocalPlayer.selectedItem
        },
        actor = ModContent.GetInstance<DemoPlayerSystem>().Snapshot()
    };

    private void Respond(string id, object response)
    {
        // Unique responses avoid replacing a file while PowerShell has it open on Windows.
        if (!Guid.TryParse(id, out _)) id = "invalid-request";
        string temporary = Path.Combine(directory, $"response-{id}.tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(response, Json));
        File.Move(temporary, Path.Combine(directory, $"response-{id}.json"), true);
    }

    private sealed record Request(string id, string command, long? expectedStopRevision = null);

    private sealed class TestCaller(Player player) : CommandCaller
    {
        public CommandType CommandType => CommandType.Chat;
        public Player Player => player;
        internal List<string> Messages { get; } = new();
        public void Reply(string text, Color color = default) => Messages.Add(text);
    }
}
