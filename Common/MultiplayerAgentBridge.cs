using System;
using System.IO;
using System.Text.Json;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerrariaPlayerAgent.Common;

// Batch 1: server-side, read-only status mailbox for the external Agent host.
// Clients must never consume it; only the authoritative server answers.
public sealed class MultiplayerAgentBridge : ModSystem
{
    public const string ServerAgentMode = "multiplayer_server";
    private int pollTicks;
    private string directory = "";
    internal bool Enabled => Program.LaunchParameters.ContainsKey("-tpatest");
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
        ProcessRequest();
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
            if (command != "status")
                throw new InvalidOperationException("Batch 1 is read-only; only status is allowed.");
            Respond(id, new { id, ok = true, accepted = true, reply = "Snapshot", state = Snapshot() });
        }
        catch (Exception ex)
        {
            // The request is consumed before execution: never replay a failed action.
            if (File.Exists(path)) File.Delete(path);
            Respond(id, new { id, ok = false, error = ex.Message });
        }
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
        actor = ModContent.GetInstance<DemoPlayerSystem>().Snapshot()
    };

    private void Respond(string id, object response)
    {
        if (!Guid.TryParse(id, out _)) id = "invalid-request";
        string temporary = Path.Combine(directory, $"response-{id}.tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(response, Json));
        File.Move(temporary, Path.Combine(directory, $"response-{id}.json"), true);
    }

    private sealed record Request(string id, string command, long? expectedStopRevision = null);
}
