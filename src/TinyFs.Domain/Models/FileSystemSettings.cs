namespace TinyFs.Domain.Models
{
    public class FileSystemSettings
    {
        public const int DescriptorsCountOffset = 0;

        public const int BitMapOffset = 2;

        public const int DescriptorsOffset = BitMapOffset + BlocksCount / 8;

        public const int BlockSize = 512; // Bytes

        // 1022 * 8
        public const int BlocksCount = 8176;

        public const int MaxSize = BlockSize * BlocksCount;

        public const bool BitmaskFreeBit = false;

        public const int RefsInFileMap = BlockSize / 2 - 1;

        public const int DefaultBlocksInDescriptor = 4;

        public const ushort DefaultBlocksSize = DefaultBlocksInDescriptor * BlockSize;

        // for truncate optimization
        public const ushort NullDescriptor = ushort.MaxValue;

        public const int MaxSymlinkInOneLookup = 255;

        public const string Separator = "/";

        public const string CurrentDirSymlink = ".";
        
        public const string PrevDirSymlink = "..";
    }
}