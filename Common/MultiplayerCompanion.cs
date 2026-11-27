using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;

namespace TerrariaPlayerAgent.Common;

// Server-side movement and short defensive reactions while following the controller.
internal sealed class MultiplayerCompanion
{
    internal enum Command { Follow, Wait, Return }

    private const float FollowDistance = 48f;
    private const float MaxDistance = 480f;
    private const float MaxHeightDifference = 112f;
    private Command command;
    private string? controllerToken;
    private long stopRevision;
    private int generation;
    private float? waypoint;
    private ulong nextDefenseTick;

    internal bool Active => controllerToken != null;
    internal string Status => !Active ? "待命" : command == Command.Return ? "正在返回" : "跟随中";

    internal static Command? Parse(string text) => text.TrimEnd('。', '！', '!').TrimEnd() switch
    {
        "跟随" or "跟随我" or "跟我走" or "继续跟随" => Command.Follow,
        "等候" or "等一下" or "原地等候" or "原地等我" => Command.Wait,
        "回来" or "回来我身边" or "回到我身边" => Command.Return,
        _ => null
    };

    internal string Start(Command requested, DemoPlayerSystem demo, string token)
    {
        command = requested;
        if (requested == Command.Wait) return "[TPA] 已停止行动，原地等候。";
        controllerToken = token;
        generation = demo.ActorGeneration;
        stopRevision = demo.StopRevision;
        return requested == Command.Return ? "[TPA] 正在回到你身边。" : "[TPA] 开始跟随你。";
    }

    internal void Clear()
    {
        controllerToken = null;
        waypoint = null;
        nextDefenseTick = 0;
    }

    internal void Update(DemoPlayerSystem demo, MultiplayerProbe probe)
    {
        if (!Active) return;
        if (controllerToken != probe.ControllerToken || generation != demo.ActorGeneration ||
            stopRevision != demo.StopRevision || demo.Actor is not { dead: false } actor ||
            !NativeCombat.IsAuthoritativeActor(actor))
        {
            Clear();
            return;
        }

        Player owner = Main.player[probe.ControllerSlot];
        Vector2 offset = owner.Bottom - actor.Bottom;
        bool returning = command == Command.Return;
        if (owner.dead || offset.LengthSquared() > MaxDistance * MaxDistance ||
            Math.Abs(offset.Y) > MaxHeightDifference)
        {
            Finish(demo, probe, "你已离开可跟随范围，AI 已停下等候。");
            return;
        }

        if (!returning && actor.itemAnimation == 0 && Main.GameUpdateCount >= nextDefenseTick &&
            offset.LengthSquared() <= 144f * 144f &&
            DefenseSlot(actor) is int slot && FindThreat(actor, actor.inventory[slot].type) is { } threat &&
            (slot == actor.selectedItem || demo.SelectItem(slot)))
        {
            // One native use press. Re-evaluate the threat before each subsequent press.
            int attackDirection = threat.Center.X < actor.Center.X ? -1 : 1;
            demo.Attack(attackDirection, 1);
            demo.RecordAttackStart(attackDirection, 1);
            waypoint = null;
            nextDefenseTick = Main.GameUpdateCount + 60;
            return;
        }
        if (!returning && actor.itemAnimation > 0) return;

        float distanceX = Math.Abs(offset.X);
        bool near = distanceX <= FollowDistance;
        bool clear = near && Collision.CanHitLine(actor.position, actor.width, actor.height,
            owner.position, owner.width, owner.height);
        if (clear || distanceX <= GroundNavigation.Tolerance)
        {
            if (waypoint.HasValue) demo.Stop();
            waypoint = null;
            if (returning && clear && Math.Abs(offset.Y) <= 24f)
                Finish(demo, probe, "已回到你身边，原地等候。");
            else if (distanceX <= GroundNavigation.Tolerance && (!clear || Math.Abs(offset.Y) > 24f) &&
                LocalTerrain.Grounded(owner) && LocalTerrain.Grounded(actor))
                Finish(demo, probe, "当前位置无法走到你身边，AI 已停下等候。");
            return;
        }

        int direction = Math.Sign(offset.X);
        float step = Math.Min(distanceX - (near ? 0f : 32f), 96f);
        float targetX = actor.position.X + direction * step;
        if (waypoint is float currentTarget && demo.NavigationActive)
        {
            // Keep a useful step until it ends. Replace it if the player has turned back
            // or the old endpoint would carry the AI past the new stopping point.
            float oldStep = (currentTarget - actor.position.X) * direction;
            if (oldStep >= -GroundNavigation.Tolerance && oldStep <= step + 16f) return;
        }
        if (waypoint.HasValue && demo.NavigationFailed)
        {
            Finish(demo, probe, "前方地形受阻，AI 已停下等候。");
            return;
        }
        if (demo.MoveTo(targetX, 90, out _)) waypoint = targetX;
        else Finish(demo, probe, "找不到可走的位置，AI 已停下等候。");
    }

    private static int? DefenseSlot(Player actor)
    {
        if (MultiplayerAttackPolicy.IsSupportedAttackItem(actor.HeldItem.type)) return actor.selectedItem;
        int slot = Array.FindIndex(actor.inventory, 0, 50,
            item => !item.IsAir && MultiplayerAttackPolicy.IsSupportedAttackItem(item.type));
        return slot < 0 ? null : slot;
    }

    private static NPC? FindThreat(Player actor, int weapon)
    {
        float reachX = weapon == StartingKit.CopperShortsword ? 34f : 42f;
        return Main.npc.Where(npc => npc.active && !npc.friendly && !npc.dontTakeDamage &&
                npc.life > 0 && npc.damage > 0 && Math.Abs(npc.Center.X - actor.Center.X) <= reachX &&
                Math.Abs(npc.Bottom.Y - actor.Bottom.Y) <= 28f &&
                (npc.Hitbox.Intersects(actor.Hitbox) || Vector2.Dot(actor.Center - npc.Center, npc.velocity) > 0f) &&
                Collision.CanHitLine(actor.position, actor.width, actor.height, npc.position, npc.width, npc.height))
            .OrderBy(npc => Vector2.DistanceSquared(npc.Center, actor.Center)).FirstOrDefault();
    }

    private void Finish(DemoPlayerSystem demo, MultiplayerProbe probe, string message)
    {
        Clear();
        demo.Stop();
        probe.NotifyController("[TPA] " + message);
    }
}
