/* Copyright(c) 2016 UltimaLive
 *
 * Permission is hereby granted, free of charge, to any person obtaining
 * a copy of this software and associated documentation files (the
 * "Software"), to deal in the Software without restriction, including
 * without limitation the rights to use, copy, modify, merge, publish,
 * distribute, sublicense, and/or sell copies of the Software, and to
 * permit persons to whom the Software is furnished to do so, subject to
 * the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
 * IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
 * CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
 * TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
 * SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

#region References

using System;
using Server;

#endregion

namespace UltimaLive;

internal class BlockUtility
{
    public static byte[] GetLandData(int blockNumber, int mapNumber)
    {
        var m = Map.Maps[mapNumber];
        var tm = m.Tiles;
        return GetLandData(new Point2D(blockNumber / tm.BlockHeight, blockNumber % tm.BlockHeight), mapNumber);
    }

    public static byte[] GetLandData(Point2D blockCoordinates, int mapNumber)
    {
        var landData = new byte[192];

        var map = Map.Maps[mapNumber];
        var tm = map.Tiles;
        var land = tm.GetLandBlock(blockCoordinates.X, blockCoordinates.Y);
        for (var i = 0; i < land.Length; i++) //64 * 3 = 192 bytes
        {
            landData[i * 3] = (byte)((ushort)land[i].ID & 0x00FF);
            landData[i * 3 + 1] = (byte)(((ushort)land[i].ID & 0xFF00) >> 8);
            landData[i * 3 + 2] = (byte)land[i].Z;
        }

        return landData;
    }

    public static void WriteLandDataToConsole(byte[] landData)
    {
        var outString = "Land Data:\n";
        for (var i = 0; i < landData.Length; i += 3)
        {
            if (i % 12 == 0 && i != 0)
            {
                outString += "\n";
            }

            outString += string.Format(
                "[{0},{1},{2}]",
                landData[i].ToString("X2"),
                landData[i + 1].ToString("X2"),
                landData[i + 2].ToString("X2")
            );
        }

        Console.WriteLine(outString);
    }

    public static void WriteStaticsDataToConsole(byte[] staticsData)
    {
        var outString = string.Format("Statics Data ({0}):\n", staticsData.Length);
        for (var i = 0; i < staticsData.Length; i += 7)
        {
            if (i % 14 == 0 && i != 0)
            {
                outString += "\n";
            }

            outString += string.Format(
                "[{0},{1},{2},{3},{4},{5},{6}]",
                staticsData[i].ToString("X2"),
                staticsData[i + 1].ToString("X2"),
                staticsData[i + 2].ToString("X2"),
                staticsData[i + 3].ToString("X2"),
                staticsData[i + 4].ToString("X2"),
                staticsData[i + 5].ToString("X2"),
                staticsData[i + 6].ToString("X2")
            );
        }

        Console.WriteLine(outString);
    }

    public static void WriteDataToConsole(byte[] anyData)
    {
        var outString = string.Format("Data ({0}):\n", anyData.Length);
        for (var i = 0; i < anyData.Length; i += 8)
        {
            if (i + 7 < anyData.Length)
            {
                if (i % 16 == 0 && i != 0)
                {
                    outString += "\n";
                }

                outString += string.Format(
                    "[{0},{1},{2},{3},{4},{5},{6},{7}]",
                    anyData[i].ToString("X2"),
                    anyData[i + 1].ToString("X2"),
                    anyData[i + 2].ToString("X2"),
                    anyData[i + 3].ToString("X2"),
                    anyData[i + 4].ToString("X2"),
                    anyData[i + 5].ToString("X2"),
                    anyData[i + 6].ToString("X2"),
                    anyData[i + 7].ToString("X2")
                );
            }
            else
            {
                outString += "\n[";
                for (var j = i; j < anyData.Length; j++)
                {
                    outString += anyData[j].ToString("X2") + ",";
                }

                outString += "]";
            }
        }

        Console.WriteLine(outString);
    }

    public static void WriteBlockDataToConsole(byte[] anyData)
    {
        Console.WriteLine("Block Data ({0}):\n", anyData.Length);
        if (anyData.Length >= 192)
        {
            var landData = new byte[192];
            Array.Copy(anyData, 0, landData, 0, 192);
            //WriteLandDataToConsole(landData);
            if ((anyData.Length - 192) % 7 == 0)
            {
                var staticsData = new byte[anyData.Length - 192];
                Array.Copy(anyData, 192, staticsData, 0, anyData.Length - 192);
                WriteStaticsDataToConsole(staticsData);
            }
        }
    }

    public static byte[] GetRawStaticsData(int blockNumber, int mapNumber)
    {
        var m = Map.Maps[mapNumber];
        var tm = m.Tiles;
        return GetRawStaticsData(
            new Point2D(blockNumber / tm.BlockHeight, blockNumber % tm.BlockHeight),
            mapNumber
        );
    }

    public static byte[] GetRawStaticsData(Point2D blockCoordinates, int mapNumber)
    {
        var map = Map.Maps[mapNumber];
        var tm = map.Tiles;
        var staticTiles = tm.GetStaticBlock(blockCoordinates.X, blockCoordinates.Y);

        var staticCount = 0;
        for (var k = 0; k < staticTiles.Length; k++)
        {
            for (var l = 0; l < staticTiles[k].Length; l++)
            {
                staticCount += staticTiles[k][l].Length;
            }
        }

        var blockBytes = new byte[staticCount * 7];
        var blockByteIdx = 0;
        for (var i = 0; i < staticTiles.Length; i++)
        {
            for (var j = 0; j < staticTiles[i].Length; j++)
            {
                var sortedTiles = staticTiles[i][j];
                //Array.Sort(sortedTiles, CompareStaticTiles);

                for (var k = 0; k < sortedTiles.Length; k++)
                {
                    blockBytes[blockByteIdx + 0] = (byte)((ushort)sortedTiles[k].ID & 0x00FF);
                    blockBytes[blockByteIdx + 1] = (byte)(((ushort)sortedTiles[k].ID & 0xFF00) >> 8);
                    blockBytes[blockByteIdx + 2] = (byte)i;
                    blockBytes[blockByteIdx + 3] = (byte)j;
                    blockBytes[blockByteIdx + 4] = (byte)sortedTiles[k].Z;
                    blockBytes[blockByteIdx + 5] = (byte)((ushort)sortedTiles[k].Hue & 0x00FF);
                    blockBytes[blockByteIdx + 6] = (byte)(((ushort)sortedTiles[k].Hue & 0xFF00) >> 8);
                    blockByteIdx += 7;
                }
            }
        }

        return blockBytes;
    }

    public static int CompareStaticTiles(StaticTile b, StaticTile a)
    {
        var retVal = a.Z.CompareTo(b.Z);
        if (retVal == 0) //same Z, lower z has higher priority now, it's correct this way, tested locally
        {
            Map.StaticTileEnumerable sts = ClientFileExport.WorkMap.Tiles.GetStaticTiles(a.X, a.Y);
            foreach (var staticTile in sts)
            {
                //we compare hashcodes for easyness, instead of comparing a bunch of properties, order has been verified to work in exportclientfiles.
                var hash = staticTile.GetHashCode();
                if (hash == a.GetHashCode())
                {
                    retVal = 1;
                    break;
                }

                if (hash == b.GetHashCode())
                {
                    retVal = -1;
                    break;
                }
            }
        }

        //We leave this as is, but it shouldn't happen anyway if we have same Z
        if (retVal == 0)
        {
            retVal = a.ID.CompareTo(b.ID);
        }

        if (retVal == 0)
        {
            retVal = a.Hue.CompareTo(b.Hue);
        }

        return retVal;
    }
}
