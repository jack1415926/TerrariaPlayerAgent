using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerrariaPlayerAgent.Common;

internal sealed class WoodCollection(string id, Player actor, int requested)
{
    private enum Phase { Search, Approach, Chop, Pickup }
    private const int SearchRadius = 24; // tiles from the current actor
    private const int TaskRadius = SearchRadius * 2;
    private readonly float originX = actor.Center.X;
    private readonly ulong deadline = Main.GameUpdateCount + 7200;
    private readonly HashSet<Point> visited = new();
    private readonly Dictionary<Item, (int Stack, int Own)> drops = new(ReferenceEqualityComparer.Instance);
    private readonly List<object> events = new();
    private readonly Item sample = new(ItemID.Wood);
    private readonly int startingWood = CountWood(actor);
    private Phase phase;
    private Point tree;
    private ulong pickupDeadline;
    private string status = "running", reason = "", lastFailure = "";
    private int acquired;

    internal Player Actor => actor;
    internal bool Complete => acquired >= requested;
    internal string Progress => $"{acquired}/{requested} 木材";
    internal static int AxeSlot(Player player) => Array.FindIndex(player.inventory, 0, 50, NativeMining.SupportedAxe);
    internal static int CountWood(Player player) => player.inventory.Take(50)
        .Where(item => !item.IsAir && item.type == ItemID.Wood).Sum(item => item.stack);

    internal string? Advance(DemoPlayerSystem demo, NativeMining mining)
    {
        if (Main.GameUpdateCount >= deadline) return "任务超时";
        if (actor.noBuilding) return "当前位置禁止使用采集工具";
        if (Complete) return null;
        if (!actor.ItemSpace(sample).CanTakeItemToPersonalInventory) return "背包没有木材容量";
        if (phase == Phase.Pickup)
        {
            GatherDrops(demo);
            return null;
        }
        if (phase == Phase.Chop)
        {
            if (mining.Ticks > 0) return null;
            Record(new { kind = "chop_result", tick = Main.GameUpdateCount, x = tree.X, y = tree.Y,
                result = mining.Result, reason = mining.Reason, drops = drops.Count });
            if (drops.Count != 0)
            {
                phase = Phase.Pickup;
                pickupDeadline = Main.GameUpdateCount + 240;
            }
            else NextTree(demo, mining.Result == "broken" ? "砍伐后未确认木材掉落" : "砍伐受阻或未确认");
            return null;
        }
        if (actor.itemAnimation > 0) return null;
        if (!NativeMining.SupportedAxe(actor.HeldItem))
        {
            int slot = AxeSlot(actor);
            if (slot < 0 || !demo.SelectItem(slot)) return "背包没有可用斧头";
        }
        if (phase == Phase.Search)
        {
            if (visited.Count >= 16) return Reason("已达到树木尝试上限");
            if (FindTree(actor) is not { } candidate) return Reason("附近没有可尝试的森林树木");
            tree = candidate;
            visited.Add(tree);
            Record(new { kind = "tree_selected", tick = Main.GameUpdateCount, x = tree.X, y = tree.Y,
                item = actor.HeldItem.type, slot = actor.selectedItem });
        }
        if (!NativeMining.IsForestTree(tree.X, tree.Y))
            NextTree(demo, "树木已变化或受到保护");
        else if (NativeMining.CanReachTree(actor, tree.X, tree.Y))
        {
            if (mining.StartChop(actor, tree.X, tree.Y)) phase = Phase.Chop;
            else NextTree(demo, "原生斧头操作被拒绝");
        }
        else if (phase == Phase.Search)
        {
            float treeX = tree.X * 16 + 8;
            float dx = treeX - actor.Center.X;
            float destination = treeX - Math.Clamp(dx, -24f, 24f) - actor.width / 2f;
            if (demo.MoveTo(destination, 210, out _)) phase = Phase.Approach;
            else NextTree(demo, "无法接近树木");
        }
        else if (!demo.NavigationActive)
            NextTree(demo, "树木受地形阻挡或在斧头范围之外");
        return null;
    }

    private Point? FindTree(Player player)
    {
        int center = (int)(player.Center.X / 16), feet = (int)(player.Bottom.Y / 16);
        Point? best = null;
        int bestDistance = int.MaxValue;
        for (int x = center - SearchRadius; x <= center + SearchRadius; x++)
        {
            if (Math.Abs(x * 16 + 8 - originX) > TaskRadius * 16) continue;
            for (int y = feet - 5; y <= feet + 3; y++)
            {
                var candidate = new Point(x, y);
                int distance = Math.Abs(x - center) + 2 * Math.Abs(y + 1 - feet);
                if (distance >= bestDistance || visited.Contains(candidate) || !NativeMining.IsForestTree(x, y)) continue;
                best = candidate;
                bestDistance = distance;
            }
        }
        return best;
    }

    private void GatherDrops(DemoPlayerSystem demo)
    {
        var item = Main.item.Take(Main.maxItems).Where(i => i.active && i.type == ItemID.Wood && Tracks(i, i.stack))
            .OrderBy(i => Vector2.DistanceSquared(i.Center, actor.Center)).FirstOrDefault();
        if (item == null || Main.GameUpdateCount >= pickupDeadline)
        {
            NextTree(demo, item == null ? "木材掉落已拾取或已不可追踪" : "未能接近并拾取剩余木材");
            return;
        }
        if (!demo.NavigationActive && Math.Abs(item.Center.X - actor.Center.X) > 12 &&
            Math.Abs(item.Center.X - originX) <= TaskRadius * 16 + 64 &&
            Math.Abs(item.Center.Y - actor.Bottom.Y) <= 80)
            demo.MoveTo(item.Center.X - actor.width / 2f, 90, out _);
    }

    private void NextTree(DemoPlayerSystem demo, string failure)
    {
        lastFailure = failure;
        Record(new { kind = "tree_finished", tick = Main.GameUpdateCount, x = tree.X, y = tree.Y, detail = failure });
        drops.Clear();
        demo.Stop();
        phase = Phase.Search;
    }

    internal void TrackDrop(Item item, Point source)
    {
        if (phase != Phase.Chop || item.stack <= 0 || Math.Abs(source.X - tree.X) > 3 ||
            source.Y < tree.Y - 32 || source.Y > tree.Y) return;
        drops[item] = (item.stack, item.stack);
        Record(new { kind = "wood_drop", tick = Main.GameUpdateCount, source.X, source.Y, quantity = item.stack });
    }

    private int OwnWood(Item item, int stack)
    {
        if (!drops.TryGetValue(item, out var expected)) return 0;
        if (expected.Stack == stack) return expected.Own;
        drops.Remove(item);
        return 0;
    }

    internal bool Tracks(Item item, int stack) => OwnWood(item, stack) > 0;

    internal void TrackStack(Item destination, Item source, int quantity)
    {
        int from = OwnWood(source, source.stack), into = OwnWood(destination, destination.stack);
        if ((from == 0 && into == 0) || !source.active || !destination.active ||
            !Main.item.Any(item => ReferenceEquals(item, source)) ||
            !Main.item.Any(item => ReferenceEquals(item, destination))) return;
        SetDrop(source, source.stack - quantity, Math.Max(0, from - quantity));
        SetDrop(destination, destination.stack + quantity,
            into + WoodCollectionPolicy.OwnInTransfer(source.stack, from, quantity));
    }

    private void SetDrop(Item item, int stack, int own)
    {
        if (own > 0) drops[item] = (stack, own);
        else drops.Remove(item);
    }

    internal void ConfirmPickup(Item item, Item remainder, int stack, int before, int after, int groundSlot)
    {
        int own = OwnWood(item, stack);
        if (own == 0) return;
        drops.Remove(item);
        int left = remainder.IsAir ? 0 : remainder.stack;
        // A different leftover item cannot establish a wood remainder.
        if (left > 0 && remainder.type != ItemID.Wood) return;
        var (added, ownLeft) = WoodCollectionPolicy.AssessPickup(stack, own, left, before, after);
        SetDrop(remainder, left, ownLeft);
        if (added == 0) return;
        acquired += added;
        Record(new { kind = "wood_pickup", tick = Main.GameUpdateCount, groundSlot,
            incoming = stack, groundRemainder = left, before, after, added, acquired });
    }

    private void Record(object entry)
    {
        if (events.Count == 96) events.RemoveAt(0);
        events.Add(entry);
    }

    private string Reason(string message) => lastFailure.Length == 0 ? message : $"{message}（上次：{lastFailure}）";

    internal void Finish(string outcome, string detail)
    {
        status = outcome;
        reason = detail;
    }

    internal object Snapshot() => new
    {
        id, resource = ItemID.Wood, source = "player", status, reason, requested, acquired, startingWood,
        phase = phase.ToString(), treesTried = visited.Count, events = events.ToArray()
    };
}

public sealed class WoodDropProbe : GlobalItem
{
    public override void OnSpawn(Item item, IEntitySource source)
    {
        if (Main.netMode == NetmodeID.Server && item.type == ItemID.Wood && source is EntitySource_TileBreak tileBreak)
            ModContent.GetInstance<NativeMining>().ObserveDrop(item, tileBreak.TileCoords);
    }

    public override void OnStack(Item destination, Item source, int numToTransfer)
    {
        if (Main.netMode == NetmodeID.Server && source.type == ItemID.Wood)
            ModContent.GetInstance<MultiplayerAgentBridge>().ObserveWoodStack(destination, source, numToTransfer);
    }
}
