using System.Buffers;
using System.Collections.Generic;
using Server;
using Server.Network;

namespace UltimaLive.Network;

public static class OutgoingUltimaLivePackets
{
    /* Update Statics Packet
     *
     *
     * Original Packet:
     * ------------------------------
     * Source: http://necrotoolz.sourceforge.net/kairpacketguide/packet3f.htm
     *
     * 0x3f      Command
     * ushort    Size of packet
     * uint      Block Number
     * uint      Number of statics
     * uint      Extra value for the index file
     *
     * Static[number of statics]    7 bytes
     *       ushort      ArtID
     *       byte        X offset in the block
     *       byte        Y offset in the block
     *       sbyte       Z axis position of the static
     *       ushort      Hue Number
     *
     *
     *
     * New Format:
     * ------------------------------
     * byte         0x3f
     * ushort       size of the packet
     * int          block_number
     * uint         number of statics
     * uint         extra value that we're splitting
     *              byte  hash
     *              byte  UltimaLive Command
     *              ushort  mapnumber - if this is 0xFF it means its a query
     * struct
     * [number of statics]
     *              ushort      ItemId
     *              byte        X           // Not actually stored in runuo
     *              byte        Y           // Not actually stored in runuo
     *              sbyte       Z
     *              ushort      Hue         //no longer used
     *
     *
     * We're going to use this as a dual purpose packet. The extra field
     * will tell us if the packet should actually be used as a hash. The
     * extra field is split into two padding bytes (0x00),
     * and an unsigned short that we're using as our map number.
     *
     * If unsigned short representing the map has a maxvalue for a ushort
     * (0xffff), then we'll know its a packet that we're using to request
     * a set of 25 CRCs from the client.
    /**/
    public static void SendUpdateStatics(this NetState ns, Point2D blockCoords, Mobile m)
    {
        var playerMap = m.Map;
        var tm = playerMap.Tiles;

        var blockNum = blockCoords.X * tm.BlockHeight + blockCoords.Y;
        var staticsData = BlockUtility.GetRawStaticsData(blockCoords, playerMap.MapID);
        ns.SendUpdateStatics(staticsData, blockNum, playerMap.MapID);
    }

    public static void SendUpdateStatics(this NetState ns, byte[] staticsData, int blockNumber, int mapID)
    {
        if (ns.CannotSendPackets())
        {
            return;
        }

        var writer = new SpanWriter(stackalloc byte[15 + staticsData.Length]);
        writer.Write((byte)0x3F);                      // Packet ID
        writer.Write((ushort)(15 + staticsData.Length)); // Length
        writer.Write((uint)blockNumber);
        writer.Write(staticsData.Length / 7);   // Number of statics in packet
        writer.Write((ushort)0x0000);           // UltimaLive sequence number
        writer.Write((byte)0x00);               // UltimaLive command 'Statics Update'
        writer.Write((byte)mapID);              // UltimaLive mapnumber
        writer.Write(staticsData);

        ns.Send(writer.Span);
    }

    public static void SendQueryClientHash(this NetState ns, Mobile m)
    {
        if (ns.CannotSendPackets())
        {
            return;
        }

        var playerMap = m.Map;
        var tm = playerMap.Tiles;
        var blocknum = (m.Location.X >> 3) * tm.BlockHeight + (m.Location.Y >> 3);

        var writer = new SpanWriter(stackalloc byte[15]);
        writer.Write((byte)0x3F);            // Packet ID
        writer.Write((ushort)15);             // Length
        writer.Write((uint)blocknum);        // Central block number for the query (block that player is standing in)
        writer.Write(0);                     // Number of statics in packet, not used
        writer.Write((ushort)0x0000);        // UltimaLive sequence number
        writer.Write((byte)0xFF);            // UltimaLive command 'Block Query'
        writer.Write((byte)playerMap.MapID); // UltimaLive mapnumber
        ns.Send(writer.Span);
    }

    public static void SendMapDefinitions(this NetState ns)
    {
        if (ns.CannotSendPackets())
        {
            return;
        }

        var definitions = MapRegistry.Definitions.Values;
        var length = definitions.Count * 9; //12 * 9 = 108
        var count = length / 7;             //108 / 7 = 15
        var padding = 0;
        if (length - count * 7 > 0)
        {
            count++;
            padding = count * 7 - length;
        }

        var writer = new SpanWriter(stackalloc byte[15 + length + padding]);
        writer.Write((byte)0x3F);                    // Packet ID
        writer.Write((ushort)(15 + length + padding)); // Length
        writer.Write((uint)0x00);                    // block number, not used
        writer.Write(count);                         // size of map definitions
        writer.Write((ushort)0x00);                  // UltimaLive sequence number, not used
        writer.Write((byte)0x01);                    // UltimaLive command 'Update Map Definitions'
        writer.Write((byte)0x00);                    // UltimaLive map number, not used

        foreach (var md in definitions)
        {
            writer.Write((byte)md.FileIndex);                // Map file index number
            writer.Write((ushort)md.Dimensions.X);           // Map width
            writer.Write((ushort)md.Dimensions.Y);           // Map height
            writer.Write((ushort)md.WrapAroundDimensions.X); // Wrap around dimension X
            writer.Write((ushort)md.WrapAroundDimensions.Y); // Wrap around dimension Y
        }

        for (var i = 0; i < padding; i++)
        {
            writer.Write((byte)0x00); // Padding
        }
        ns.Send(writer.Span);
    }

    public static void SendLoginComplete(this NetState ns)
    {
        if (ns.CannotSendPackets())
        {
            return;
        }

        var writer = new SpanWriter(stackalloc byte[43]);
        writer.Write((byte)0x3F);           // Packet ID
        writer.Write((ushort)43);              // Length
        writer.Write((uint)0x01);     // Block number, doesn't really apply in this case
        writer.Write((uint)4);        // 4 * 7 bytes, 28 characters
        writer.Write((ushort)0x0000); // UltimaLive sequence number, not used
        writer.Write((byte)0x02);     // UltimaLive command 'Login Confirmation'
        writer.Write((byte)0x00);     // UltimaLive mapnumber, not used
        if (UltimaLiveSettings.UNIQUE_SHARD_IDENTIFIER.Length < 28)
        {
            writer.WriteAscii(UltimaLiveSettings.UNIQUE_SHARD_IDENTIFIER, UltimaLiveSettings.UNIQUE_SHARD_IDENTIFIER.Length);
            var remainingLength = 28 - UltimaLiveSettings.UNIQUE_SHARD_IDENTIFIER.Length;
            for (var i = 0; i < remainingLength; ++i)
            {
                writer.Write((byte)0x00);
            }
        }
        else
        {
            writer.WriteAscii(UltimaLiveSettings.UNIQUE_SHARD_IDENTIFIER.Substring(0, 28), 28);
        }
        ns.Send(writer.Span);
    }

    public static void SendRefreshClientView(this NetState ns)
    {
        if (ns.CannotSendPackets())
        {
            return;
        }

        var writer = new SpanWriter(stackalloc byte[15]);
        writer.Write((byte)0x3F);     // Packet ID
        writer.Write((ushort)15);     // Length
        writer.Write((uint)0);        // not used
        writer.Write(0);              // not used
        writer.Write((ushort)0x0000); // UltimaLive sequence number
        writer.Write((byte)0x03);     // UltimaLive command 'Refresh Client'
        writer.Write((byte)0);        // UltimaLive mapnumber
        ns.Send(writer.Span);
    }

    /* Update Terrain Packet
     * Source: http://necrotoolz.sourceforge.net/kairpacketguide/packet40.htm
     *
     * Format:
     *
     * byte         0x40
     * uint         block number
     *
     * struct[64]   maptiles
     *              ushort      Tile Number
     *              sbyte       Z
     * uint         map grid header -> we're splitting this into two ushorts
     *      ushort  padding
     *      ushort  mapnumber
     *
     *
     *
    /**/
    public static void SendUpdateTerrain(this NetState ns, Point2D blockCoords, Mobile m)
    {
        var playerMap = m.Map;
        var tm = playerMap.Tiles;
        var blockNumber = blockCoords.X * tm.BlockHeight + blockCoords.Y;
        var landData = BlockUtility.GetLandData(blockCoords, playerMap.MapID);
        ns.SendUpdateTerrain(landData, blockNumber, playerMap.MapID);
    }

    public static void SendUpdateTerrain(this NetState ns, byte[] landData, int blockNumber, int mapID)
    {
        if (ns.CannotSendPackets())
        {
            return;
        }

        var writer = new SpanWriter(stackalloc byte[201]);
        writer.Write((byte)0x40);                                 // Packet ID
        writer.Write(blockNumber);
        writer.Write(landData);
        writer.Write((byte)0x00);  // padding
        writer.Write((byte)0x00);  // padding
        writer.Write((byte)0x00);  // padding
        writer.Write((byte)mapID);
        ns.Send(writer.Span);
    }

    public struct TargetObject
    {
        public int ItemID;
        public int Hue;
        public int xOffset;
        public int yOffset;
        public int zOffset;
    }

    /* Target Object List Packet
     * Thank you -hash- from RunUO.com for providing the definition for this
     */
    public static void SendTargetObjectList(this NetState ns, List<TargetObject> targetObjects, Mobile m, bool allowGround)
    {
        if (ns.CannotSendPackets())
        {
            return;
        }

        byte allowGroundByte = 0;
        if (allowGround)
        {
            allowGroundByte = 0x1;
        }

        var packetSize = 16 + targetObjects.Count * 10;

        var writer = new SpanWriter(stackalloc byte[201]);
        writer.Write((byte)0xB4);                  // Packet ID
        writer.Write((ushort)201);                 // Length
        writer.Write(allowGroundByte);             // Allow Ground
        writer.Write(m.Serial);                    // target serial
        writer.Write((ushort)0);                   // x
        writer.Write((ushort)0);                   // y
        writer.Write((ushort)0);                   // z
        writer.Write((ushort)targetObjects.Count); // Number of Entries
        foreach (var t in targetObjects)
        {
            writer.Write((ushort)t.ItemID);
            writer.Write((ushort)t.Hue);
            writer.Write((ushort)t.xOffset);
            writer.Write((ushort)t.yOffset);
            writer.Write((ushort)t.zOffset);
        }
        ns.Send(writer.Span);
    }
}
