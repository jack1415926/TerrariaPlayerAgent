using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerrariaPlayerAgent.Common;

// Development acceptance only: exercise real QueueGoal/StopGoal with deterministic server-side timing.
public sealed class MultiplayerGoalAcceptance : ModSystem
{
    private string? testId, first, second, controllerToken, worldSession;
    private int generation;
    private bool stopped;
    private ulong stoppedTick;
    private DateTime deadline;
    private string? firstResult, secondResult;
    private readonly List<object> events = new();
    private MultiplayerAgentBridge Bridge => ModContent.GetInstance<MultiplayerAgentBridge>();
    private MultiplayerProbe Probe => ModContent.GetInstance<MultiplayerProbe>();
    private DemoPlayerSystem Demo => ModContent.GetInstance<DemoPlayerSystem>();

    internal string Start()
    {
        if (testId != null) throw new InvalidOperationException("A goal acceptance test is already running.");
        if (Bridge.CurrentGoalId != null || !Bridge.QueueGoal(Probe.ControllerSlot,
                "向右走三秒，然后停止。", out string message))
            throw new InvalidOperationException("Cannot start acceptance: prepare a living AI, controller and multiplayer host.");
        testId = Guid.NewGuid().ToString("N");
        first = Bridge.CurrentGoalId;
        second = null;
        controllerToken = Probe.ControllerToken;
        worldSession = ModContent.GetInstance<WorldSession>().Id;
        generation = Demo.ActorGeneration;
        firstResult = secondResult = null;
        stopped = false;
        events.Clear();
        deadline = DateTime.UtcNow.AddSeconds(60);
        Mark("first_submitted");
        return testId;
    }

    private void Mark(string name) => events.Add(new { name, tick = Main.GameUpdateCount, revision = Demo.StopRevision });

    internal void Observe(string id, bool ack, string status)
    {
        if (testId == null || (id != first && id != second)) return;
        if (!ack)
        {
            if (id == first) firstResult = status; else secondResult = status;
            Mark(id == first ? "first_result_" + status : "second_result_" + status);
            return;
        }
        if (status != "accepted") { Finish(false, "Goal was not accepted: " + status); return; }
        if (id == first)
        {
            if (Bridge.CurrentGoalId != first || second != null) { Finish(false, "First goal no longer owned by test."); return; }
            Mark("first_accepted");
            if (!Bridge.QueueGoal(Probe.ControllerSlot, "改为向左走三秒，然后停止。", out string message))
            { Finish(false, message); return; }
            second = Bridge.CurrentGoalId;
            Mark("replacement_submitted");
        }
        else Mark("replacement_accepted");
    }

    public override void PostUpdateEverything()
    {
        if (testId == null || Main.netMode != NetmodeID.Server) return;
        if (DateTime.UtcNow > deadline) { Finish(false, "Timed out waiting for goal transitions."); return; }
        if (!Probe.ControllerValid || Probe.ControllerToken != controllerToken || Demo.ActorGeneration != generation ||
            ModContent.GetInstance<WorldSession>().Id != worldSession || Demo.Actor is not { active: true, dead: false })
        { Finish(false, "Controller, AI or world changed during acceptance."); return; }
        var actor = Demo.Actor;
        if (!stopped)
        {
            string? expected = second ?? first;
            if (Bridge.CurrentGoalId != expected) { Finish(false, "Goal completed or was externally cancelled before test transition."); return; }
            if (second != null && (actor.controlLeft || actor.controlRight))
            {
                if (!actor.controlLeft || actor.controlRight) { Finish(false, "Old rightward input resumed after replacement."); return; }
                Mark("replacement_moving_left");
                Bridge.StopGoal();
                stopped = true;
                stoppedTick = Main.GameUpdateCount;
                Mark("stop_issued");
            }
        }
        else
        {
            if (actor.controlLeft || actor.controlRight || actor.controlJump || actor.controlUseItem)
            { Finish(false, "Input resumed after stop."); return; }
            if (Main.GameUpdateCount - stoppedTick >= 60 && firstResult != null && secondResult != null)
                Finish(firstResult == "interrupted" && secondResult == "interrupted", "Both goals interrupted; input stayed clear for 60 ticks.");
        }
    }

    public override void OnWorldUnload()
    {
        if (testId != null) Finish(false, "World unloaded during acceptance.");
    }

    private void Finish(bool passed, string reason)
    {
        if (testId == null) return;
        if (Bridge.CurrentGoalId != null && (Bridge.CurrentGoalId == first || Bridge.CurrentGoalId == second)) Bridge.StopGoal();
        string id = testId;
        testId = null;
        string path = Path.Combine(Bridge.MailboxDirectory, $"acceptance-{id}.json");
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new {
            id, outcome = passed ? "passed" : "failed", reason, worldSession, generation,
            first, second, firstResult, secondResult, events
        }, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
        Probe.NotifyController($"[TPA] 目标替换／停止自动验收：{(passed ? "通过" : "未通过")}。");
        Mod.Logger.Info($"[TPA-MP] acceptance={id}, passed={passed}, reason={reason}");
    }
}
