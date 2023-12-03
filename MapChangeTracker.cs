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
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Server;

#endregion

namespace UltimaLive;

/*
 * Map Change Format
 * -----------------------
 * 2 bytes      Map Index
 * 2 bytes      X block number
 * 2 bytes      Y block number
 * 192 bytes    landblocks[land block count]
 *
 * Statics Change Format
 * 2 bytes      Map Index
 * 4 bytes      X block number
 * 4 bytes      Y block number
 * 4 bytes      Number of statics
 * 7 bytes      Static[Number of statics]
/**/
public class MapChangeTracker
{
    private static Hashtable[] m_LandChanges;
    private static Hashtable[] m_StaticsChanges;

    public static void Configure()
    {
        m_LandChanges = new Hashtable[256];
        m_StaticsChanges = new Hashtable[256];
        //foreach (KeyValuePair<int, MapRegistry.MapDefinition> kvp in MapRegistry.Definitions)
        for (var i = 0; i < 256; i++)
        {
            m_LandChanges[i] = new Hashtable();
            m_StaticsChanges[i] = new Hashtable();
        }

        EventSink.WorldLoad += OnLoad;
        EventSink.WorldSave += OnSave;
    }

    public static void MarkStaticsBlockForSave(int map, Point2D block)
    {
        if (!m_StaticsChanges[map].ContainsKey(block))
        {
            m_StaticsChanges[map].Add(block, null);
        }
    }

    public static void MarkLandBlockForSave(int map, Point2D block)
    {
        if (!m_LandChanges[map].ContainsKey(block))
        {
            m_LandChanges[map].Add(block, null);
        }
    }

    public static void OnLoad()
    {
        Console.WriteLine("Loading Ultima Live map changes");

        if (!Directory.Exists(UltimaLiveSettings.UltimaLiveMapChangesSavePath))
        {
            Directory.CreateDirectory(UltimaLiveSettings.UltimaLiveMapChangesSavePath);
        }

        var filePaths = Directory.GetFiles(UltimaLiveSettings.UltimaLiveMapChangesSavePath, "*.live");
        var staticsPaths = new List<string>();
        var landPaths = new List<string>();

        foreach (var s in filePaths)
        {
            if (s.Contains("map"))
            {
                landPaths.Add(s);
            }
            else if (s.Contains("statics"))
            {
                staticsPaths.Add(s);
            }
        }

        landPaths.Sort();

        //read map blocks and apply them in order
        foreach (var s in landPaths)
        {
            var reader = new BinaryReader(File.Open(Path.Combine(Core.BaseDirectory, s), FileMode.Open));
            try
            {
                reader.BaseStream.Seek(0, SeekOrigin.Begin);
                int MapNumber = reader.ReadUInt16();

                while (reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    int x = reader.ReadInt16();
                    int y = reader.ReadInt16();
                    var blocktiles = new LandTile[64];

                    for (var j = 0; j < 64; j++)
                    {
                        var id = reader.ReadInt16();
                        var z = reader.ReadSByte();
                        var lt = new LandTile(id, z);
                        blocktiles[j] = lt;
                    }

                    List<int> associated;
                    MapRegistry.MapAssociations.TryGetValue(MapNumber, out associated);
                    foreach (var integer in associated)
                    {
                        var map = Map.Maps[integer];
                        var tm = map.Tiles;
                        tm.SetLandBlock(x, y, blocktiles);
                    }
                }
            }
            catch
            {
                Console.WriteLine("An error occured reading land changes at " + reader.BaseStream.Position);
            }
            finally
            {
                reader.Close();
            }
        }


        staticsPaths.Sort();
        //read statics blocks and apply them in order
        foreach (var s in staticsPaths)
        {
            var mapFile = new FileInfo(Path.Combine(Core.BaseDirectory, s));
            var reader = new BinaryReader(File.Open(Path.Combine(Core.BaseDirectory, s), FileMode.Open));
            try
            {
                reader.BaseStream.Seek(0, SeekOrigin.Begin);
                int MapNumber = reader.ReadUInt16();

                while (reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    int blockX = reader.ReadInt16();
                    int blockY = reader.ReadInt16();
                    var staticCount = reader.ReadInt32();

                    var
                        blockStatics = new Dictionary<Point2D, List<StaticTile>>();

                    for (var staticIndex = 0; staticIndex < staticCount; staticIndex++)
                    {
                        var id = reader.ReadUInt16();
                        var x = reader.ReadByte();
                        var y = reader.ReadByte();
                        var z = reader.ReadSByte();
                        var hue = reader.ReadInt16();
                        var st = new StaticTile(id, x, y, z, hue);

                        var p = new Point2D(x, y);
                        if (!blockStatics.ContainsKey(p))
                        {
                            blockStatics.Add(p, new List<StaticTile>());
                        }

                        blockStatics[p].Add(st);
                    }

                    var newblockOfTiles = new StaticTile[8][][];

                    for (var i = 0; i < 8; i++)
                    {
                        newblockOfTiles[i] = new StaticTile[8][];
                        for (var j = 0; j < 8; j++)
                        {
                            var p = new Point2D(i, j);
                            var length = 0;
                            if (blockStatics.ContainsKey(p))
                            {
                                length = blockStatics[p].Count;
                            }

                            newblockOfTiles[i][j] = new StaticTile[length];
                            for (var k = 0; k < length; k++)
                            {
                                if (blockStatics.ContainsKey(p))
                                {
                                    newblockOfTiles[i][j][k] = blockStatics[p][k];
                                }
                            }
                        }
                    }

                    List<int> associated;
                    MapRegistry.MapAssociations.TryGetValue(MapNumber, out associated);
                    foreach (var integer in associated)
                    {
                        var map = Map.Maps[integer];
                        var tm = map.Tiles;
                        tm.SetStaticBlock(blockX, blockY, newblockOfTiles);
                    }
                }
            }
            catch
            {
                Console.WriteLine("An error occured reading land changes.");
            }
            finally
            {
                reader.Close();
            }
        }
    }

    public static void OnSave(WorldSaveEventArgs e)
    {
        if (!Directory.Exists(UltimaLiveSettings.UltimaLiveMapChangesSavePath))
        {
            Directory.CreateDirectory(UltimaLiveSettings.UltimaLiveMapChangesSavePath);
        }

        var now = DateTime.Now;
        var Stamp = string.Format(
            "{0}-{1}-{2}-{3}-{4}-{5}",
            now.Year,
            now.Month.ToString("00"),
            now.Day.ToString("00"),
            now.Hour.ToString("00"),
            now.Minute.ToString("00"),
            now.Second.ToString("00")
        );
        foreach (var kvp in MapRegistry.Definitions)
            //for (int mapIndex = 0; mapIndex < Live.NumberOfMapFiles; mapIndex++)
        {
            try
            {
                var CurrentMap = Map.Maps[kvp.Key];
                var CurrentMatrix = CurrentMap.Tiles;

                var keyColl = m_LandChanges[kvp.Key].Keys;
                if (keyColl.Count > 0)
                {
                    var filename = string.Format("map{0}-{1}.live", kvp.Key, Stamp);
                    Console.WriteLine(Path.Combine(UltimaLiveSettings.UltimaLiveMapChangesSavePath, filename));
                    GenericWriter writer =
                        new BinaryFileWriter(
                            Path.Combine(UltimaLiveSettings.UltimaLiveMapChangesSavePath, filename),
                            true
                        );
                    writer.Write((ushort)kvp.Key);

                    foreach (Point2D p in keyColl)
                    {
                        writer.Write((ushort)p.X);
                        writer.Write((ushort)p.Y);
                        var blocktiles = CurrentMatrix.GetLandBlock(p.X, p.Y);
                        for (var j = 0; j < 64; j++)
                        {
                            writer.Write((ushort)blocktiles[j].ID);
                            writer.Write((sbyte)blocktiles[j].Z);
                        }
                    }

                    writer.Close();
                }

                m_LandChanges[kvp.Key].Clear();

                keyColl = m_StaticsChanges[kvp.Key].Keys;
                if (keyColl.Count > 0)
                {
                    var filename = string.Format("statics{0}-{1}.live", kvp.Key, Stamp);
                    GenericWriter writer =
                        new BinaryFileWriter(
                            Path.Combine(UltimaLiveSettings.UltimaLiveMapChangesSavePath, filename),
                            true
                        );
                    writer.Write((ushort)kvp.Key);

                    foreach (Point2D p in keyColl)
                    {
                        var staticTiles = CurrentMatrix.GetStaticBlock(p.X, p.Y);

                        var staticCount = 0;
                        for (var i = 0; i < staticTiles.Length; i++)
                        {
                            for (var j = 0; j < staticTiles[i].Length; j++)
                            {
                                staticCount += staticTiles[i][j].Length;
                            }
                        }

                        writer.Write((ushort)p.X);
                        writer.Write((ushort)p.Y);
                        writer.Write(staticCount);

                        for (var i = 0; i < staticTiles.Length; i++)
                        {
                            for (var j = 0; j < staticTiles[i].Length; j++)
                            {
                                for (var k = 0; k < staticTiles[i][j].Length; k++)
                                {
                                    writer.Write((ushort)staticTiles[i][j][k].ID);
                                    writer.Write((byte)i);
                                    writer.Write((byte)j);
                                    writer.Write((sbyte)staticTiles[i][j][k].Z);
                                    writer.Write((short)staticTiles[i][j][k].Hue);
                                }
                            }
                        }
                    }

                    writer.Close();
                }

                m_StaticsChanges[kvp.Key].Clear();
            }
            catch
            {
                Console.WriteLine("Key: " + kvp.Key);
            }
        }
    }
}
