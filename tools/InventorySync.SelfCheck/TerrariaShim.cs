using System;
using System.Collections.Generic;

// Test-only stand-ins for the Terraria members StartingInventory touches. The real signatures were
// checked against the tModLoader 2026.08.3.0 assembly; this shim only makes the file replayable offline.
namespace Terraria
{
    internal sealed class Item
    {
        public int type;
        public int stack;
        public int prefix;

        public bool IsAir => type == 0;
        public string Name => type switch
        {
            0 => "",
            603 => "Carrot",
            3507 => "Copper Shortsword",
            3509 => "Copper Pickaxe",
            3506 => "Copper Axe",
            _ => "Item " + type
        };

        public void SetDefaults(int newType)
        {
            type = newType;
            stack = newType == 0 ? 0 : 1;
            prefix = 0;
        }

        public bool Prefix(int newPrefix)
        {
            ModLoader.PlayerLoader.PrefixCalls.Add((type, newPrefix));
            prefix = newPrefix;
            return true;
        }
    }

    internal sealed class Player
    {
        public Item[] inventory;
        public int selectedItem;

        public Player()
        {
            inventory = new Item[50];
            for (int i = 0; i < inventory.Length; i++) inventory[i] = new Item();
        }

        public Item HeldItem => inventory[selectedItem];
    }
}

namespace Terraria.ModLoader
{
    internal static class PlayerLoader
    {
        internal static Func<Player, IEnumerable<Item>, bool, List<Item>>? GetStartingItemsHook;
        internal static Action<Player, IList<Item>>? SetStartInventoryHook;
        internal static readonly List<(int Type, int Prefix)> PrefixCalls = new();
        internal static int GetStartingItemsCalls;
        internal static int SetStartInventoryCalls;

        public static List<Item> GetStartingItems(Player player, IEnumerable<Item> vanillaItems, bool mediumCoreDeath)
        {
            GetStartingItemsCalls++;
            return GetStartingItemsHook?.Invoke(player, vanillaItems, mediumCoreDeath) ?? new List<Item>(vanillaItems);
        }

        public static void SetStartInventory(Player player, IList<Item> items)
        {
            SetStartInventoryCalls++;
            if (SetStartInventoryHook != null)
            {
                SetStartInventoryHook(player, items);
                return;
            }
            for (int i = 0; i < player.inventory.Length; i++)
                player.inventory[i] = i < items.Count ? items[i] : new Item();
        }
    }
}
