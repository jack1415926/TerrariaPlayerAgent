using System;
using Terraria;

namespace TerrariaPlayerAgent.Common;

// Horizontal goals only: native movement/collision still owns the actual position.
internal sealed class GroundNavigation
{
    internal const float Tolerance = 8;
    internal bool Active { get; private set; }
    internal bool Bounded { get; private set; }
    internal float HomeX { get; private set; }
    private float minX, maxX, targetX, startX;
    private string status = "idle", reason = "";
    private int sequence, inputTicks;

    internal void SetRange(Player player, int tiles)
    {
        Clear();
        if (tiles == 0) return;
        Bounded = true;
        HomeX = player.position.X;
        minX = Math.Max(16, HomeX - tiles * 16);
        maxX = Math.Min((Main.maxTilesX - 1) * 16 - player.width, HomeX + tiles * 16);
    }

    internal string? Validate(Player player, float x)
    {
        if (!float.IsFinite(x) || x < 16 || x > (Main.maxTilesX - 1) * 16 - player.width)
            return "Target X must be a finite world pixel coordinate inside the world.";
        if (Bounded && (x < minX || x > maxX)) return "target_outside_activity_range";
        return null;
    }

    internal void Start(Player player, float x)
    {
        sequence++;
        targetX = x;
        startX = player.position.X;
        inputTicks = 0;
        Active = true;
        status = "moving";
        reason = "";
    }

    internal void Finish(string outcome, string detail = "")
    {
        Active = false;
        status = outcome;
        reason = detail;
    }

    internal void Cancel()
    {
        if (Active) Finish("cancelled", "input_cancelled");
    }

    internal void Clear()
    {
        Active = Bounded = false;
        HomeX = minX = maxX = targetX = startX = 0;
        status = "idle";
        reason = "";
        inputTicks = 0;
    }

    private static float BrakingDistance(Player player) =>
        player.velocity.X * player.velocity.X / (2 * Math.Max(0.05f, player.runSlowdown));

    internal int Direction(Player player)
    {
        float error = targetX - player.position.X;
        if (Math.Abs(error) <= Tolerance && Math.Abs(player.velocity.X) <= 0.15f && LocalTerrain.Grounded(player))
        {
            Finish("arrived");
            return 0;
        }
        // Release early so native friction stops the player instead of cutting velocity/teleporting.
        if (Math.Sign(error) == Math.Sign(player.velocity.X) && Math.Abs(error) <= BrakingDistance(player) + 4)
            return 0;
        int direction = Math.Sign(error);
        if (Math.Abs(error) <= 4 && Math.Abs(player.velocity.X) <= 0.15f) direction = 0;
        if (direction != 0) inputTicks++;
        return direction;
    }

    internal bool AtOutwardBoundary(Player player, int direction, bool targetMove)
    {
        if (!Bounded || direction == 0) return false;
        if (player.position.X < minX && direction > 0 || player.position.X > maxX && direction < 0) return false;
        // A goal already brakes toward its validated target. Timed input needs an earlier fence.
        float margin = targetMove ? 1 : (player.velocity.X * direction > 0 ? BrakingDistance(player) : 0) + 4;
        return direction < 0 && player.position.X - minX <= margin ||
            direction > 0 && maxX - player.position.X <= margin;
    }

    internal object Snapshot(Player player) => new
    {
        active = Active, bounded = Bounded, homeX = Bounded ? (float?)HomeX : null,
        minX = Bounded ? (float?)minX : null, maxX = Bounded ? (float?)maxX : null,
        targetX = status != "idle" ? (float?)targetX : null, startX, sequence, status, reason,
        inputTicks, tolerancePixels = Tolerance,
        outsideBounds = Bounded && (player.position.X < minX || player.position.X > maxX)
    };
}
