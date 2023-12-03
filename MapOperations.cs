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
using System.Collections.Generic;
using Server;
using Server.Engines.UltimaLive;
using UltimaLive.Network;
using Server.Targeting;

#endregion

namespace UltimaLive;

[Flags]
public enum LocalUpdateFlags
{
    None = 0,
    Terrain = 1,
    Statics = 2
}

public interface IMapOperation
{
    void DoOperation();
    void DoOperation(Dictionary<int, LocalUpdateFlags> blockUpdateChain);

    int BlockNumber { get; }

    int MapNumber { get; }

    Point2D Location { get; }
}

public class MapOperationSeries : BaseMapOperation
{
    private readonly List<IMapOperation> m_Changes;

    public override int BlockNumber => -1;

    public override int MapNumber => m_MapNumber;

    public MapOperationSeries(IMapOperation startingChange, int mapNumber)
        : base(-1, -1, mapNumber)
    {
        m_Changes = new List<IMapOperation>();
        m_Changes.Add(startingChange);
    }

    public override void DoOperation(Dictionary<int, LocalUpdateFlags> blockUpdateChain)
    {
        var sendoutUpdates = false;

        if (blockUpdateChain == null)
        {
            blockUpdateChain = new Dictionary<int, LocalUpdateFlags>();
            sendoutUpdates = true;
        }

        foreach (var mc in m_Changes)
        {
            mc.DoOperation(blockUpdateChain);
        }

        if (sendoutUpdates)
        {
            foreach (var kvp in blockUpdateChain)
            {
                if (kvp.Key >= 0)
                {
                    SendOutLocalUpdates(m_MapNumber, kvp.Key, kvp.Value);
                }
            }
        }
    }

    public void Add(IMapOperation mc)
    {
        m_Changes.Add(mc);
    }

    public void Remove(IMapOperation mc)
    {
        m_Changes.Remove(mc);
    }
}

public class BaseMapOperation : IMapOperation
{
    #region Static Methods

    public static void SendOutLocalUpdates(int mapNum, int blockNumber, LocalUpdateFlags flags)
    {
        var map = Map.Maps[mapNum];
        var tm = map.Tiles;
        var x = blockNumber / tm.BlockHeight * 8 + 4;
        var y = blockNumber % tm.BlockHeight * 8 + 4;
        SendOutLocalUpdates(map, x, y, flags);
    }

    public static void SendOutLocalUpdates(Map map, int x, int y, LocalUpdateFlags flags)
    {
        var candidates = new List<Mobile>();

        foreach (Mobile m in map.GetMobilesInRange(new Point3D(x, y, 0)))
        {
            if (m.Player)
            {
                candidates.Add(m);
            }
        }

        foreach (var m in candidates)
        {
            if ((flags & LocalUpdateFlags.Terrain) == LocalUpdateFlags.Terrain)
            {
                m.NetState.SendUpdateTerrain(new Point2D(x >> 3, y >> 3), m);
            }

            if ((flags & LocalUpdateFlags.Statics) == LocalUpdateFlags.Statics)
            {
                m.NetState.SendUpdateStatics(new Point2D(x >> 3, y >> 3), m);
            }
            //m.Send(new RefreshClientView());
        }
    }

    #endregion

    protected int m_BlockNumber;
    protected int m_MapNumber;
    protected Point2D m_Location;
    protected TileMatrix m_Matrix;
    protected Map m_Map;

    #region IMapOperation

    public Point2D Location => m_Location;

    public virtual int BlockNumber => m_BlockNumber;

    public virtual int MapNumber => m_MapNumber;

    public virtual void DoOperation()
    {
        DoOperation(null);
    }

    public virtual void DoOperation(Dictionary<int, LocalUpdateFlags> blockUpdateChain)
    {
        CRC.InvalidateBlockCRC(m_MapNumber, m_BlockNumber);
    }

    #endregion

    public BaseMapOperation(int x, int y, int map)
    {
        m_MapNumber = map;
        if (map >= 0 && x >= 0 && y >= 0)
        {
            m_Map = Map.Maps[map];
            m_Matrix = m_Map.Tiles;
            m_BlockNumber = (x >> 3) * m_Matrix.BlockHeight + (y >> 3);
            m_Location = new Point2D(x, y);
        }
    }
}

#region Static Operations

public class StaticOperation : BaseMapOperation
{
    protected StaticTile[][][] m_Block;

    public StaticOperation(int x, int y, int mapNum)
        : base(x, y, mapNum)
    {
        m_Block = m_Matrix.GetStaticBlock(x >> 3, y >> 3);

        // If the block we retrieved is the "m_EmptyStaticBlock" from the
        // TileMatrix, then we need to create a new blank block and change
        // our m_Block to reference the new blank block.
        if (m_Block == m_Matrix.GetStaticBlock(-1, -1))
        {
            var tempBlock = new StaticTile[8][][];

            for (var i = 0; i < 8; ++i)
            {
                tempBlock[i] = new StaticTile[8][];

                for (var j = 0; j < 8; ++j)
                {
                    tempBlock[i][j] = new StaticTile[0];
                }
            }

            //public void SetStaticBlock( int x, int y, StaticTile[][][] value )
            m_Matrix.SetStaticBlock(x >> 3, y >> 3, tempBlock);
            m_Block = m_Matrix.GetStaticBlock(x >> 3, y >> 3);
        }
    }

    public override void DoOperation(Dictionary<int, LocalUpdateFlags> blockUpdateChain)
    {
        //invalidate CRC
        base.DoOperation(blockUpdateChain);

        //mark block for saving statics
        MapChangeTracker.MarkStaticsBlockForSave(m_Map.MapID, new Point2D(m_Location.X >> 3, m_Location.Y >> 3));

        if (blockUpdateChain == null)
        {
            SendOutLocalUpdates(m_Map, m_Location.X, m_Location.Y, LocalUpdateFlags.Statics);
        }
        else
        {
            if (blockUpdateChain.ContainsKey(m_BlockNumber))
            {
                blockUpdateChain[m_BlockNumber] = blockUpdateChain[m_BlockNumber] | LocalUpdateFlags.Statics;
            }
            else
            {
                blockUpdateChain.Add(m_BlockNumber, LocalUpdateFlags.Statics);
            }
        }
    }
}

public class ExistingStaticOperation : StaticOperation
{
    protected StaticTarget m_StaticTarget;

    protected StaticTile[] getExistingTiles() => m_Matrix.GetStaticTilesArray(m_Location.X, m_Location.Y);

    protected int lookupExistingStatic(ref StaticTile existingTile)
    {
        var tileIndex = -1;

        if (m_StaticTarget != null)
        {
            var z = m_StaticTarget.Z - TileData.ItemTable[m_StaticTarget.ItemID].CalcHeight;
            var staticTiles = m_Matrix.GetStaticTilesArray(m_Location.X, m_Location.Y);

            for (var i = 0; i < staticTiles.Length; i++)
            {
                if (staticTiles[i].Z == z && staticTiles[i].ID == m_StaticTarget.ItemID)
                {
                    tileIndex = i;
                }
            }

            if (tileIndex >= 0)
            {
                existingTile = staticTiles[tileIndex];
            }
        }

        return tileIndex;
    }

    public ExistingStaticOperation(int mapNum, StaticTarget targ)
        : base(targ.X, targ.Y, mapNum) =>
        m_StaticTarget = targ;

    public override void DoOperation(Dictionary<int, LocalUpdateFlags> blockUpdateChain)
    {
        base.DoOperation(blockUpdateChain);
    }
}

#region Static X/Y movement by Elm

public class IncStaticX : MoveStatic
{
    public IncStaticX(int mapNum, StaticTarget targ, int xChange)
        : base(mapNum, targ, targ.X + xChange, targ.Y)
    {
    }
}

public class IncStaticY : MoveStatic
{
    public IncStaticY(int mapNum, StaticTarget targ, int yChange)
        : base(mapNum, targ, targ.X, targ.Y + yChange)
    {
    }
}

#endregion

public class AddStatic : StaticOperation
{
    protected int m_NewID;
    protected int m_NewAltitude;
    protected int m_Hue;

    public AddStatic(int mapNumber, int newID, int newAltitude, int x, int y, int hue)
        : base(x, y, mapNumber)
    {
        m_NewID = newID;
        m_NewAltitude = newAltitude;
        m_Hue = hue;
    }

    public override void DoOperation(Dictionary<int, LocalUpdateFlags> blockUpdateChain)
    {
        var newTileList = new StaticTile[m_Block[Location.X & 0x7][Location.Y & 0x7].Length + 1];
        Array.Copy(
            m_Block[Location.X & 0x7][Location.Y & 0x7],
            newTileList,
            m_Block[Location.X & 0x7][Location.Y & 0x7].Length
        );
        newTileList[newTileList.Length - 1] = new StaticTile((ushort)m_NewID, (sbyte)m_NewAltitude);
        newTileList[newTileList.Length - 1].Hue = m_Hue;
        m_Block[Location.X & 0x7][Location.Y & 0x7] = newTileList;
        base.DoOperation(blockUpdateChain);
    }
}

public class IncStaticAltitude : ExistingStaticOperation
{
    protected int m_Change;

    public IncStaticAltitude(int mapNum, StaticTarget targ, int altitudeChange)
        : base(mapNum, targ) =>
        m_Change = altitudeChange;

    public override void DoOperation(Dictionary<int, LocalUpdateFlags> blockUpdateChain)
    {
        var tiles = getExistingTiles();

        if (tiles == null || tiles.Length == 0)
        {
            return;
        }

        var existingTile = new StaticTile();
        var idx = lookupExistingStatic(ref existingTile);

        if (idx >= 0)
        {
            tiles[idx].Set((ushort)existingTile.ID, (sbyte)(m_Change + existingTile.Z));
        }

        base.DoOperation(blockUpdateChain);
    }
}

public class SetStaticAltitude : ExistingStaticOperation
{
    protected int m_NewAltitude;

    public SetStaticAltitude(int mapNum, StaticTarget targ, int newAltitude)
        : base(mapNum, targ) =>
        m_NewAltitude = newAltitude;

    public override void DoOperation(Dictionary<int, LocalUpdateFlags> blockUpdateChain)
    {
        var tiles = getExistingTiles();

        if (tiles == null || tiles.Length == 0)
        {
            return;
        }

        var existingTile = new StaticTile();
        var idx = lookupExistingStatic(ref existingTile);

        if (idx >= 0)
        {
            tiles[idx]
                .Set(
                    (ushort)existingTile.ID,
                    (sbyte)(m_NewAltitude - TileData.ItemTable[existingTile.ID].CalcHeight)
                );
        }

        base.DoOperation(blockUpdateChain);
    }
}

public class SetStaticID : ExistingStaticOperation
{
    protected int m_NewID;

    public SetStaticID(int mapNum, StaticTarget targ, int newID)
        : base(mapNum, targ) =>
        m_NewID = newID;

    public override void DoOperation(Dictionary<int, LocalUpdateFlags> blockUpdateChain)
    {
        var tiles = getExistingTiles();

        if (tiles == null || tiles.Length == 0)
        {
            return;
        }

        var existingTile = new StaticTile();
        var idx = lookupExistingStatic(ref existingTile);

        if (idx >= 0)
        {
            tiles[idx]
                .Set(
                    (ushort)m_NewID,
                    (sbyte)(existingTile.Z - TileData.ItemTable[existingTile.ID].CalcHeight)
                );
        }

        base.DoOperation(blockUpdateChain);
    }
}

public class SetStaticHue : ExistingStaticOperation
{
    protected int m_NewHue;

    public SetStaticHue(int mapNum, StaticTarget targ, int newHue)
        : base(mapNum, targ) =>
        m_NewHue = newHue;

    public override void DoOperation(Dictionary<int, LocalUpdateFlags> blockUpdateChain)
    {
        var tiles = getExistingTiles();
        var existingTile = new StaticTile();
        var idx = lookupExistingStatic(ref existingTile);

        if (idx >= 0)
        {
            tiles[idx].Hue = m_NewHue;
            base.DoOperation(blockUpdateChain);
        }
    }
}

public class MoveStatic : ExistingStaticOperation
{
    protected int m_destinationX;
    protected int m_destinationY;

    public MoveStatic(int mapNum, StaticTarget targetOfStaticToMove, int destinationX, int destinationY)
        : base(mapNum, targetOfStaticToMove)
    {
        m_destinationX = destinationX;
        m_destinationY = destinationY;
    }

    public override void DoOperation(Dictionary<int, LocalUpdateFlags> blockUpdateChain)
    {
        var existingTile = new StaticTile();
        var idx = lookupExistingStatic(ref existingTile);

        if (idx >= 0)
        {
            var addStatic = new AddStatic(
                m_MapNumber,
                existingTile.ID,
                (sbyte)existingTile.Z,
                m_destinationX,
                m_destinationY,
                existingTile.Hue
            );
            var delStatic = new DeleteStatic(m_MapNumber, m_StaticTarget);

            var moveSeries = new MapOperationSeries(addStatic, m_MapNumber);
            moveSeries.Add(delStatic);
            moveSeries.DoOperation(blockUpdateChain);
        }
    }
}

public class DeleteStatic : ExistingStaticOperation
{
    public DeleteStatic(int mapNum, StaticTarget targ)
        : base(mapNum, targ)
    {
    }

    public override void DoOperation(Dictionary<int, LocalUpdateFlags> blockUpdateChain)
    {
        var tiles = getExistingTiles();

        if (tiles == null || tiles.Length == 0)
        {
            return;
        }

        var existingTile = new StaticTile();
        var idx = lookupExistingStatic(ref existingTile);

        if (idx >= 0 && idx < tiles.Length)
        {
            var newTileList = new List<StaticTile>(tiles);

            newTileList.RemoveAt(idx);

            //reassign the array
            m_Block[Location.X & 0x7][Location.Y & 0x7] = newTileList.ToArray();

            base.DoOperation(blockUpdateChain);
        }
    }
}

#endregion

#region Land Operations

public class LandOperation : BaseMapOperation
{
    protected LandTile[] m_Block;
    protected short m_OldID;
    protected sbyte m_OldZ;
    protected int m_TileIndex;

    public LandOperation(int x, int y, int mapNum)
        : base(x, y, mapNum)
    {
        m_Block = m_Matrix.GetLandBlock(x >> 3, y >> 3);
        m_OldID = (short)m_Block[((y & 0x7) << 3) + (x & 0x7)].ID;
        m_OldZ = (sbyte)m_Block[((y & 0x7) << 3) + (x & 0x7)].Z;
        m_TileIndex = ((m_Location.Y & 0x7) << 3) + (m_Location.X & 0x7);
    }

    public override void DoOperation(Dictionary<int, LocalUpdateFlags> blockUpdateChain)
    {
        base.DoOperation(blockUpdateChain);
        MapChangeTracker.MarkLandBlockForSave(m_Map.MapID, new Point2D(m_Location.X >> 3, m_Location.Y >> 3));


        if (blockUpdateChain == null)
        {
            SendOutLocalUpdates(m_Map, m_Location.X, m_Location.Y, LocalUpdateFlags.Terrain);
        }
        else
        {
            if (blockUpdateChain.ContainsKey(m_BlockNumber))
            {
                blockUpdateChain[m_BlockNumber] = blockUpdateChain[m_BlockNumber] | LocalUpdateFlags.Terrain;
            }
            else
            {
                blockUpdateChain.Add(m_BlockNumber, LocalUpdateFlags.Terrain);
            }
        }
    }
}

public class IncLandAltitude : LandOperation
{
    protected int m_Change;

    public IncLandAltitude(int x, int y, int mapNum, int change)
        : base(x, y, mapNum) =>
        m_Change = change;

    public override void DoOperation(Dictionary<int, LocalUpdateFlags> blockUpdateChain)
    {
        m_Block[m_TileIndex].Set(m_OldID, (sbyte)(m_Change + m_OldZ));
        base.DoOperation(blockUpdateChain);
    }
}

public class SetLandID : LandOperation
{
    protected int m_NewID;

    public SetLandID(int x, int y, int mapNum, int newID)
        : base(x, y, mapNum) =>
        m_NewID = newID;

    public override void DoOperation(Dictionary<int, LocalUpdateFlags> blockUpdateChain)
    {
        m_Block[m_TileIndex].Set((short)m_NewID, m_OldZ);
        base.DoOperation(blockUpdateChain);
    }
}

public class SetLandAltitude : LandOperation
{
    protected int m_Altitude;

    public SetLandAltitude(int x, int y, int mapNum, int altitude)
        : base(x, y, mapNum) =>
        m_Altitude = altitude;

    public override void DoOperation(Dictionary<int, LocalUpdateFlags> blockUpdateChain)
    {
        m_Block[m_TileIndex].Set(m_OldID, (sbyte)m_Altitude);
        base.DoOperation(blockUpdateChain);
    }
}

#endregion
