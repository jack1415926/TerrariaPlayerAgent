namespace TerrariaPlayerAgent.Common;

// Bounded manual multiplayer attack input. Kept Terraria-free so the offline self-check can run it.
internal static class MultiplayerAttackPolicy
{
    internal const int MaxSeconds = 1;
    internal const int IronBroadsword = 4;

    internal static bool IsSupportedAttackItem(int heldItemType) =>
        heldItemType is StartingKit.CopperShortsword or IronBroadsword;

    internal static bool TryParseCommand(string[] args, out string action, out int seconds)
    {
        action = "";
        seconds = 0;
        if (args.Length is < 2 or > 3 || args[0] != "attack" || args[1] is not ("left" or "right"))
            return false;
        if (args.Length == 3 &&
            (!int.TryParse(args[2], out seconds) || seconds is < 1 or > MaxSeconds))
            return false;
        if (args.Length == 2) seconds = MaxSeconds;
        action = args[1] == "left" ? "attack_left" : "attack_right";
        return true;
    }

    internal static bool TryDirection(string action, out int direction)
    {
        direction = action switch { "attack_left" => -1, "attack_right" => 1, _ => 0 };
        return direction != 0;
    }

    internal static string? Validate(bool controllerValid, bool controllerMatches, bool actorAlive,
        bool authoritative, int heldItemType, int direction, int seconds)
    {
        if (!controllerValid || !controllerMatches)
            return "Only the current AI controller can issue an attack.";
        if (!actorAlive)
            return "No living AI. Spawn one or wait for automatic respawn.";
        if (!authoritative)
            return "The AI slot or identity changed; attack rejected.";
        if (!IsSupportedAttackItem(heldItemType))
            return "Only Copper Shortsword or Iron Broadsword attacks are supported in this batch.";
        if (direction is not (-1 or 1) || seconds is < 1 or > MaxSeconds)
            return "Use /tpamp attack left/right [1 second].";
        return null;
    }
}
