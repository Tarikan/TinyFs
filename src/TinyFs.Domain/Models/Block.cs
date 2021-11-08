using System.Runtime.InteropServices;

namespace TinyFs.Domain.Models
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Block
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = FileSystemSettings.BlockSize)]
        public byte[] Data;
    }
}