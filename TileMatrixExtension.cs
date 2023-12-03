namespace Server.Engines.UltimaLive;

public static class TileMatrixExtension
{
    public static StaticTile[] GetStaticTilesArray(this TileMatrix tm, int x, int y)
    {
        var tiles = tm.GetStaticBlock(x >> 3, y >> 3);

        return tiles[x & 0x7][y & 0x7];
    }
}
