using System.Runtime.InteropServices;
using TinyFs.Domain.Models;

namespace TinyFs.Interop.Helpers
{
    public static class SizeHelper
    {
        private static int? _blocksOffset = null;

        public static int GetStructureSize<T>(T structure) where T : struct
        {
            return Marshal.SizeOf(structure);
        }

        public static int GetStructureSize<T>() where T : struct
        {
            return Marshal.SizeOf(typeof(T));
        }

        public static int GetBlocksOffset(int descriptorsCount)
        {
            if (_blocksOffset.HasValue)
            {
                return _blocksOffset.Value;
            }

            var offset = FileSystemSettings.DescriptorsOffset + descriptorsCount * GetStructureSize<FileDescriptor>();

            _blocksOffset = offset;
            return offset;
        }
    }
}