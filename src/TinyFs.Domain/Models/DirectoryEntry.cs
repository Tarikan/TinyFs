using System.Runtime.InteropServices;

namespace TinyFs.Domain.Models
{
    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct DirectoryEntry
    {
        [FieldOffset(0)]
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 13)]
        public string Name;

        [FieldOffset(13)]
        [MarshalAs(UnmanagedType.Bool)]
        public bool IsValid;

        [FieldOffset(14)]
        [MarshalAs(UnmanagedType.U2)]
        public ushort FileDescriptorId;
    }
}