using System;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerrariaPlayerAgent.Common;

// The owner checks used here were inspected in tML 2026.08.3.0. Never change Main.myPlayer.
public sealed class NativeCombat : ModSystem
{
    private sealed record ShortswordShot(Player Owner, int AttackSequence);
    private static readonly ConditionalWeakTable<Projectile, ShortswordShot> shortswordOwners = new();

    internal static int? ShortswordAttackSequence(Projectile projectile) =>
        shortswordOwners.TryGetValue(projectile, out var shot) ? shot.AttackSequence : null;

    public override void Load()
    {
        IL_Player.ItemCheck_Inner += ItemStart;
        IL_Player.ItemCheck_OwnerOnlyCode += MeleeOwner;
        IL_Player.ItemCheck_Shoot += ProjectileStart;
        IL_Player.ItemCheck_ApplyUseStyle_Inner += ServerSwingPose;
        IL_Player.UpdateMeleeHitCooldowns += MeleeCooldown;
        IL_Projectile.Damage += ServerShortswordDamage;
        On_Projectile.NewProjectile_IEntitySource_float_float_float_float_int_int_float_int_float_float_float += TrackShortsword;
        On_Player.ItemCheck_PlayInstruments += PlayInstruments;
        IL_Player.Hurt_HurtInfo_bool += LethalHurt;
        On_Player.UpdateDead += UpdateDead;
    }

    public override void Unload()
    {
        IL_Player.ItemCheck_Inner -= ItemStart;
        IL_Player.ItemCheck_OwnerOnlyCode -= MeleeOwner;
        IL_Player.ItemCheck_Shoot -= ProjectileStart;
        IL_Player.ItemCheck_ApplyUseStyle_Inner -= ServerSwingPose;
        IL_Player.UpdateMeleeHitCooldowns -= MeleeCooldown;
        IL_Projectile.Damage -= ServerShortswordDamage;
        On_Projectile.NewProjectile_IEntitySource_float_float_float_float_int_int_float_int_float_float_float -= TrackShortsword;
        shortswordOwners.Clear();
        On_Player.ItemCheck_PlayInstruments -= PlayInstruments;
        IL_Player.Hurt_HurtInfo_bool -= LethalHurt;
        On_Player.UpdateDead -= UpdateDead;
    }

    // A client mirror and a slot claimed by a real connection must never simulate damage.
    internal static bool IsAuthoritativeActor(Player player) =>
        Main.netMode is NetmodeID.SinglePlayer or NetmodeID.Server &&
        player.active && player.whoAmI >= 0 && player.whoAmI < Main.maxPlayers &&
        ReferenceEquals(player, ModContent.GetInstance<DemoPlayerSystem>().Actor) &&
        ReferenceEquals(Main.player[player.whoAmI], player) &&
        (Main.netMode == NetmodeID.SinglePlayer || Netplay.Clients[player.whoAmI].State == 0);

    private static void UpdateDead(On_Player.orig_UpdateDead orig, Player player)
    {
        orig(player);
        // The native countdown runs for extra slots, but only the local slot calls Spawn.
        if (!IsAuthoritativeActor(player) || !player.dead || player.respawnTimer > 0 ||
            player.difficulty != PlayerDifficultyID.SoftCore)
            return;
        player.FindSpawn();
        if (!Player.CheckSpawn(player.SpawnX, player.SpawnY))
            player.SpawnX = player.SpawnY = -1;
        var system = ModContent.GetInstance<DemoPlayerSystem>();
        int generation = system.ActorGeneration;
        player.Spawn(PlayerSpawnContext.ReviveFromDeath);
        if (system.ActorGeneration == generation) system.RecordActorRespawn();
    }

    private enum OwnerRule { Supported, Melee, ServerSwing, Ranged, Any }

    private static bool IsSinglePlayerMelee(Player player) => Main.netMode == NetmodeID.SinglePlayer &&
        player.HeldItem.type is ItemID.WoodenSword or ItemID.IronBroadsword;

    // Representative server swing only. The field checks guard against a modded item merely
    // sharing the ID; the ID whitelist stays until the hitbox path is verified in game.
    private static bool IsServerSwing(Player player) => Main.netMode == NetmodeID.Server &&
        player.HeldItem.type is ItemID.IronBroadsword &&
        player.HeldItem.useStyle == ItemUseStyleID.Swing &&
        !player.HeldItem.noMelee && player.HeldItem.shoot <= ProjectileID.None;

    // The last chop can finish before the same swing reaches the native melee code.
    private static bool HasServerSwingPose(Player player, Item item) => Main.netMode == NetmodeID.Server &&
        (item.type == ItemID.IronBroadsword && IsServerSwing(player) || NativeMining.SupportedAxe(item));

    private static bool IsServerShortsword(Player player) => Main.netMode == NetmodeID.Server &&
        player.HeldItem.type == ItemID.CopperShortsword;

    private static void PlayInstruments(On_Player.orig_ItemCheck_PlayInstruments orig, Player player, Item item)
    {
        // The server-side swing enters owner-only code, where instrument input expects a local client.
        if (Main.dedServ && (IsServerSwing(player) || NativeMining.ActiveChop(player)) &&
            IsAuthoritativeActor(player)) return;
        orig(player, item);
    }

    private static bool AllowedByRule(Player player, OwnerRule rule) => rule switch
    {
        OwnerRule.Melee => IsSinglePlayerMelee(player) || IsServerSwing(player) ||
            NativeMining.Active(player),
        OwnerRule.ServerSwing => HasServerSwingPose(player, player.HeldItem),
        OwnerRule.Ranged => IsServerShortsword(player),
        OwnerRule.Supported => IsSinglePlayerMelee(player) || IsServerSwing(player) ||
            IsServerShortsword(player) || NativeMining.Active(player),
        _ => true
    };

    private static void ItemStart(ILContext il) => ExtendOwnerCheck(il, false, OwnerRule.Supported);
    private static void MeleeOwner(ILContext il) => ExtendOwnerCheck(il, false, OwnerRule.Melee);
    // The first hit sets a per-NPC cooldown. Let the authoritative server swing run its native countdown.
    private static void MeleeCooldown(ILContext il) => ExtendOwnerCheck(il, false, OwnerRule.ServerSwing);
    private static void ProjectileStart(ILContext il)
    {
        ExtendOwnerCheck(il, false, OwnerRule.Ranged);
        var cursor = new ILCursor(il);
        if (!cursor.TryGotoNext(MoveType.After, i => i.MatchCall<Main>("get_MouseWorld")))
            throw new InvalidOperationException($"TPA projectile aim missing in {il.Method.Name}; recheck the tML version.");
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.EmitDelegate<Func<Vector2, Player, Vector2>>((mouse, player) =>
            IsServerShortsword(player) && IsAuthoritativeActor(player)
                ? player.MountedCenter + new Vector2(64f * player.direction, 0f)
                : mouse);

        if (!cursor.TryGotoNext(MoveType.Before, i => i.MatchCall("Terraria.ModLoader.CombinedHooks", "ModifyShootStats")) ||
            !cursor.TryGotoPrev(MoveType.After, i => i.MatchCall<Vector2>(".ctor")) ||
            !il.Instrs[cursor.Index - 4].MatchLdloca(out int velocity))
            throw new InvalidOperationException($"TPA projectile velocity missing in {il.Method.Name}; recheck the tML version.");
        // Supply horizontal aim before the native shoot-stat hooks apply weapon modifiers.
        cursor.Emit(OpCodes.Ldloca, il.Body.Variables[velocity]);
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.EmitDelegate<SetShortswordVelocity>((ref Vector2 nativeVelocity, Player player) =>
        {
            if (IsServerShortsword(player) && IsAuthoritativeActor(player))
                nativeVelocity = new Vector2(player.direction * nativeVelocity.Length(), 0f);
        });
    }

    private delegate void SetShortswordVelocity(ref Vector2 velocity, Player player);

    private static int TrackShortsword(
        On_Projectile.orig_NewProjectile_IEntitySource_float_float_float_float_int_int_float_int_float_float_float orig,
        IEntitySource source, float x, float y, float speedX, float speedY, int type, int damage,
        float knockback, int owner, float ai0, float ai1, float ai2)
    {
        int index = orig(source, x, y, speedX, speedY, type, damage, knockback, owner, ai0, ai1, ai2);
        if (Main.netMode != NetmodeID.Server || index >= Main.maxProjectiles) return index;

        Projectile projectile = Main.projectile[index];
        shortswordOwners.Remove(projectile);
        if (type == ProjectileID.CopperShortswordStab &&
            source is EntitySource_ItemUse { Entity: Player player, Item.type: ItemID.CopperShortsword } &&
            projectile.owner == player.whoAmI && IsAuthoritativeActor(player))
        {
            shortswordOwners.Add(projectile, new ShortswordShot(player,
                ModContent.GetInstance<DemoPlayerSystem>().AttackSequence));
            // Vanilla only broadcasts creation for the local owner, so send the virtual player's stab explicitly.
            NetMessage.SendData(MessageID.SyncProjectile, number: index);
        }
        return index;
    }

    private static void ServerShortswordDamage(ILContext il)
    {
        var cursor = new ILCursor(il);
        // Match the NPC hit gate after the earlier projectile-versus-player and tile-cutting owner checks.
        if (!cursor.TryGotoNext(MoveType.After,
            i => i.MatchLdfld<Projectile>(nameof(Projectile.owner)),
            i => i.MatchLdsfld<Main>(nameof(Main.myPlayer)),
            i => i.MatchBneUn(out _), i => i.MatchLdcR4(1f)))
            throw new InvalidOperationException($"TPA projectile damage owner check missing in {il.Method.Name}; recheck the tML version.");
        cursor.Index -= 2;
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.EmitDelegate<Func<int, Projectile, int>>((local, projectile) =>
            Main.netMode == NetmodeID.Server && projectile.type == ProjectileID.CopperShortswordStab &&
            shortswordOwners.TryGetValue(projectile, out var shot) && shot.Owner is { dead: false, hostile: false } player &&
            projectile.owner == player.whoAmI && IsAuthoritativeActor(player) ? projectile.owner : local);
    }

    private static void LethalHurt(ILContext il) => ExtendOwnerCheck(il, true, OwnerRule.Any);

    private static void ServerSwingPose(ILContext il)
    {
        var cursor = new ILCursor(il);
        if (!cursor.TryGotoNext(MoveType.After, i => i.MatchLdsfld<Main>(nameof(Main.dedServ))))
            throw new InvalidOperationException($"TPA swing pose check missing in {il.Method.Name}; recheck the tML version.");
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.Emit(OpCodes.Ldarg_2);
        // The native melee hitbox is anchored at the item location computed by this routine.
        cursor.EmitDelegate<Func<bool, Player, Item, bool>>((dedicated, player, item) =>
            dedicated && !(HasServerSwingPose(player, item) && IsAuthoritativeActor(player)));
    }

    private static void ExtendOwnerCheck(ILContext il, bool last, OwnerRule rule)
    {
        var cursor = new ILCursor(il);
        bool found;
        if (last)
        {
            cursor.Index = il.Instrs.Count;
            found = cursor.TryGotoPrev(MoveType.After, i => i.MatchLdsfld<Main>(nameof(Main.myPlayer)));
        }
        else
            found = cursor.TryGotoNext(MoveType.After, i => i.MatchLdsfld<Main>(nameof(Main.myPlayer)));
        if (!found)
            throw new InvalidOperationException($"TPA combat owner check missing in {il.Method.Name}; recheck the tML version.");
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.EmitDelegate<Func<int, Player, int>>((owner, player) =>
            IsAuthoritativeActor(player) && AllowedByRule(player, rule) ? player.whoAmI : owner);
    }
}
