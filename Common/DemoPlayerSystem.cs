using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerrariaPlayerAgent.Common;

public sealed class DemoPlayerSystem : ModSystem
{
    internal Player? Actor { get; private set; }
    private Player? previousSlot;
    private int moveDirection;
    private int moveTicks;
    private int jumpTicks;
    private int updatesThisTick;
    private int lastUpdates;
    private long totalUpdates;

    internal string Spawn(Player owner)
    {
        if (Actor != null)
            return "Demo already exists. Use /tpa status or /tpa remove.";

        int slot = Array.FindIndex(Main.player, 0, Main.maxPlayers,
            p => !p.active && !ReferenceEquals(p, owner));
        if (slot < 0)
            return "No free player slot.";

        Vector2? spawn = FindSpawn(owner);
        if (spawn == null)
            return "No clear space nearby. Move to open ground and try again.";

        var actor = new Player
        {
            whoAmI = slot,
            name = "Agent Demo",
            position = spawn.Value,
            active = true,
            statLife = 100,
            statLifeMax = 100,
            statMana = 20,
            direction = owner.direction,
            releaseJump = true,
            shirtColor = new Color(60, 150, 230),
            underShirtColor = new Color(220, 230, 240),
            pantsColor = new Color(45, 55, 85),
            shoeColor = new Color(70, 65, 55)
        };
        actor.fallStart = actor.fallStart2 = (int)(actor.position.Y / 16);
        previousSlot = Main.player[slot];
        Actor = actor;
        Main.player[slot] = actor;
        totalUpdates = 0;
        lastUpdates = 0;
        Mod.Logger.Info($"[TPA] Spawn slot={slot}, human={owner.whoAmI}, position={actor.position}");
        return $"Spawned Agent Demo in player slot {slot}. Use /tpa status.";
    }

    private static Vector2? FindSpawn(Player owner)
    {
        foreach (int side in new[] { owner.direction, -owner.direction })
        {
            for (int rise = 0; rise <= 5; rise++)
            {
                Vector2 position = owner.Bottom + new Vector2(80 * side - 10, -42 - rise * 16);
                if (position.X >= 16 && position.Y >= 16 &&
                    position.X + 20 < (Main.maxTilesX - 1) * 16 &&
                    position.Y + 42 < (Main.maxTilesY - 1) * 16 &&
                    !Collision.SolidCollision(position, 20, 42))
                    return position;
            }
        }
        return null;
    }

    internal void Move(int direction, int ticks)
    {
        Stop();
        moveDirection = direction;
        moveTicks = ticks;
    }

    internal void Jump()
    {
        Stop();
        jumpTicks = 12;
    }

    internal void Stop()
    {
        moveTicks = jumpTicks = moveDirection = 0;
        if (Actor != null)
            ClearInput(Actor);
    }

    private static void ClearInput(Player player)
    {
        player.controlLeft = player.controlRight = player.controlUp = player.controlDown = false;
        player.controlJump = player.controlUseItem = player.controlUseTile = false;
        player.controlThrow = player.controlHook = player.controlMount = false;
        player.controlInv = player.controlQuickHeal = player.controlQuickMana = false;
    }

    internal void Remove()
    {
        Stop();
        if (Actor == null)
            return;
        int slot = Actor.whoAmI;
        Actor.active = false;
        if (ReferenceEquals(Main.player[slot], Actor) && previousSlot != null)
            Main.player[slot] = previousSlot;
        Mod.Logger.Info($"[TPA] Removed slot={slot}, updates={totalUpdates}");
        Actor = null;
        previousSlot = null;
    }

    public override void PreUpdatePlayers()
    {
        if (Actor == null)
            return;
        if (Main.netMode != NetmodeID.SinglePlayer || !ReferenceEquals(Main.player[Actor.whoAmI], Actor))
        {
            Remove();
            return;
        }
        updatesThisTick = 0;
        ClearInput(Actor);
        if (Actor.dead || !Actor.active)
        {
            Stop();
            return;
        }
        Actor.controlLeft = moveTicks > 0 && moveDirection < 0;
        Actor.controlRight = moveTicks > 0 && moveDirection > 0;
        Actor.controlJump = jumpTicks > 0;
        if (moveTicks > 0) moveTicks--;
        if (jumpTicks > 0) jumpTicks--;
        // The engine updates and draws this occupied slot. Do not call Player.Update again.
    }

    internal void RecordUpdate(Player player)
    {
        if (ReferenceEquals(player, Actor))
        {
            updatesThisTick++;
            totalUpdates++;
        }
    }

    public override void PostUpdatePlayers() => lastUpdates = updatesThisTick;

    internal object? Snapshot() => Actor == null ? null : new
    {
        slot = Actor.whoAmI,
        x = Actor.position.X, y = Actor.position.Y,
        velocityX = Actor.velocity.X, velocityY = Actor.velocity.Y,
        life = Actor.statLife, dead = Actor.dead,
        moveTicks, jumpTicks,
        left = Actor.controlLeft, right = Actor.controlRight, jumping = Actor.controlJump,
        updatesPerTick = lastUpdates, totalUpdates
    };

    internal string Status(Player owner)
    {
        if (Actor == null)
            return "No demo. Use /tpa spawn.";
        return $"AI slot={Actor.whoAmI}, human={owner.whoAmI}, sameObject={ReferenceEquals(Actor, owner)}; " +
            $"pos=({Actor.position.X:F1},{Actor.position.Y:F1}), velocity=({Actor.velocity.X:F2},{Actor.velocity.Y:F2}); " +
            $"life={Actor.statLife}/{Actor.statLifeMax2}, dead={Actor.dead}; " +
            $"moveTicks={moveTicks}, jumpTicks={jumpTicks}, updates/tick={lastUpdates}, total={totalUpdates}.";
    }

    public override void OnWorldUnload() => Remove();
    public override void ClearWorld() => Remove();
    public override void OnModUnload() => Remove();
}

public sealed class DemoUpdateProbe : ModPlayer
{
    public override void PreUpdate() => ModContent.GetInstance<DemoPlayerSystem>().RecordUpdate(Player);
}
