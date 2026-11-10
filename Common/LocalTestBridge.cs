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
        if (pendingGoal != null || File.Exists(Path.Combine(directory, "goal.json")))
        {
            message = "The host has not taken the previous goal yet.";
            return false;
        }
        pendingGoal = text;
        message = "Goal queued for the host.";
        return true;
    }

    private void PublishGoal()
    {
        if (pendingGoal == null) return;
        string temporary = Path.Combine(directory, "goal.tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid().ToString("N"),
            text = pendingGoal,
            stopRevision = ModContent.GetInstance<DemoPlayerSystem>().StopRevision
        }));
        try
        {
            File.Move(temporary, Path.Combine(directory, "goal.json"), true);
            pendingGoal = null;
        }
        catch (IOException)
        {
            // Keep it queued; the previous goal file is still unread.
        }
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
            string text = GoalMessage(ack, Status("status"), ack ? Status("reason") : Status("summary"));
            File.Delete(file);
            Main.NewText(text, Color.LightGreen);
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
            "accepted" => "[TPA] Goal accepted; the host is working on it.",
            "duplicate" => "[TPA] This goal id was already handled; duplicate ignored.",
            "stale" => "[TPA] Leftover goal from a previous host run; not replayed.",
            "rejected" => $"[TPA] Goal rejected: {detail}",
            _ => $"[TPA] {status}: {detail}"
        }
        : status switch
        {
            "finished" => $"[TPA] Goal finished (model ended normally): {detail}",
            "budget_exhausted" => "[TPA] Turn budget used up; verify the result before trusting it.",
            "interrupted" => "[TPA] Goal interrupted.",
            "unknown" => "[TPA] Outcome unknown; observe before retrying.",
            "failed" => $"[TPA] Goal failed: {detail}",
            _ => $"[TPA] {status}: {detail}"
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
