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
        Poll();
    }

    private void Poll()
    {
        if (directory.Length == 0) return;
        if (++pollTicks % 3 != 0) return;
        string path = Path.Combine(directory, "request.json");
        if (!File.Exists(path)) return;

        string id = "";
        try
        {
            var request = JsonSerializer.Deserialize<Request>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Empty request.");
            File.Delete(path);
            id = request.id;
            if (!Guid.TryParse(id, out _) || request.command.Length > 200)
                throw new InvalidDataException("Invalid request id or command.");
            string reply = Execute(request.command.Trim());
            Respond(id, new { id, ok = true, reply, state = Snapshot() });
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
        string[] tokens = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string[] allowed = { "spawn", "left", "right", "jump", "stop", "status", "remove" };
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
        tick = Main.GameUpdateCount,
        input = ModContent.GetInstance<EnterDiagnostics>().Snapshot(),
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

    private sealed record Request(string id, string command);

    private sealed class TestCaller(Player player) : CommandCaller
    {
        public CommandType CommandType => CommandType.Chat;
        public Player Player => player;
        internal List<string> Messages { get; } = new();
        public void Reply(string text, Color color = default) => Messages.Add(text);
    }
}
