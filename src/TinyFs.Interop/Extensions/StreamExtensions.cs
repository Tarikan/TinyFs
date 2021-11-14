using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace TinyFs.Interop.Extensions
{
    public static class StreamExtensions
    {
        public static T ReadStruct<T>(this Stream stream, int offset) where T : struct
        {
            stream.Position = offset;
            var sz = Marshal.SizeOf(typeof(T));
#if DEBUG
            Console.WriteLine($"Reading {sz} bytes from offset {offset}");
#endif
            var buffer = new byte[sz];
            stream.Read(buffer, 0, sz);
            var pinnedBuffer = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            var structure = (T)Marshal.PtrToStructure(
                pinnedBuffer.AddrOfPinnedObject(), typeof(T));
            pinnedBuffer.Free();
            return structure;
        }

        public static ICollection<T> ReadStructs<T>(this Stream stream, int offset, int count) where T : struct
        {
            var result = new List<T>(count);
            var sz = Marshal.SizeOf(typeof(T));
            for (var i = 0; i < count; i++)
            {
                result.Add(stream.ReadStruct<T>(offset + i * sz));
            }

            return result;
        }

        public static byte[] ReadBytes(this Stream stream, int count, int offset)
        {
#if DEBUG
            Console.WriteLine($"Reading {count} bytes from offset {offset}");
#endif
            var result = new byte[count];
            stream.Position = offset;
            stream.Read(result, 0, count);
            return result;
        }

        public static void WriteObject<T>(this Stream stream, T obj, int offset) where T : struct
        {
            var byteArr = obj.ToByteArray();
            stream.WriteBytes(byteArr, offset);
        }

        public static void WriteBytes(this Stream stream, byte[] bytes, int offset)
        {
#if DEBUG
            if (offset == 2624)
            {
                Console.WriteLine();
            }
            Console.WriteLine($"Write {bytes.Length} bytes to offset {offset}");
#endif
            stream.Position = offset;
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}