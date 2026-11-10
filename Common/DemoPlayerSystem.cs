using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerrariaPlayerAgent.Common;

public sealed class DemoPlayerSystem : ModSystem
{
    internal Player? Actor { get; private set; }
    internal long StopRevision { get; private set; }
    private Player? previousSlot;
    private int moveDirection;
    private int moveTicks;
    private int jumpTicks;
    private int autoJumpCount;
    private int dropTicks, stalledTicks, recoveryJumps;
    private float progressX;
    private string blockedReason = "";
    private readonly GroundNavigation navigation = new();
    private int updatesThisTick;
    private int lastUpdates;
    private long totalUpdates;
    private int attackTicks;
    private int attackDirection;
    private int movementProbeTicks;
    private float movementProbeStartX;
    private readonly List<object> combatEvents = new();
    private readonly List<NPC> testEnemies = new();
    private int hitCount, hurtCount, deathCount, respawnCount;

    internal string Spawn(Player owner)
    {
        if (Actor != null)
            return "Demo already exists. Use /tpa status or /tpa remove.";

        int slot = -1;
        if (Main.netMode == NetmodeID.Server)
        {
            for (int i = Main.maxPlayers - 1; i >= 0; i--)
                if (!Main.player[i].active && Netplay.Clients[i].State == 0)
                { slot = i; break; }
        }
        else slot = Array.FindIndex(Main.player, 0, Main.maxPlayers,
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
        navigation.Clear();
        ModContent.GetInstance<NativeMining>().Reset();
        Main.player[slot] = actor;
        totalUpdates = 0;
        lastUpdates = 0;
        autoJumpCount = 0;
        combatEvents.Clear();
        hitCount = hurtCount = deathCount = respawnCount = 0;
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
        progressX = Actor?.position.X ?? 0;
        if (Main.netMode == NetmodeID.Server && Actor != null)
        {
            movementProbeTicks = ticks + 30;
            movementProbeStartX = Actor.position.X;
            Mod.Logger.Info($"[TPA-Move] start direction={direction}, ticks={ticks}, x={Actor.position.X}, active={Actor.active}");
        }
    }

    internal bool SetRange(int tiles)
    {
        if (Actor == null || Actor.dead || tiles < 0 || tiles > 100) return false;
        RequestStop();
        navigation.SetRange(Actor, tiles);
        return true;
    }

    internal bool MoveTo(float? x, int ticks, out string message)
    {
        message = "No living AI, or home requires an activity range first.";
        if (Actor == null || Actor.dead || (!x.HasValue && !navigation.Bounded)) return false;
        float goal = x ?? navigation.HomeX;
        string? invalid = navigation.Validate(Actor, goal);
        if (invalid != null) { message = invalid; return false; }
        Stop();
        navigation.Start(Actor, goal);
        moveTicks = ticks;
        progressX = Actor.position.X;
        message = $"Moving toward world pixel X={goal:F2}, tolerance={GroundNavigation.Tolerance}, at most {ticks / 60.0:F2} simulation seconds.";
        return true;
    }

    internal void Jump()
    {
        attackTicks = dropTicks = 0;
        blockedReason = "";
        stalledTicks = recoveryJumps = 0;
        jumpTicks = 12;
    }

    internal void Drop(int ticks)
    {
        Stop();
        dropTicks = ticks;
    }

    internal void Stop()
    {
        ModContent.GetInstance<NativeMining>().Cancel();
        navigation.Cancel();
        moveTicks = jumpTicks = moveDirection = attackTicks = dropTicks = stalledTicks = recoveryJumps = 0;
        blockedReason = "";
        if (Actor != null)
            ClearInput(Actor);
    }

    internal void RequestStop()
    {
        StopRevision++;
        Stop();
    }

    internal void Attack(int direction, int ticks)
    {
        Stop();
        attackDirection = direction;
        attackTicks = ticks;
    }

    internal string Equip(string weapon)
    {
        if (Actor == null || Actor.dead) return "No living demo. Spawn one or wait for automatic respawn.";
        int type = weapon switch { "wood" => ItemID.WoodenSword, "iron" => ItemID.IronBroadsword,
            "pick" => ItemID.CopperPickaxe, _ => 0 };
        if (type == 0) return "Equipment must be wood, iron or pick.";
        Stop();
        int slot = weapon == "pick" ? 1 : 0;
        Actor.inventory[slot].SetDefaults(type);
        Actor.selectedItem = slot;
        Actor.releaseUseItem = true;
        return $"Equipped vanilla {Actor.HeldItem.Name} (item={type}, damage={Actor.HeldItem.damage}).";
    }

    internal bool SelectItem(int slot)
    {
        if (Actor == null || Actor.dead || slot < 0 || slot >= 50 || Actor.inventory[slot].IsAir) return false;
        Stop();
        Actor.selectedItem = slot;
        Actor.releaseUseItem = true;
        return true;
    }

    internal string SpawnEnemy(string kind)
    {
        if (Actor == null || Actor.dead) return "No living demo. Spawn one or wait for automatic respawn.";
        int type = kind switch { "slime" => NPCID.BlueSlime, "zombie" => NPCID.Zombie, "eye" => NPCID.DemonEye, _ => -1 };
        if (type < 0) return "Enemy must be slime, zombie or eye.";
        Vector2 bottom = Actor.Bottom + new Vector2(Actor.direction * 28, 0);
        int slot = NPC.NewNPC(Actor.GetSource_Misc("TPA combat test"), (int)bottom.X, (int)bottom.Y, type);
        if (slot >= Main.maxNPCs) return "No free NPC slot.";
        NPC npc = Main.npc[slot];
        testEnemies.Add(npc);
        Mod.Logger.Info($"[TPA] Test enemy slot={slot}, type={npc.type}, life={npc.life}, damage={npc.damage}");
        return $"Spawned vanilla {kind}, NPC slot={slot}, life={npc.life}, damage={npc.damage}.";
    }

    internal void ClearEnemies()
    {
        foreach (NPC npc in testEnemies)
            if (ReferenceEquals(Main.npc[npc.whoAmI], npc)) npc.active = false;
        testEnemies.Clear();
    }

    private void RecordCombat(object entry)
    {
        if (combatEvents.Count == 64) combatEvents.RemoveAt(0);
        combatEvents.Add(entry);
        Mod.Logger.Info($"[TPA] Combat {System.Text.Json.JsonSerializer.Serialize(entry)}");
    }

    internal void RecordHit(Item item, NPC npc, int damage)
    {
        hitCount++;
        RecordCombat(new { kind = "item-hit", tick = Main.GameUpdateCount, attacker = Actor!.whoAmI,
            item = item.type, npc = npc.whoAmI, npcType = npc.type, damage, targetLifeAfter = npc.life });
    }

    internal void RecordHurt(Player.HurtInfo info)
    {
        hurtCount++;
        int source = info.DamageSource.SourceNPCIndex;
        RecordCombat(new { kind = "hurt", tick = Main.GameUpdateCount, victim = Actor!.whoAmI,
            npc = source, npcType = source >= 0 ? Main.npc[source].type : -1,
            damage = info.Damage, lifeBefore = Actor.statLife });
    }

    internal void RecordDeath(PlayerDeathReason reason)
    {
        deathCount++;
        Stop();
        RecordCombat(new { kind = "death", tick = Main.GameUpdateCount, victim = Actor!.whoAmI,
            npc = reason.SourceNPCIndex, life = Actor.statLife, dead = Actor.dead,
            respawnTimer = Actor.respawnTimer });
    }

    internal void RecordRespawn()
    {
        respawnCount++;
        Stop();
        RecordCombat(new { kind = "respawn", tick = Main.GameUpdateCount, slot = Actor!.whoAmI,
            life = Actor.statLife, dead = Actor.dead, item = Actor.HeldItem.type,
            x = Actor.position.X, y = Actor.position.Y,
            worldSpawnX = Main.spawnTileX, worldSpawnY = Main.spawnTileY });
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
        RequestStop();
        ClearEnemies();
        if (Actor == null)
            return;
        int slot = Actor.whoAmI;
        // A connecting real client takes priority over the experimental virtual slot.
        bool claimed = Main.netMode == NetmodeID.Server && Netplay.Clients[slot].State != 0;
        if (!claimed)
        {
            Actor.active = false;
            if (ReferenceEquals(Main.player[slot], Actor) && previousSlot != null)
                Main.player[slot] = previousSlot;
        }
        Mod.Logger.Info($"[TPA] Removed slot={slot}, updates={totalUpdates}");
        Actor = null;
        movementProbeTicks = 0;
        previousSlot = null;
        navigation.Clear();
    }

    public override void PreUpdatePlayers()
    {
        if (Actor == null)
            return;
        if (Main.netMode == NetmodeID.MultiplayerClient || !ReferenceEquals(Main.player[Actor.whoAmI], Actor))
        {
            Remove();
            return;
        }
        updatesThisTick = 0;
        ClearInput(Actor);
        if (Actor.dead || !Actor.active)
        {
            if (moveTicks > 0 && Main.netMode == NetmodeID.Server)
                Mod.Logger.Warn($"[TPA-Move] cancelled remaining={moveTicks}, active={Actor.active}, dead={Actor.dead}");
            Stop();
            return;
        }
        if (navigation.Active)
        {
            if (moveTicks == 0)
            {
                navigation.Finish("timeout", "target_move_time_limit");
                blockedReason = "target_move_time_limit";
            }
            else moveDirection = navigation.Direction(Actor);
            if (!navigation.Active) moveTicks = jumpTicks = 0;
        }
        if (moveTicks > 0 && navigation.AtOutwardBoundary(Actor, moveDirection, navigation.Active))
        {
            navigation.Finish("blocked", "activity_boundary");
            blockedReason = "activity_boundary";
            moveTicks = jumpTicks = 0;
        }
        if (moveTicks > 0 && moveDirection != 0) AdjustMovement(Actor);
        Actor.controlLeft = moveTicks > 0 && moveDirection < 0;
        Actor.controlRight = moveTicks > 0 && moveDirection > 0;
        if (moveTicks > 0 && jumpTicks == 0 && LocalTerrain.Grounded(Actor) &&
            Actor.releaseJump && LocalTerrain.CanJumpObstacle(Actor, moveDirection))
        {
            jumpTicks = 12;
            autoJumpCount++;
            Mod.Logger.Info($"[TPA] AutoJump tick={Main.GameUpdateCount}, direction={moveDirection}, position={Actor.position}");
        }
        Actor.controlJump = jumpTicks > 0;
        Actor.controlDown = dropTicks > 0;
        ModContent.GetInstance<NativeMining>().PrepareInput(Actor);
        if (attackTicks > 0)
        {
            Actor.direction = attackDirection;
            Actor.controlUseItem = true;
            // A new press between swings preserves vanilla non-auto-reuse swords.
            if (Actor.itemAnimation == 0) Actor.releaseUseItem = true;
            attackTicks--;
        }
        if (moveTicks > 0) moveTicks--;
        if (jumpTicks > 0) jumpTicks--;
        if (dropTicks > 0) dropTicks--;
        // The engine updates and draws this occupied slot. Do not call Player.Update again.
    }

    private void AdjustMovement(Player player)
    {
        if ((player.position.X - progressX) * moveDirection > 4)
        {
            progressX = player.position.X;
            stalledTicks = 0;
        }
        else stalledTicks++;
        if (stalledTicks < 45 || !LocalTerrain.Grounded(player) || !player.releaseJump) return;
        if (recoveryJumps < 2 && LocalTerrain.HeadClear(player))
        {
            jumpTicks = 18;
            recoveryJumps++;
            stalledTicks = 0;
            Mod.Logger.Info($"[TPA] MovementRecovery tick={Main.GameUpdateCount}, attempt={recoveryJumps}, position={player.position}");
            return;
        }
        blockedReason = LocalTerrain.HeadClear(player) ? "no_progress_after_jump_attempts" : "blocked_with_low_ceiling";
        if (navigation.Active) navigation.Finish("blocked", blockedReason);
        moveTicks = jumpTicks = 0;
        Mod.Logger.Info($"[TPA] MovementBlocked tick={Main.GameUpdateCount}, reason={blockedReason}, position={player.position}");
    }

    internal void RecordUpdate(Player player)
    {
        if (ReferenceEquals(player, Actor))
        {
            updatesThisTick++;
            totalUpdates++;
        }
    }

    public override void PostUpdatePlayers()
    {
        lastUpdates = updatesThisTick;
        if (Main.netMode != NetmodeID.Server || Actor == null || movementProbeTicks <= 0) return;
        movementProbeTicks--;
        if (movementProbeTicks % 10 == 0)
            Mod.Logger.Info($"[TPA-Move] sample tick={Main.GameUpdateCount}, remaining={moveTicks}, " +
                $"active={Actor.active}, updates={updatesThisTick}, left={Actor.controlLeft}, right={Actor.controlRight}, " +
                $"position={Actor.position}, velocity={Actor.velocity}, frozen={Actor.frozen}, " +
                $"stoned={Actor.stoned}, webbed={Actor.webbed}, " +
                $"acceleration={Actor.runAcceleration}, maxSpeed={Actor.maxRunSpeed}, blocked={blockedReason}");
        if (movementProbeTicks == 0)
            Mod.Logger.Info($"[TPA-Move] end deltaX={Actor.position.X - movementProbeStartX}");
    }

    internal object? Snapshot() => Actor == null ? null : new
    {
        slot = Actor.whoAmI,
        x = Actor.position.X, y = Actor.position.Y,
        velocityX = Actor.velocity.X, velocityY = Actor.velocity.Y,
        life = Actor.statLife, dead = Actor.dead, respawnTimer = Actor.respawnTimer,
        moveTicks, jumpTicks, attackTicks, dropTicks, autoJumpCount, recoveryJumps, blockedReason,
        navigation = navigation.Snapshot(Actor),
        terrain = LocalTerrain.Observe(Actor),
        mining = ModContent.GetInstance<NativeMining>().Snapshot(),
        mineCandidates = NativeMining.Candidates(Actor),
        mineTicks = ModContent.GetInstance<NativeMining>().Ticks,
        selectedSlot = Actor.selectedItem,
        nearbyHostiles = Main.npc.Where(n => n.active && !n.friendly && n.life > 0 && n.damage > 0 &&
                Vector2.DistanceSquared(n.Center, Actor.Center) <= 480 * 480)
            .OrderBy(n => Vector2.DistanceSquared(n.Center, Actor.Center)).Take(8)
            .Select(n => new { slot = n.whoAmI, type = n.type, name = n.FullName,
                x = n.position.X, y = n.position.Y, width = n.width, height = n.height,
                dx = n.Center.X - Actor.Center.X, dy = n.Center.Y - Actor.Center.Y,
                distancePixels = Vector2.Distance(n.Center, Actor.Center),
                velocityX = n.velocity.X, velocityY = n.velocity.Y, life = n.life,
                contactDamage = n.damage, touching = n.Hitbox.Intersects(Actor.Hitbox) }).ToArray(),
        inventory = Actor.inventory.Take(50).Select((item, slot) => new { slot, item })
            .Where(entry => !entry.item.IsAir).Select(entry => new { entry.slot, type = entry.item.type,
                name = entry.item.Name, stack = entry.item.stack, pick = entry.item.pick, shoot = entry.item.shoot }).ToArray(),
        direction = Actor.direction, item = Actor.HeldItem.type,
        itemAnimation = Actor.itemAnimation, usingItem = Actor.controlUseItem,
        hitCount, hurtCount, deathCount, respawnCount,
        combatEvents = combatEvents.ToArray(),
        enemies = testEnemies.Select(n => new { slot = n.whoAmI, type = n.type,
            active = ReferenceEquals(Main.npc[n.whoAmI], n) && n.active,
            life = n.life, x = n.position.X, y = n.position.Y }).ToArray(),
        left = Actor.controlLeft, right = Actor.controlRight, jumping = Actor.controlJump,
        updatesPerTick = lastUpdates, totalUpdates
    };

    internal string Status(Player owner)
    {
        if (Actor == null)
            return "No demo. Use /tpa spawn.";
        return $"AI slot={Actor.whoAmI}, human={owner.whoAmI}, sameObject={ReferenceEquals(Actor, owner)}; " +
            $"pos=({Actor.position.X:F1},{Actor.position.Y:F1}), velocity=({Actor.velocity.X:F2},{Actor.velocity.Y:F2}); " +
            $"life={Actor.statLife}/{Actor.statLifeMax2}, dead={Actor.dead}, respawnTicks={Actor.respawnTimer}; " +
            $"moveTicks={moveTicks}, jumpTicks={jumpTicks}, autoJumps={autoJumpCount}, attackTicks={attackTicks}, item={Actor.HeldItem.type}; " +
            $"hits={hitCount}, hurts={hurtCount}, deaths={deathCount}, respawns={respawnCount}, updates/tick={lastUpdates}, total={totalUpdates}.";
    }

    public override void OnWorldUnload() => Remove();
    public override void ClearWorld() => Remove();
    public override void OnModUnload() => Remove();
}

public sealed class DemoUpdateProbe : ModPlayer
{
    public override void PreUpdate() => ModContent.GetInstance<DemoPlayerSystem>().RecordUpdate(Player);

    private DemoPlayerSystem System => ModContent.GetInstance<DemoPlayerSystem>();
    private bool IsActor => Main.netMode == NetmodeID.SinglePlayer && ReferenceEquals(Player, System.Actor);

    public override void PostUpdate()
    {
        // Player.Update's local-only interaction block skips this for extra slots.
        if (IsActor && Player.whoAmI != Main.myPlayer && !Player.dead)
            Player.Update_NPCCollision();
    }

    public override void OnHitNPCWithItem(Item item, NPC target, NPC.HitInfo hit, int damageDone)
    {
        if (IsActor) System.RecordHit(item, target, damageDone);
    }

    public override void OnHurt(Player.HurtInfo info)
    {
        if (IsActor) System.RecordHurt(info);
    }

    public override void Kill(double damage, int hitDirection, bool pvp, PlayerDeathReason damageSource)
    {
        if (IsActor) System.RecordDeath(damageSource);
    }
}
