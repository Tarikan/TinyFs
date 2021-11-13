using System.Runtime.InteropServices;
using TinyFs.Domain.Enums;

namespace TinyFs.Domain.Models
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FileDescriptor
    {
        public ushort Id; // 2

        [MarshalAs(UnmanagedType.U1)]
        public FileDescriptorType FileDescriptorType;  // 1

        public ushort FileSize; // 2

        public byte References; // 1

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = FileSystemSettings.DefaultBlocksInDescriptor)]
        public ushort[] Blocks; // 8

        public ushort MapIndex; // 2
    }
}