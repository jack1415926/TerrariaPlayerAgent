using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Chat;
using Terraria.Localization;
using Mono.Cecil.Cil;
using MonoMod.Cil;

namespace TerrariaPlayerAgent.Common;

// Disposable test-world probe. This is not a network-connected vanilla client.
public sealed class MultiplayerProbe : ModSystem
{
    private const byte RequestKind = 1, StateKind = 2, ReplyKind = 3, GoalKind = 4;
    private int controller = -1, mirrorSlot = -1;
    private Player? controllerPlayer;
    private object? controllerSocket;
    private string? controllerToken;
    internal bool ControllerValid => controller >= 0 && controller < Main.maxPlayers &&
        controllerSocket != null && Main.player[controller].active && ReferenceEquals(Main.player[controller], controllerPlayer) &&
        Netplay.Clients[controller].State != 0 && ReferenceEquals(Netplay.Clients[controller].Socket, controllerSocket);
    internal int ControllerSlot => ControllerValid ? controller : -1;
    internal string? ControllerToken => ControllerValid ? controllerToken : null;
    private Player? mirror, previous;
    private readonly DisplaySync display = new();
    private uint stateSequence;
    private Vector2 lastSentPosition;
    private bool hasSentPosition;
    private int combatDiagnosticTicks;
    private DemoPlayerSystem Demo => ModContent.GetInstance<DemoPlayerSystem>();
    private static double Now => Environment.TickCount64 / 1000.0;

    public override void Load()
    {
        IL_Netplay.UpdateConnectedClients += PreserveVirtualSlot;
        On_ChatHelper.SendChatMessageToClient += SendChat;
    }

    public override void Unload()
    {
        IL_Netplay.UpdateConnectedClients -= PreserveVirtualSlot;
        On_ChatHelper.SendChatMessageToClient -= SendChat;
    }

    private void PreserveVirtualSlot(ILContext il)
    {
        var cursor = new ILCursor(il);
        // Skip only the unconnected-slot cleanup. Never toggle active across threads.
        if (!cursor.TryGotoNext(MoveType.Before,
            i => i.MatchLdsfld<Main>(nameof(Main.player)),
            i => i.MatchLdloc(1),
            i => i.OpCode == OpCodes.Ldelem_Ref,
            i => i.MatchLdfld<Entity>(nameof(Entity.active)),
            i => i.MatchLdsfld<Main>(nameof(Main.player)),
            i => i.MatchLdloc(1),
            i => i.OpCode == OpCodes.Ldelem_Ref,
            i => i.MatchLdcI4(0),
            i => i.MatchStfld<Entity>(nameof(Entity.active))))
            throw new InvalidOperationException("TPA virtual slot cleanup pattern missing; recheck tML version.");
        var branch = il.Instrs[cursor.Index + 9];
        // MonoMod converts branch operands to ILLabel while invoking manipulators.
        if (branch.OpCode.FlowControl != FlowControl.Cond_Branch || branch.Operand is not ILLabel end)
            throw new InvalidOperationException("TPA virtual slot cleanup exit missing; recheck tML version.");
        cursor.Emit(OpCodes.Ldloc, il.Body.Variables[1]);
        cursor.EmitDelegate<Func<int, bool>>(slot => Main.netMode == NetmodeID.Server &&
            Demo.Actor is { } actor && actor.whoAmI == slot &&
            ReferenceEquals(Main.player[slot], actor) && Netplay.Clients[slot].State == 0);
        cursor.Emit(OpCodes.Brtrue, end);
    }

    private void SendChat(On_ChatHelper.orig_SendChatMessageToClient orig, NetworkText text, Color color, int playerId)
    {
        if (Main.netMode == NetmodeID.Server && Demo.Actor?.whoAmI == playerId &&
            Netplay.Clients[playerId].State == 0) return;
        orig(text, color, playerId);
    }

    internal void Request(string action, int value = 0)
    {
        var packet = Mod.GetPacket();
        packet.Write(RequestKind);
        packet.Write(action);
        packet.Write(value);
        packet.Send();
    }

    internal void RequestGoal(string text)
    {
        var packet = Mod.GetPacket(); packet.Write(GoalKind); packet.Write(text); packet.Send();
    }

    internal void NotifyController(string text)
    {
        if (ControllerValid) SendReply(controller, text);
    }

    private void SendReply(int recipient, string text)
    {
        var packet = Mod.GetPacket(); packet.Write(ReplyKind); packet.Write(text); packet.Send(recipient);
    }

    internal void Receive(BinaryReader reader, int sender)
    {
        byte kind = reader.ReadByte();
        if (Main.netMode == NetmodeID.Server && kind == GoalKind)
        {
            string text = reader.ReadString();
            if (sender < 0 || sender >= Main.maxPlayers || !Main.player[sender].active) return;
            bool accepted = ModContent.GetInstance<MultiplayerAgentBridge>().QueueGoal(sender, text, out string message);
            Mod.Logger.Info($"[TPA-MP] goal sender={sender}, accepted={accepted}, reply={message}");
            SendReply(sender, message);
            return;
        }
        if (Main.netMode == NetmodeID.Server && kind == RequestKind)
        {
            string action = reader.ReadString();
            int value = reader.ReadInt32();
            if (sender < 0 || sender >= Main.maxPlayers || !Main.player[sender].active) return;
            Mod.Logger.Info($"[TPA-MP] request sender={sender}, action={action}, value={value}");
            string reply;
            if (action == "status")
                reply = Demo.Status(Main.player[sender]);
            else if (controller >= 0 && (!ControllerValid || controller != sender))
                reply = "Only the player who spawned this probe can control it.";
            else if (action == "spawn")
            {
                reply = Demo.Spawn(Main.player[sender]);
                if (Demo.Actor != null && controller < 0)
                {
                    controller = sender; controllerPlayer = Main.player[sender];
                    controllerSocket = Netplay.Clients[sender].Socket;
                    controllerToken = Guid.NewGuid().ToString("N");
                }
            }
            else if (action == "remove")
            {
                ModContent.GetInstance<MultiplayerAgentBridge>().CancelGoal();
                Demo.Remove(); controller = -1; reply = "Probe removed.";
            }
            else if (Demo.Actor == null) reply = "Use /tpamp spawn first.";
            else if (action == "combat")
            {
                combatDiagnosticTicks = 1200;
                reply = "Combat diagnostics enabled for 20s; repeat your position-switching test, then query status.";
            }
            else if (action == "stop")
            {
                ModContent.GetInstance<MultiplayerAgentBridge>().StopGoal();
                reply = "Probe stopped; goal cancelled.";
            }
            else if (action == "team" && value is >= 0 and <= 5)
            {
                Demo.Actor.team = value; reply = $"Probe team={value}.";
            }
            else if ((action is "left" or "right") && value is >= 1 and <= 10)
            {
                ModContent.GetInstance<MultiplayerAgentBridge>().CancelGoal();
                Demo.RequestStop();
                Demo.Move(action == "left" ? -1 : 1, value * 60);
                reply = $"Probe moving {action} for {value}s.";
            }
            else reply = "Invalid probe command.";
            SendState();
            Mod.Logger.Info($"[TPA-MP] reply sender={sender}, result={reply}");
            SendReply(sender, reply);
        }
        else if (Main.netMode == NetmodeID.MultiplayerClient && kind == StateKind)
        {
            int slot = reader.ReadInt16();
            if (slot == -1) { ClearMirror(); return; }
            var position = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            var velocity = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            int direction = reader.ReadSByte(), team = reader.ReadByte(), life = reader.ReadInt32();
            uint sequence = reader.ReadUInt32();
            long serverTick = reader.ReadInt64();
            byte flags = reader.ReadByte();
            if (slot < 0 || slot >= Main.maxPlayers || slot == Main.myPlayer) return;
            if (mirrorSlot != slot) ClearMirror();
            if (mirror == null)
            {
                // Never replace an existing real player on this client.
                if (Main.player[slot].active) return;
                previous = Main.player[slot];
                mirror = new Player { whoAmI = slot, name = "Agent MP Probe", active = true };
                mirrorSlot = slot;
                Main.player[slot] = mirror;
            }
            if (!ReferenceEquals(Main.player[slot], mirror)) { ClearMirror(); return; }
            // Keep the authoritative sample; position is applied per tick in PostUpdatePlayers.
            display.Add(new SyncSnapshot
            {
                Sequence = sequence, ServerTick = serverTick,
                X = position.X, Y = position.Y, VelocityX = velocity.X, VelocityY = velocity.Y,
                ReceivedAt = Now, Teleported = (flags & 1) != 0, TeleportKnown = true
            });
            mirror.direction = direction; mirror.team = team;
            mirror.statLife = life; mirror.dead = life <= 0;
        }
        else if (Main.netMode == NetmodeID.MultiplayerClient && kind == ReplyKind)
            Main.NewText(reader.ReadString(), Color.LightBlue);
    }

    public override void PreUpdatePlayers()
    {
        if (Main.netMode != NetmodeID.Server || controller < 0) return;
        // Yield if a real connection claims the unreserved slot. Never kick a client.
        if (!ControllerValid || Demo.Actor != null &&
            Netplay.Clients[Demo.Actor.whoAmI].State != 0)
        {
            ModContent.GetInstance<MultiplayerAgentBridge>().CancelGoal();
            Demo.Remove(); controller = -1; SendState();
        }
    }

    public override void PostUpdatePlayers()
    {
        if (Main.netMode == NetmodeID.Server)
        {
            if (controller >= 0 && Main.GameUpdateCount % 6 == 0) SendState();
            return;
        }
        ApplyDisplay();
    }

    public override void PostUpdateNPCs()
    {
        if (Main.netMode != NetmodeID.Server || combatDiagnosticTicks <= 0) return;
        combatDiagnosticTicks--;
        if (Demo.Actor is not { } actor) { combatDiagnosticTicks = 0; return; }
        if (combatDiagnosticTicks % 30 != 0) return;
        string enemies = string.Join("; ", Main.npc.Where(n => n.active && !n.friendly && n.damage > 0 &&
                Vector2.DistanceSquared(n.Center, actor.Center) < 800 * 800).Take(8)
            .Select(n => $"npc={n.whoAmI}/type={n.type},target={n.target},distance={Vector2.Distance(n.Center, actor.Center):F0}," +
                $"touching={n.Hitbox.Intersects(actor.Hitbox)},noAggro={actor.npcTypeNoAggro[n.type]}"));
        Mod.Logger.Info($"[TPA-Combat] slot={actor.whoAmI},active={actor.active},dead={actor.dead},ghost={actor.ghost}," +
            $"life={actor.statLife},immune={actor.immune}/{actor.immuneTime},god={actor.creativeGodMode},aggro={actor.aggro}," +
            $"position={actor.position}; {enemies}");
    }

    // Called after native player updates, before drawing: the mirror is positioned for this frame.
    private void ApplyDisplay()
    {
        if (mirror == null || mirrorSlot < 0) return;
        if (!ReferenceEquals(Main.player[mirrorSlot], mirror)) { ClearMirror(); return; }
        if (!display.TrySample(Now, out DisplaySample sample)) return;
        float correction = Vector2.Distance(mirror.position, new Vector2(sample.X, sample.Y));
        if (correction > 0.01f) display.RecordCorrection(correction, Now);
        mirror.position.X = sample.X;
        mirror.position.Y = sample.Y;
        if (display.TryGetLatest(out SyncSnapshot latest))
        {
            // Keep the authoritative velocity; only the displayed position is smoothed.
            mirror.velocity.X = latest.VelocityX;
            mirror.velocity.Y = latest.VelocityY;
        }
    }

    internal void PrintDiagnostics(CommandCaller caller)
    {
        caller.Reply($"[MP] snapshots={display.ReceivedCount} dup={display.DuplicateCount} " +
            $"late={display.OutOfOrderCount} extrap={display.ExtrapolatedFrames} stale={display.StaleFrames} " +
            $"teleport={display.TeleportCount} correction_last={display.LastCorrection:F1} max={display.MaxCorrection:F1}");
        var recent = display.RecentCorrections;
        for (int i = Math.Max(0, recent.Count - 5); i < recent.Count; i++)
            caller.Reply($"[MP] seq={recent[i].Sequence} tick={recent[i].ServerTick} " +
                $"offset={recent[i].Offset:F1} t={recent[i].Time:F2}");
    }

    private void SendState()
    {
        var packet = Mod.GetPacket(); packet.Write(StateKind);
        var actor = Demo.Actor;
        packet.Write((short)(actor?.whoAmI ?? -1));
        if (actor != null)
        {
            // The server marks a jump on its fixed 6-tick cadence; normal movement cannot reach 96 px.
            bool teleported = hasSentPosition &&
                Vector2.Distance(lastSentPosition, actor.position) > DisplaySync.DefaultTeleportDistance;
            lastSentPosition = actor.position;
            hasSentPosition = true;
            packet.Write(actor.position.X); packet.Write(actor.position.Y);
            packet.Write(actor.velocity.X); packet.Write(actor.velocity.Y);
            packet.Write((sbyte)actor.direction); packet.Write((byte)actor.team); packet.Write(actor.statLife);
            packet.Write(stateSequence++);
            packet.Write((long)Main.GameUpdateCount);
            packet.Write((byte)(teleported ? 1 : 0));
        }
        else hasSentPosition = false;
        packet.Send();
    }

    private void ClearMirror()
    {
        display.Reset();
        if (mirror != null && mirrorSlot >= 0 && ReferenceEquals(Main.player[mirrorSlot], mirror))
        {
            mirror.active = false;
            if (previous != null) Main.player[mirrorSlot] = previous;
        }
        mirror = previous = null; mirrorSlot = -1;
    }

    public override void OnWorldUnload()
    {
        ClearMirror(); controller = -1; controllerPlayer = null; controllerSocket = null; controllerToken = null;
        hasSentPosition = false; combatDiagnosticTicks = 0;
    }
}
