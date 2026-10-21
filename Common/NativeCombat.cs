using System;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerrariaPlayerAgent.Common;

// These three owner checks were inspected in tML 2026.08.3.0. Never change Main.myPlayer.
public sealed class NativeCombat : ModSystem
{
    public override void Load()
    {
        IL_Player.ItemCheck_Inner += ItemStart;
        IL_Player.ItemCheck_OwnerOnlyCode += MeleeOwner;
        IL_Player.Hurt_HurtInfo_bool += LethalHurt;
    }

    public override void Unload()
    {
        IL_Player.ItemCheck_Inner -= ItemStart;
        IL_Player.ItemCheck_OwnerOnlyCode -= MeleeOwner;
        IL_Player.Hurt_HurtInfo_bool -= LethalHurt;
    }

    private static bool IsActor(Player player) => Main.netMode == NetmodeID.SinglePlayer &&
        ReferenceEquals(player, ModContent.GetInstance<DemoPlayerSystem>().Actor);

    private static void ItemStart(ILContext il) => ExtendOwnerCheck(il, false, true);
    private static void MeleeOwner(ILContext il) => ExtendOwnerCheck(il, false, true);
    private static void LethalHurt(ILContext il) => ExtendOwnerCheck(il, true, false);

    private static void ExtendOwnerCheck(ILContext il, bool last, bool swordOnly)
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
            IsActor(player) && (!swordOnly || player.HeldItem.type is ItemID.WoodenSword or ItemID.IronBroadsword)
                ? player.whoAmI : owner);
    }
}
