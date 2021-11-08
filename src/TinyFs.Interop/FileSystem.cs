using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TinyFs.Domain.Enums;
using TinyFs.Domain.Models;
using TinyFs.Interop.Extensions;
using TinyFs.Interop.Helpers;

namespace TinyFs.Interop
{
    public class FileSystem : FileSystemBase
    {
        private readonly int _descriptorsCount;

        private readonly int _fsSize;

        // Active blocks
        private readonly int _totalBlocks;
        private FileDescriptor Root => GetFileDescriptor(0);
        private Block RootBlock => GetBlock(0);
        private readonly List<ushort> _openFilesDescriptorsIds = new List<ushort>();

        public FileSystem(FileStream fsFileStream, int fsSize) : base(fsFileStream)
        {
            _fsSize = fsSize;
            _totalBlocks = fsSize / FileSystemSettings.BlockSize;
            _descriptorsCount = fsFileStream.ReadStruct<short>(FileSystemSettings.DescriptorsCountOffset);
        }

        public override Block GetBlock(int index)
        {
            return FsFileStream.ReadStruct<Block>(SizeHelper.GetBlocksOffset(_descriptorsCount) * index);
        }

        public override void SetBlock(int index, Block block)
        {
            var offset = SizeHelper.GetBlocksOffset(_descriptorsCount) * index;
            WriteObject(block, offset);
        }

        public override FileMap GetFileMap(int index)
        {
            return FsFileStream.ReadStruct<FileMap>(SizeHelper.GetBlocksOffset(_descriptorsCount) * index);
        }

        public override void SetFileMap(int index, FileMap fileMap)
        {
            var offset = SizeHelper.GetBlocksOffset(_descriptorsCount) * index;
            WriteObject(fileMap, offset);
        }

        public override FileDescriptor GetFileDescriptor(ushort id)
        {
            var offset = FileSystemSettings.DescriptorsOffset + SizeHelper.GetStructureSize<FileDescriptor>() * id;
            return FsFileStream.ReadStruct<FileDescriptor>(offset);
        }

        public override void SetFileDescriptor(ushort id, FileDescriptor fileDescriptor)
        {
            var offset = FileSystemSettings.DescriptorsOffset + SizeHelper.GetStructureSize<FileDescriptor>() * id;
            WriteObject(fileDescriptor, offset);
        }

        public override BitArray GetBitmask(int offset, int bytes = 16)
        {
            var byteArray = ReadBytes(bytes, offset + FileSystemSettings.BitMapOffset);
            return new BitArray(byteArray);
        }

        public override void SetBitmask(int offset, BitArray bitArray)
        {
            var buff = new byte[OpHelper.DivWithRoundUp(bitArray.Count, 8)];
            bitArray.CopyTo(buff, 0);
            WriteBytes(buff, offset + FileSystemSettings.BitMapOffset);
        }

        public override void SetBitFree(int index)
        {
            var offset = index / 8;
            var arr = GetBitmask(offset);
            arr[index % 8] = FileSystemSettings.BitmaskFreeBit;
            SetBitmask(offset, arr);
        }

        public override void UnsetBitFree(int index)
        {
            var offset = index / 8;
            var arr = GetBitmask(offset);
            arr[index % 8] = !FileSystemSettings.BitmaskFreeBit;
            SetBitmask(offset, arr);
        }

        public override void CreateFile(byte[] file, string filename)
        {
            var descriptorId = GetFirstFreeDescriptor();
            var descriptor = new FileDescriptor
            {
                Id = descriptorId,
            };

            descriptor.References = 1;
            descriptor.FileSize = Convert.ToUInt16(file.Length);
            descriptor.FileDescriptorType = FileDescriptorType.File;

            var fileSizeInBlocks = OpHelper.DivWithRoundUp(file.Length, FileSystemSettings.BlockSize);
            var mapsNeeded = OpHelper.DivWithRoundUp(fileSizeInBlocks - FileSystemSettings.DefaultBlocksInDescriptor,
                FileSystemSettings.RefsInFileMap);

            var blockIds = new List<ushort>();
            for (var i = 0; i < fileSizeInBlocks; i++)
            {
                blockIds.Add(GetFirstFreeBlockAndReserve());
            }

            for (var i = 0; i < FileSystemSettings.DefaultBlocksInDescriptor; i++)
            {
                descriptor.Blocks[i] = blockIds[i];
            }

            if (mapsNeeded > 0)
            {
                var mapsIds = new List<ushort>();
                for (var i = 0; i < mapsNeeded; i++)
                {
                    mapsIds.Add(GetFirstFreeBlockAndReserve());
                }

                descriptor.MapIndex = mapsIds[0];

                for (var i = 0; i < mapsNeeded; i++)
                {
                    var indexesToAdd = blockIds
                        .Skip(FileSystemSettings.DefaultBlocksInDescriptor + i * FileSystemSettings.RefsInFileMap)
                        .Take(FileSystemSettings.RefsInFileMap)
                        .ToArray();
                    ushort nextMapIndex = 0;
                    if (mapsIds.Count > i)
                    {
                        nextMapIndex = mapsIds[i + 1];
                    }

                    var map = new FileMap
                    {
                        Indexes = indexesToAdd,
                        NextMapIndex = nextMapIndex,
                    };
                    SetFileMap(mapsIds[i], map);
                }
            }

            SetFileDescriptor(descriptorId, descriptor);
            AddLinkToDirectory(Root, filename, descriptorId);
        }

        public override byte[] ReadFile(string filename)
        {
            var descriptorId = FileLookUp(filename);
            var descriptor = GetFileDescriptor(descriptorId);
            var result = new byte[descriptor.FileSize];
            var blocksCount = OpHelper.DivWithRoundUp(descriptor.FileSize, FileSystemSettings.BlockSize);
            if (descriptor.FileSize == 0)
            {
                return result;
            }

            for (var i = 0; i < FileSystemSettings.DefaultBlocksInDescriptor; i++)
            {
                GetBlock(descriptor.Blocks[i]).Data.CopyTo(result, i * FileSystemSettings.BlockSize);
                if (blocksCount <= i + 1)
                {
                    return result;
                }
            }

            ReadFileMapEntries(descriptor.MapIndex,
                    Convert.ToUInt16(descriptor.FileSize - FileSystemSettings.DefaultBlocksSize),
                    blocksCount - FileSystemSettings.DefaultBlocksInDescriptor)
                .CopyTo(result, FileSystemSettings.BlockSize * FileSystemSettings.DefaultBlocksInDescriptor);

            return result;
        }

        public override void DeleteFile(string filename)
        {
            var descriptorId = FileLookUp(filename);
            var descriptor = GetFileDescriptor(descriptorId);
            if (descriptor.References > 1 ||
                _openFilesDescriptorsIds.Contains(descriptorId))
            {
                descriptor.References -= 1;
                SetFileDescriptor(descriptorId, descriptor);
                return;
            }

            RemoveFileFromFs(descriptorId);
        }

        public override void OpenFile(string filename)
        {
            _openFilesDescriptorsIds.Add(FileLookUp(filename));
        }

        public override void CloseFile(string filename)
        {
            var descriptorId = FileLookUp(filename);
            var result = _openFilesDescriptorsIds.Remove(descriptorId);
            if (!result)
            {
                throw new Exception($"File with name {filename} was not open");
            }

            var descriptor = GetFileDescriptor(descriptorId);
            if (descriptor.References == 0)
            {
                RemoveFileFromFs(descriptorId);
            }
        }

        private byte[] ReadBytes(int count, int offset)
        {
            var result = new byte[count];
            FsFileStream.Position = offset;
            FsFileStream.Read(result, 0, count);
            return result;
        }

        private void WriteObject<T>(T obj, int offset) where T : struct
        {
            var byteArr = obj.ToByteArray();
            WriteBytes(byteArr, offset);
        }

        private void WriteBytes(byte[] bytes, int offset)
        {
            FsFileStream.Position = offset;
            FsFileStream.Write(bytes, 0, bytes.Length);
        }

        private ushort GetFirstFreeDescriptor()
        {
            for (ushort i = 0; i < _totalBlocks; i++)
            {
                var descriptor = GetFileDescriptor(i);
                if (descriptor.FileDescriptorType == FileDescriptorType.Unused)
                {
                    return i;
                }
            }

            throw new Exception("No free descriptor found");
        }

        private int GetFirstFreeBlock()
        {
            var takeBytes = 16;
            for (var i = 0; i < FileSystemSettings.BlocksCount / 8 / 16; i++)
            {
                var bitmask = GetBitmask(i * takeBytes, takeBytes);
                var firstFree = bitmask.GetIndexOfFirst(FileSystemSettings.BitmaskFreeBit);
                return i * takeBytes * 8 + firstFree;
            }

            throw new Exception("No free block found");
        }

        private ushort GetFirstFreeBlockAndReserve()
        {
            short takeBytes = 16;
            for (short i = 0; i < FileSystemSettings.BlocksCount / 8 / 16; i++)
            {
                var bitmask = GetBitmask(i * takeBytes, takeBytes);
                var firstFree = bitmask.GetIndexOfFirst(FileSystemSettings.BitmaskFreeBit);
                SetBitmask(i * takeBytes, bitmask);
                return Convert.ToUInt16(i * takeBytes * 8 + firstFree);
            }

            throw new Exception("No free block found");
        }

        private int GetBlockOffset(ushort blockIndex)
        {
            return SizeHelper.GetBlocksOffset(_descriptorsCount) + FileSystemSettings.BlockSize * blockIndex;
        }

        private void AddLinkToDirectory(FileDescriptor directoryDescriptor, string filename, ushort fileDescriptorId)
        {
            if (directoryDescriptor.FileDescriptorType != FileDescriptorType.Directory)
            {
                throw new Exception("Internal error, directory descriptor");
            }

            var directoryBlock = GetBlock(directoryDescriptor.Blocks[0]);
            var directoryEntry = new DirectoryEntry
            {
                Name = filename,
                IsValid = true,
                FileDescriptorId = fileDescriptorId,
            }.ToByteArray();
            for (int i = directoryDescriptor.FileSize; i < directoryDescriptor.FileSize + directoryEntry.Length; i++)
            {
                directoryBlock.Data[i] = directoryEntry[i - directoryDescriptor.FileSize];
            }

            directoryDescriptor.FileSize += Convert.ToUInt16(directoryEntry.Length);
            directoryDescriptor.References++;
            SetFileDescriptor(directoryDescriptor.Id, directoryDescriptor);
            SetBlock(directoryDescriptor.Blocks[0], directoryBlock);
        }

        private byte[] ReadFileMapEntries(ushort fileMapIndex, ushort fileSize, int? totalBlocks = null)
        {
            var result = new byte[fileSize];
            totalBlocks ??= OpHelper.DivWithRoundUp(fileSize, FileSystemSettings.BlockSize);
            var blocksToRead = totalBlocks > FileSystemSettings.RefsInFileMap
                ? FileSystemSettings.RefsInFileMap
                : totalBlocks.Value;
            var fileMap = GetFileMap(fileMapIndex);
            var blockIndexes =
                FsFileStream.ReadStructs<ushort>(GetBlockOffset(fileMapIndex), FileSystemSettings.RefsInFileMap)
                    .ToList();
            for (var i = 0; i < blocksToRead; i++)
            {
                GetBlock(i).Data.CopyTo(result, i * FileSystemSettings.BlockSize);
            }

            if (blocksToRead < totalBlocks)
            {
                ReadFileMapEntries(fileMap.NextMapIndex,
                        Convert.ToUInt16(fileSize - FileSystemSettings.BlockSize * blocksToRead),
                        totalBlocks - FileSystemSettings.BlockSize)
                    .CopyTo(result, blocksToRead * FileSystemSettings.BlockSize);
            }

            return result;
        }

        private ushort FileLookUp(string fileName)
        {
            var filesCount = Root.FileSize / SizeHelper.GetStructureSize<DirectoryEntry>();
            try
            {
                return FsFileStream
                    .ReadStructs<DirectoryEntry>(SizeHelper.GetBlocksOffset(_descriptorsCount), filesCount)
                    .Single(de => de.Name == fileName)
                    .FileDescriptorId;
            }
            catch (Exception e)
            {
                throw new Exception($"File with name {fileName} not found");
            }
        }

        private void RemoveFileFromFs(ushort fileDescriptorId)
        {
            var descriptor = GetFileDescriptor(fileDescriptorId);
            var blocksCount = OpHelper.DivWithRoundUp(descriptor.FileSize, FileSystemSettings.BlockSize);

            var blockIds = new List<ushort>();

            for (var i = 0; i < FileSystemSettings.DefaultBlocksInDescriptor; i++)
            {
                blockIds.Add(descriptor.Blocks[i]);
                if (i + 1 == blocksCount)
                {
                    break;
                }
            }

            if (blocksCount > FileSystemSettings.DefaultBlocksInDescriptor)
            {
                blockIds.Add(descriptor.MapIndex);
                var bytesToRead = descriptor.FileSize -
                                  FileSystemSettings.BlockSize * FileSystemSettings.DefaultBlocksInDescriptor;
                var blocksToRead = blocksCount - FileSystemSettings.DefaultBlocksInDescriptor;
                var fileMap = GetFileMap(descriptor.MapIndex);
                for (var i = 0; i < blocksToRead; i++)
                {
                    if (bytesToRead >= FileSystemSettings.BlockSize)
                    {
                        blockIds.AddRange(fileMap.Indexes);
                        blockIds.Add(fileMap.NextMapIndex);
                        bytesToRead -= FileSystemSettings.BlockSize;
                        if (bytesToRead > FileSystemSettings.BlocksCount)
                        {
                            fileMap = GetFileMap(fileMap.NextMapIndex);
                        }
                    }

                    if (bytesToRead < FileSystemSettings.BlockSize)
                    {
                        for (var j = 0; j < bytesToRead; j++)
                        {
                            blockIds.Add(fileMap.Indexes[j]);
                        }
                    }
                }
            }

            foreach (var blockId in blockIds)
            {
                SetBitFree(blockId);
            }

            descriptor.FileDescriptorType = FileDescriptorType.Unused;
            SetFileDescriptor(fileDescriptorId, descriptor);
        }
    }
}