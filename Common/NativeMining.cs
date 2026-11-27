using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerrariaPlayerAgent.Common;

// Swing pickaxes use the engine's item timing, range and PickTile rules.
public sealed class NativeMining : ModSystem
{
    internal int Ticks { get; private set; }
    private int targetX, targetY, sequence, initialType, itemType, pickPower, attempts;
    private string status = "idle", reason = "";
    private readonly List<object> events = new();
    private Player? miner;
    private bool chopping, inChopCall;
    internal string Result => status;
    internal string Reason => reason;
    internal static bool Active(Player player) =>
        (Main.netMode == NetmodeID.SinglePlayer &&
            ReferenceEquals(player, ModContent.GetInstance<DemoPlayerSystem>().Actor) &&
            ModContent.GetInstance<NativeMining>().Ticks > 0) || ActiveChop(player);

    internal static bool ActiveChop(Player player) =>
        Main.netMode == NetmodeID.Server && !player.dead && SupportedAxe(player.HeldItem) &&
        NativeCombat.IsAuthoritativeActor(player) &&
        ModContent.GetInstance<NativeMining>() is { Ticks: > 0, chopping: true } mining &&
        ReferenceEquals(mining.miner, player);

    public override void Load()
    {
        IL_Player.ItemCheck_Inner += EnterOwnerOnly;
        On_Player.ItemCheck_UseMiningTools += UseMiningTools;
        On_Player.ItemCheck_UseMiningTools_ActuallyUseMiningTool += UseTool;
        On_Player.PickTile += PickTile;
    }

    public override void Unload()
    {
        IL_Player.ItemCheck_Inner -= EnterOwnerOnly;
        On_Player.ItemCheck_UseMiningTools -= UseMiningTools;
        On_Player.ItemCheck_UseMiningTools_ActuallyUseMiningTool -= UseTool;
        On_Player.PickTile -= PickTile;
    }

    private static void EnterOwnerOnly(ILContext il)
    {
        var cursor = new ILCursor(il);
        if (!cursor.TryGotoNext(i => i.MatchCall<Player>("ItemCheck_OwnerOnlyCode")) ||
            !cursor.TryGotoPrev(MoveType.After, i => i.MatchLdsfld<Main>(nameof(Main.myPlayer))))
            throw new InvalidOperationException("TPA mining owner branch missing; recheck tML version.");
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.EmitDelegate<Func<int, Player, int>>((owner, player) => Active(player) ? player.whoAmI : owner);
    }

    private static bool InBounds(int x, int y) => x > 0 && y > 0 && x < Main.maxTilesX - 1 && y < Main.maxTilesY - 1;

    private static T AtTarget<T>(int x, int y, Func<T> action)
    {
        int previousX = Player.tileTargetX, previousY = Player.tileTargetY;
        try
        {
            Player.tileTargetX = x;
            Player.tileTargetY = y;
            return action();
        }
        finally
        {
            Player.tileTargetX = previousX;
            Player.tileTargetY = previousY;
        }
    }

    private static bool InRange(Player player, int x, int y) => InBounds(x, y) &&
        AtTarget(x, y, () => player.IsTargetTileInItemRange(player.HeldItem));

    internal static bool SupportedAxe(Item item) => !item.IsAir && item.axe > 0 && item.pick == 0 &&
        item.hammer == 0 && item.shoot <= ProjectileID.None && item.useStyle == ItemUseStyleID.Swing;

    internal static bool IsForestTree(int x, int y)
    {
        if (!InBounds(x, y) || y < 2) return false;
        Tile trunk = Main.tile[x, y], above = Main.tile[x, y - 1], ground = Main.tile[x, y + 1];
        if (!trunk.HasTile || trunk.IsActuated || trunk.TileType != TileID.Trees ||
            !above.HasTile || above.IsActuated || above.TileType != TileID.Trees ||
            !ground.HasTile || ground.IsActuated || ground.TileType != TileID.Grass)
            return false;
        // Exclude side-root and branch frames before the native bottom lookup.
        int frameX = trunk.TileFrameX / 22, frameY = trunk.TileFrameY / 22;
        if (frameX == 3 && (frameY <= 2 || frameY >= 9) ||
            frameX == 4 && frameY is >= 3 and <= 5 ||
            frameX == 1 && frameY is >= 6 and <= 8 ||
            frameX == 2 && frameY >= 6)
            return false;
        WorldGen.GetTreeBottom(x, y, out int bottomX, out int bottomY);
        return bottomX == x && bottomY == y + 1 && WorldGen.CanKillTile(x, y);
    }

    internal static bool CanReachTree(Player player, int x, int y) => InRange(player, x, y) &&
        Collision.CanHitLine(player.position, player.width, player.height, new Vector2(x * 16 + 4, y * 16 + 4), 8, 8);

    internal bool StartChop(Player player, int x, int y)
    {
        if (Main.netMode != NetmodeID.Server || !NativeCombat.IsAuthoritativeActor(player) ||
            player.dead || player.noBuilding || !SupportedAxe(player.HeldItem) ||
            !IsForestTree(x, y) || !CanReachTree(player, x, y)) return false;
        Begin(player, x, y, 600, true);
        return true;
    }

    internal bool Start(Player player, int x, int y, int ticks, out string message)
    {
        message = "Select a swing pickaxe first (pick > 0, no projectile).";
        if (player.HeldItem.pick <= 0 || player.HeldItem.shoot > ProjectileID.None) return false;
        message = "Target is outside the held tool's native range, or building is disabled.";
        if (!InRange(player, x, y) || player.noBuilding) return false;
        Tile tile = Main.tile[x, y];
        message = "Target has no active tile to mine.";
        if (!tile.HasTile || tile.IsActuated) return false;
        message = $"native_cannot_kill_tile: ({x},{y}) is protected by world rules; above={TileName(x, y - 1)}. Choose another candidate.";
        if (!WorldGen.CanKillTile(x, y)) return false;
        Begin(player, x, y, ticks, false);
        message = $"Mining tile ({x},{y}) with {player.HeldItem.Name} for at most {ticks / 60.0:F2} simulation seconds.";
        return true;
    }

    private void Begin(Player player, int x, int y, int ticks, bool chop)
    {
        ModContent.GetInstance<DemoPlayerSystem>().Stop();
        miner = player;
        chopping = chop;
        targetX = x;
        targetY = y;
        initialType = Main.tile[x, y].TileType;
        itemType = player.HeldItem.type;
        pickPower = player.HeldItem.pick;
        attempts = 0;
        events.Clear();
        sequence++;
        status = "active";
        reason = "";
        Ticks = ticks;
        player.releaseUseItem = true;
    }

    internal void Cancel()
    {
        if (Ticks > 0) Finish("cancelled", "input_cancelled");
    }

    internal void Reset()
    {
        Cancel();
        miner = null;
        chopping = inChopCall = false;
        status = "idle";
        events.Clear();
    }

    public override void OnWorldUnload() => Reset();

    private void Finish(string outcome, string detail)
    {
        status = outcome;
        reason = detail;
        Ticks = 0;
        if (miner != null) miner.controlUseItem = false;
        Mod.Logger.Info($"[TPA] MiningResult {System.Text.Json.JsonSerializer.Serialize(Snapshot())}");
    }

    internal void PrepareInput(Player player)
    {
        if (chopping && Ticks > 0 && ReferenceEquals(miner, player) && !ValidateChop(player)) return;
        if (!Active(player)) return;
        if (!InRange(player, targetX, targetY) || player.noBuilding)
        { Finish("blocked", "out_of_range_or_building_disabled"); return; }
        if (!Main.tile[targetX, targetY].HasTile || Main.tile[targetX, targetY].IsActuated)
        { Finish("unconfirmed", "target_changed_outside_ai_pick_call"); return; }
        if (!WorldGen.CanKillTile(targetX, targetY))
        { Finish("blocked", "native_cannot_kill_tile"); return; }
        player.direction = targetX * 16 + 8 < player.Center.X ? -1 : 1;
        player.controlUseItem = true;
        if (player.itemAnimation == 0) player.releaseUseItem = true;
    }

    private bool ValidateChop(Player player)
    {
        if (ActiveChop(player) && player.HeldItem.type == itemType && !player.noBuilding &&
            IsForestTree(targetX, targetY) && CanReachTree(player, targetX, targetY)) return true;
        Finish("blocked", "tree_tool_or_range_changed");
        return false;
    }

    public override void PostUpdatePlayers()
    {
        if (Ticks > 0 && --Ticks == 0) Finish("unconfirmed", "time_limit_without_confirmed_break");
    }

    private static void UseMiningTools(On_Player.orig_ItemCheck_UseMiningTools orig, Player player, Item item)
    {
        if (!ReferenceEquals(player, ModContent.GetInstance<DemoPlayerSystem>().Actor))
        { orig(player, item); return; }
        if (!Active(player)) return;
        var mining = ModContent.GetInstance<NativeMining>();
        if (mining.chopping && !mining.ValidateChop(player)) return;
        AtTarget(mining.targetX, mining.targetY, () => { orig(player, item); return true; });
    }

    private static void UseTool(On_Player.orig_ItemCheck_UseMiningTools_ActuallyUseMiningTool orig,
        Player player, Item item, out bool canHitWalls, int x, int y)
    {
        if (!ActiveChop(player))
        {
            orig(player, item, out canHitWalls, x, y);
            return;
        }
        var mining = ModContent.GetInstance<NativeMining>();
        if (x != mining.targetX || y != mining.targetY || !mining.ValidateChop(player))
        {
            canHitWalls = false;
            return;
        }
        mining.inChopCall = true;
        try { orig(player, item, out canHitWalls, x, y); }
        finally { mining.inChopCall = false; }
        mining.attempts++;
        if (Main.tile[x, y].HasTile && Main.tile[x, y].TileType == TileID.Trees) return;
        int left = Math.Max(0, x - 3), top = Math.Max(0, y - 32);
        NetMessage.SendTileSquare(-1, left, top, Math.Min(7, Main.maxTilesX - left),
            Math.Min(y + 3, Main.maxTilesY) - top);
        mining.Finish("broken", "native_axe_removed_trunk");
    }

    internal void ObserveDrop(Item item, Point source)
    {
        if (inChopCall)
            ModContent.GetInstance<MultiplayerAgentBridge>().ObserveWoodDrop(item, source);
    }

    private static void PickTile(On_Player.orig_PickTile orig, Player player, int x, int y, int power)
    {
        var mining = ModContent.GetInstance<NativeMining>();
        bool tracked = !mining.chopping && Active(player) && x == mining.targetX && y == mining.targetY;
        bool hadTile = tracked && Main.tile[x, y].HasTile;
        int beforeType = tracked ? Main.tile[x, y].TileType : -1;
        bool canKillBefore = tracked && WorldGen.CanKillTile(x, y);
        orig(player, x, y, power);
        if (!tracked) return;
        Tile after = Main.tile[x, y];
        mining.attempts++;
        if (mining.events.Count == 8) mining.events.RemoveAt(0);
        var entry = new { kind = "pick-tile", tick = Main.GameUpdateCount, actor = player.whoAmI,
            item = player.HeldItem.type, pickPower = power, x, y, hadTile, beforeType, canKillBefore,
            hasTileAfter = after.HasTile, typeAfter = after.HasTile ? (int)after.TileType : -1,
            broken = hadTile && !after.HasTile };
        mining.events.Add(entry);
        ModContent.GetInstance<NativeMining>().Mod.Logger.Info($"[TPA] Mining {System.Text.Json.JsonSerializer.Serialize(entry)}");
        if (entry.broken) mining.Finish("broken", "native_pick_removed_target");
    }

    internal object? Snapshot() => status == "idle" ? null : new
    {
        sequence, status, reason, ticks = Ticks, x = targetX, y = targetY, initialType,
        item = itemType, pickPower, attempts, action = chopping ? "chop" : "pick",
        hasTileNow = InBounds(targetX, targetY) && Main.tile[targetX, targetY].HasTile,
        events = events.ToArray()
    };

    private static string TileName(int x, int y) => InBounds(x, y) && Main.tile[x, y].HasTile
        ? TileID.Search.GetName(Main.tile[x, y].TileType) : "none";

    internal static object Inspect(int x, int y)
    {
        if (!InBounds(x, y)) throw new ArgumentOutOfRangeException(nameof(x), "Tile outside world bounds.");
        Tile tile = Main.tile[x, y], above = Main.tile[x, y - 1];
        return new { x, y, hasTile = tile.HasTile, type = tile.TileType, name = TileName(x, y),
            canKill = WorldGen.CanKillTile(x, y),
            above = new { hasTile = above.HasTile, type = above.TileType, name = TileName(x, y - 1),
                isTreeTrunk = above.HasTile && TileID.Sets.IsATreeTrunk[above.TileType],
                frameX = above.TileFrameX, frameY = above.TileFrameY } };
    }

    internal static object[] Candidates(Player player)
    {
        int centerX = (int)(player.Center.X / 16), footY = (int)(player.Bottom.Y / 16);
        var tiles = new List<(int x, int y)>();
        for (int x = centerX - 6; x <= centerX + 6; x++)
            for (int y = footY - 3; y <= footY + 2; y++)
                if (InRange(player, x, y))
                {
                    Tile tile = Main.tile[x, y];
                    if (tile.HasTile && !tile.IsActuated && Main.tileSolid[tile.TileType] &&
                        !Main.tileSolidTop[tile.TileType] && !Main.tileAxe[tile.TileType] && !Main.tileHammer[tile.TileType] &&
                        WorldGen.CanKillTile(x, y))
                        tiles.Add((x, y));
                }
        return tiles.OrderBy(t => Math.Abs(t.x - centerX) + Math.Abs(t.y - footY)).Take(12)
            .Select(t => (object)new { t.x, t.y, type = Main.tile[t.x, t.y].TileType,
                name = TileID.Search.GetName(Main.tile[t.x, t.y].TileType), canKill = true }).ToArray();
    }
}
