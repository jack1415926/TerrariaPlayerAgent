using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace TerrariaPlayerAgent.Common;

internal static class LocalTerrain
{
    internal static bool Grounded(Player player) => player.gravDir == 1 && player.velocity.Y == 0 &&
        Collision.SolidCollision(new Vector2(player.position.X, player.Bottom.Y), player.width, 2, true);

    internal static bool HeadClear(Player player) => !Collision.SolidCollision(
        player.position - new Vector2(0, 16), player.width, player.height + 16);

    internal static bool Blocked(Player player, int direction) => Collision.SolidCollision(
        player.position + new Vector2(direction * 16, 0), player.width, player.height);

    internal static bool CanJumpObstacle(Player player, int direction)
    {
        if (!Blocked(player, direction) || !HeadClear(player)) return false;
        Vector2 ahead = player.position + new Vector2(direction * 16, 0);
        for (int rise = 16; rise <= 80; rise += 16)
            if (!Collision.SolidCollision(ahead - new Vector2(0, rise), player.width, player.height) &&
                !Collision.SolidCollision(player.position - new Vector2(0, rise), player.width, player.height + rise))
                return true;
        return false;
    }

    internal static object Observe(Player player)
    {
        int centerX = (int)(player.Center.X / 16), footY = (int)(player.Bottom.Y / 16);
        int originX = centerX - 8, originY = footY - 6;
        var rows = new string[11];
        for (int row = 0; row < rows.Length; row++)
        {
            var cells = new char[17];
            for (int column = 0; column < cells.Length; column++)
            {
                int x = originX + column, y = originY + row;
                char symbol = '?';
                if (x >= 0 && y >= 0 && x < Main.maxTilesX && y < Main.maxTilesY)
                {
                    Tile tile = Main.tile[x, y];
                    symbol = tile.LiquidAmount > 0 ? '~' : '.';
                    if (tile.HasTile && !tile.IsActuated)
                    {
                        if (Main.tileSolidTop[tile.TileType]) symbol = '=';
                        else if (Main.tileSolid[tile.TileType])
                            symbol = tile.IsHalfBlock ? '_' : (byte)tile.Slope != 0 ? '/' : '#';
                    }
                }
                cells[column] = symbol;
            }
            rows[row] = new string(cells);
        }
        object Side(int direction) => new
        {
            blocked = Blocked(player, direction),
            jumpClearance = CanJumpObstacle(player, direction),
            groundWithin4Tiles = Collision.SolidCollision(new Vector2(
                player.position.X + direction * 32, player.Bottom.Y), player.width, 64, true)
        };
        return new
        {
            grounded = Grounded(player), headClear = HeadClear(player),
            left = Side(-1), right = Side(1),
            originTileX = originX, originTileY = originY, actorTileX = centerX, actorFootTileY = footY,
            tileSize = 16, legend = ". air/nonblocking, # solid, / slope, _ half-block, = platform, ~ liquid (type unknown), ? outside world",
            rows
        };
    }
}
