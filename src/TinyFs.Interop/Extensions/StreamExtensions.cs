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
            var buffer = new byte[sz];
            stream.Read(buffer, 0, sz);
            var pinnedBuffer = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            var structure = (T) Marshal.PtrToStructure(
                pinnedBuffer.AddrOfPinnedObject(), typeof(T));
            pinnedBuffer.Free();
            return structure;
        }

        public static ICollection<T> ReadStructs<T>(this Stream stream, int offset, int count)  where T : struct
        {
            var result = new List<T>();
            var sz = Marshal.SizeOf(typeof(T));
            for (var i = 0; i < count; i++)
            {
                result.Add(stream.ReadStruct<T>(offset + i * sz));
            }

            return result;
        }
    }
}