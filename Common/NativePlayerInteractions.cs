using System;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerrariaPlayerAgent.Common;

public sealed class NativePlayerInteractions : ModSystem
{
    public override void Load()
    {
        IL_Player.UpdateBuffs += TickBuffs;
        IL_Player.Update += RemoveExpiredBuffs;
        IL_Player.GrabItems += GrabItems;
        On_Player.PickupItem += PickupItem;
    }

    public override void Unload()
    {
        IL_Player.UpdateBuffs -= TickBuffs;
        IL_Player.Update -= RemoveExpiredBuffs;
        IL_Player.GrabItems -= GrabItems;
        On_Player.PickupItem -= PickupItem;
    }

    private static bool IsServerActor(Player player) => Main.netMode == NetmodeID.Server &&
        !player.dead && NativeCombat.IsAuthoritativeActor(player);

    private static void TickBuffs(ILContext il)
    {
        var cursor = new ILCursor(il);
        if (!cursor.TryGotoNext(MoveType.Before,
            i => i.MatchLdsfld<Main>(nameof(Main.myPlayer)),
            i => i.MatchBneUn(out _),
            i => i.MatchLdsfld(typeof(BuffID.Sets).FullName!, nameof(BuffID.Sets.TimeLeftDoesNotDecrease))))
            throw new InvalidOperationException("TPA could not locate the native buff timer owner check.");
        cursor.Index++;
        AllowServerActor(cursor);
    }

    private static void RemoveExpiredBuffs(ILContext il)
    {
        var cursor = new ILCursor(il);
        if (!cursor.TryGotoNext(MoveType.Before,
            i => i.MatchStfld<Player>(nameof(Player.forceWerewolf)),
            i => i.MatchLdarg(0), i => i.MatchLdfld<Entity>(nameof(Entity.whoAmI)),
            i => i.MatchLdsfld<Main>(nameof(Main.myPlayer)), i => i.MatchBneUn(out _)))
            throw new InvalidOperationException("TPA could not locate the expired buff owner check.");
        cursor.Index += 4;
        AllowServerActor(cursor);
    }

    private static void GrabItems(ILContext il)
    {
        var cursor = new ILCursor(il);
        if (!cursor.TryGotoNext(MoveType.After, i => i.MatchLdsfld<Main>(nameof(Main.myPlayer))))
            throw new InvalidOperationException("TPA could not locate the native pickup owner check.");
        AllowServerActor(cursor);

        // OnPickup can consume an item without calling PickupItem, so mirror that removal too.
        int itemIndex = -1;
        if (!cursor.TryGotoNext(MoveType.After, i => i.MatchLdsfld<Main>(nameof(Main.item)),
            i => i.MatchLdloc(out itemIndex), i => i.MatchNewobj<Item>(), i => i.MatchStelemRef()))
            throw new InvalidOperationException("TPA could not locate pickup removal after the item hook.");
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.Emit(OpCodes.Ldloc, il.Body.Variables[itemIndex]);
        cursor.EmitDelegate<Action<Player, int>>(SyncPickup);
    }

    private static void AllowServerActor(ILCursor cursor)
    {
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.EmitDelegate<Func<int, Player, int>>((owner, player) =>
            IsServerActor(player) ? player.whoAmI : owner);
    }

    private static Item PickupItem(On_Player.orig_PickupItem orig, Player player, int slot, int itemIndex, Item item)
    {
        int stack = item.stack;
        Item remainder = orig(player, slot, itemIndex, item);
        if (!ReferenceEquals(item, remainder) || stack != remainder.stack)
            SyncPickup(player, itemIndex);
        return remainder;
    }

    private static void SyncPickup(Player player, int itemIndex)
    {
        if (IsServerActor(player)) NetMessage.SendData(MessageID.SyncItem, number: itemIndex);
    }
}
