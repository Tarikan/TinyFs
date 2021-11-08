using System.Runtime.InteropServices;

namespace TinyFs.Domain.Models
{
    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct FileMap
    {
        [FieldOffset(0)]
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = FileSystemSettings.RefsInFileMap)]
        public ushort[] Indexes;

        [FieldOffset(510)]
        public ushort NextMapIndex;
    }
}