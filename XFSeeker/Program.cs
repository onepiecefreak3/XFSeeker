﻿using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using static System.Linq.Enumerable;

namespace XFSeeker
{
    class Program
    {
        static void Main(string[] args)
        {
            var filename = args.DefaultIfEmpty("chr001.fsm").Last();
            var root = (FSM.rAIFSM)ReadXFS(File.OpenRead(filename));

            // Do something arbitrary, such as printing out all conditions
            foreach (var cond in root.mpConditionTree.mpTreeList)
                Console.WriteLine($"[{cond.mName.mId}] {cond.mpRootNode}");
        }

        static object ReadXFS(Stream stream)
        {
            // Get all types in assembly and translate them to hashes
            var dicTypes = Assembly.GetExecutingAssembly().GetTypes().ToDictionary(
                t => new BitArray(t.FullName.Split('.').Last().Replace("+", "::").Select(x => (byte)x).ToArray())
                .Cast<bool>().Aggregate(~0u, (h, i) => h / 2 ^ (i ^ h % 2 != 0 ? 0xEDB88320 : 0)) * 2 / 2);

            using (var br = new BinaryReader(stream))
            {
                br.ReadBytes(16);
                int count = br.ReadInt32();
                int infoSize = br.ReadInt32();
                var offsets = Range(0, count).Select(_ => br.ReadInt32() + 0x18).ToList();

                var structs = (from offset in offsets
                               let type = dicTypes[br.ReadUInt32()]
                               let members = (from _ in Range(0, br.ReadInt32())
                                              let fieldInfo = type.GetField(ReadStringAt(br.ReadInt32() + 0x18))
                                              let typeNo = br.ReadInt16()
                                              let typeLength = br.ReadInt16() // ignore length
                                              let bytes = br.ReadBytes(32) // ignore bytes
                                              select (fieldInfo, typeNo))
                                              .ToList()
                               select (type, members))
                               .ToList();

                br.BaseStream.Position = infoSize + 0x18;
                var retval = ReadClass();
                if (br.BaseStream.Position != br.BaseStream.Length) throw new Exception("Unused bytes at end of stream.");
                return retval;

                #region BinaryReader Helpers
                // helpers for reading strings
                string ReadString() => string.Concat(Range(0, 999).Select(_ => (char)br.ReadByte()).TakeWhile(c => c != 0));
                string ReadStringAt(int offset)
                {
                    var tmp = stream.Position;
                    stream.Position = offset;
                    var str = ReadString();
                    stream.Position = tmp;
                    return str;
                }

                // helper for reading a struct or class
                object ReadClass()
                {
                    var (structId, objectId) = (br.ReadInt16(), br.ReadInt16());
                    if (structId == -2) return default;
                    if (structId % 2 == 0) throw new Exception($"Error parsing structId = {structId}");

                    var (size, str) = (br.ReadInt32(), structs[structId / 2]);
                    var item = Activator.CreateInstance(str.type);

                    foreach (var (fieldInfo, type) in str.members)
                    {
                        int lstCount = br.ReadInt32();
                        if (type < 0) // probably means it has to be a list
                        {
                            var lst = (IList)Activator.CreateInstance(fieldInfo.FieldType);
                            for (int i = 0; i < lstCount; i++)
                                lst.Add(ReadObject(type));
                            fieldInfo.SetValue(item, lst);
                        }
                        else if (lstCount == 1)
                            fieldInfo.SetValue(item, ReadObject(type));
                        else
                            throw new Exception($"Error parsing type = {type}");
                    }
                    return item;
                }

                // helper for reading an object by type
                object ReadObject(int type)
                {
                    switch ((byte)type)
                    {
                        case 1: // fallthrough: ReadStruct() == ReadClass()
                        case 2: return ReadClass();
                        case 3: return br.ReadByte() != 0;
                        case 4: return br.ReadByte();
                        case 6: return br.ReadUInt32();
                        case 10: return br.ReadInt32();
                        case 12: return br.ReadSingle();
                        case 14: return ReadString();
                        case 20: return (br.ReadInt32(), br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
                        default: throw new NotSupportedException($"Unknown type {type}");
                    }
                }
                #endregion
            }
        }
    }
}