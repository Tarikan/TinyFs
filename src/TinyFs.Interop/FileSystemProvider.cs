using System;
using System.Collections;
using System.IO;
using System.Linq;
using TinyFs.Domain.Enums;
using TinyFs.Domain.Models;
using TinyFs.Interop.Extensions;
using TinyFs.Interop.Helpers;

namespace TinyFs.Interop
{
    public class FileSystemProvider : IFileSystemProvider
    {
        public IFileSystemInterop CreateNewFileSystem(string fsName, ushort descriptorsCount)
        {
            if (descriptorsCount >= FileSystemSettings.NullDescriptor)
            {
                throw new Exception("Maximum descriptor count exceeded");
            }

            var file = File.Open(fsName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
            file.Flush();

            file.WriteObject(descriptorsCount, 0);
            var bitArray = new BitArray(
                FileSystemSettings.BlocksCount,
                FileSystemSettings.BitmaskFreeBit);
            var buffer = new byte[OpHelper.DivWithRoundUp(FileSystemSettings.BlocksCount, 8)];
            bitArray.CopyTo(buffer, 0);
            file.WriteBytes(buffer, FileSystemSettings.BitMapOffset);

            byte zero = 0;

            byte[] descriptorsSpace =
                Enumerable.Repeat(zero, SizeHelper.GetStructureSize<FileDescriptor>() * descriptorsCount).ToArray();
            file.WriteBytes(descriptorsSpace, FileSystemSettings.DescriptorsOffset);

            // var root = new FileDescriptor
            // {
            //     Id = 0,
            //     FileDescriptorType = FileDescriptorType.Directory,
            //     FileSize = 0,
            //     References = 1,
            //     Blocks = new ushort[]
            //     {
            //         FileSystemSettings.NullDescriptor,
            //         FileSystemSettings.NullDescriptor,
            //         FileSystemSettings.NullDescriptor,
            //         FileSystemSettings.NullDescriptor,
            //     },
            //     MapIndex = 0
            // };
            //
            // file.WriteObject(root, FileSystemSettings.DescriptorsOffset);

            return new FileSystem(file);
        }

        public IFileSystemInterop MountExistingFileSystem(string fsName)
        {
            var file = File.Open(fsName, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

            return new FileSystem(file);
        }
    }
}