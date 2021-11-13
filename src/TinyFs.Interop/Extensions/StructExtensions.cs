using System;
using System.Runtime.InteropServices;

namespace TinyFs.Interop.Extensions
{
    public static class StructExtensions
    {
        public static byte[] ToByteArray<T>(this T structure)
            where T : struct
        {
            int len = Marshal.SizeOf(structure);

            byte [] arr = new byte[len];

            IntPtr ptr = Marshal.AllocHGlobal(len);

            Marshal.StructureToPtr(structure, ptr, false);

            Marshal.Copy(ptr, arr, 0, len);

            Marshal.FreeHGlobal(ptr);

            return arr;
        }
    }
}