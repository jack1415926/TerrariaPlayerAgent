using System;
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
    public override void Load()
    {
        IL_Player.ItemCheck_Inner += ItemStart;
        IL_Player.ItemCheck_OwnerOnlyCode += MeleeOwner;
        IL_Player.ItemCheck_Shoot += ProjectileStart;
        IL_Player.Hurt_HurtInfo_bool += LethalHurt;
        On_Player.UpdateDead += UpdateDead;
    }

    public override void Unload()
    {
        IL_Player.ItemCheck_Inner -= ItemStart;
        IL_Player.ItemCheck_OwnerOnlyCode -= MeleeOwner;
        IL_Player.ItemCheck_Shoot -= ProjectileStart;
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

    private enum OwnerRule { Supported, Melee, Ranged, Any }

    private static bool IsSinglePlayerMelee(Player player) => Main.netMode == NetmodeID.SinglePlayer &&
        player.HeldItem.type is ItemID.WoodenSword or ItemID.IronBroadsword;

    private static bool IsServerShortsword(Player player) => Main.netMode == NetmodeID.Server &&
        player.HeldItem.type == ItemID.CopperShortsword;

    private static bool AllowedByRule(Player player, OwnerRule rule) => rule switch
    {
        OwnerRule.Melee => IsSinglePlayerMelee(player) || NativeMining.Active(player),
        OwnerRule.Ranged => IsServerShortsword(player),
        OwnerRule.Supported => IsSinglePlayerMelee(player) || IsServerShortsword(player) ||
            NativeMining.Active(player),
        _ => true
    };

    private static void ItemStart(ILContext il) => ExtendOwnerCheck(il, false, OwnerRule.Supported);
    private static void MeleeOwner(ILContext il) => ExtendOwnerCheck(il, false, OwnerRule.Melee);
    private static void ProjectileStart(ILContext il) => ExtendOwnerCheck(il, false, OwnerRule.Ranged);
    private static void LethalHurt(ILContext il) => ExtendOwnerCheck(il, true, OwnerRule.Any);

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
