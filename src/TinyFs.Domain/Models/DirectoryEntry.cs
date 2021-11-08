using System.Runtime.InteropServices;

namespace TinyFs.Domain.Models
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DirectoryEntry
    {
        [MarshalAs(UnmanagedType.LPStr, SizeConst = 13)]
        public string Name;

        public bool IsValid;

        public ushort FileDescriptorId;
    }
}