using System.IO;
using System.Runtime.InteropServices;

namespace ConsoleApp1
{
    public static class StreamExtensions
    {
        public static T ReadStruct<T>(this Stream stream, int offset) where T : struct
        {
            stream.Position = 0;
            var sz = Marshal.SizeOf(typeof(T));
            var buffer = new byte[sz];
            stream.Read(buffer, offset, sz);
            var pinnedBuffer = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            var structure = (T) Marshal.PtrToStructure(
                pinnedBuffer.AddrOfPinnedObject(), typeof(T));
            pinnedBuffer.Free();
            return structure;
        }
    }
}