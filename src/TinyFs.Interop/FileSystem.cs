using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TinyFs.Domain.Enums;
using TinyFs.Domain.Models;
using TinyFs.Interop.Extensions;
using TinyFs.Interop.Helpers;

namespace TinyFs.Interop
{
    public class FileSystem : FileSystemBase
    {
        private FileDescriptor Cwd => Root;

        private readonly int _descriptorsCount;

        private readonly int _fsSize;
        private FileDescriptor Root => GetFileDescriptor(0);

        // private ICollection<FileDescriptor> Descriptors =>
        //     FsFileStream.ReadStructs<FileDescriptor>(FileSystemSettings.DescriptorsOffset, _descriptorsCount);

        // private ICollection<FileMap> FileMaps =>
        //     FsFileStream.ReadStructs<FileMap>(SizeHelper.GetBlocksOffset(_descriptorsCount),
        //         FileSystemSettings.BlocksCount);

        private int _directoryEntriesInBlock
            = FileSystemSettings.BlockSize / SizeHelper.GetStructureSize<DirectoryEntry>();

        // private ICollection<DirectoryEntry> RootFiles =>
        //     FsFileStream.ReadStructs<DirectoryEntry>(SizeHelper.GetBlocksOffset(_descriptorsCount),
        //         FileSystemSettings.BlockSize / SizeHelper.GetStructureSize<DirectoryEntry>());

        private Block RootBlock => GetBlock(0);
        private int _fd = 0;
        private readonly Dictionary<int, ushort> _openedFiles = new Dictionary<int, ushort>();

        public FileSystem(FileStream fsFileStream) : base(fsFileStream)
        {
            _fsSize = FileSystemSettings.BlocksCount * FileSystemSettings.BlockSize;
            _descriptorsCount = fsFileStream.ReadStruct<short>(FileSystemSettings.DescriptorsCountOffset);
            if (Root.FileDescriptorType == FileDescriptorType.Unused)
            {
                Init();
            }
        }

        #region Setters

        public override Block GetBlock(int index)
        {
            if (index == FileSystemSettings.NullDescriptor)
            {
                return new Block
                {
                    Data = Enumerable.Repeat((byte)0, FileSystemSettings.BlockSize).ToArray(),
                };
            }

            if (index >= FileSystemSettings.BlocksCount)
            {
                throw new Exception("Trying to get block with index that bigger that maximal");
            }

            return FsFileStream.ReadStruct<Block>(SizeHelper.GetBlocksOffset(_descriptorsCount) +
                                                  SizeHelper.GetStructureSize<Block>() * index);
        }

        public override void SetBlock(int index, Block block)
        {
            if (index == FileSystemSettings.NullDescriptor)
            {
                throw new Exception("Trying to write to reserved block. Aborting.");
            }

            if (index >= FileSystemSettings.BlocksCount)
            {
                throw new Exception("Trying to set block with index that bigger that maximal");
            }

            var offset = SizeHelper.GetBlocksOffset(_descriptorsCount) + FileSystemSettings.BlockSize * index;
            FsFileStream.WriteObject(block, offset);
        }

        public override FileMap GetFileMap(int index)
        {
            if (index == FileSystemSettings.NullDescriptor)
            {
                return new FileMap
                {
                    Indexes = Enumerable.Repeat((ushort)0, FileSystemSettings.BlockSize - 2).ToArray(),
                    NextMapIndex = 0
                };
            }

            if (index >= FileSystemSettings.BlocksCount)
            {
                throw new Exception("Trying to get block with index that bigger that maximal");
            }

            return FsFileStream.ReadStruct<FileMap>(SizeHelper.GetBlocksOffset(_descriptorsCount) +
                                                    SizeHelper.GetStructureSize<FileMap>() * index);
        }

        public override void SetFileMap(int index, FileMap fileMap)
        {
            if (index >= FileSystemSettings.BlocksCount)
            {
                throw new Exception("Trying to set block with index that bigger that maximal");
            }

            var offset = SizeHelper.GetBlocksOffset(_descriptorsCount) + SizeHelper.GetStructureSize<Block>() * index;
            FsFileStream.WriteObject(fileMap, offset);
        }

        public override FileDescriptor GetFileDescriptor(ushort id)
        {
            if (id >= _descriptorsCount)
            {
                throw new Exception("Trying to get descriptor with index that bigger that maximal");
            }

            var offset = FileSystemSettings.DescriptorsOffset + SizeHelper.GetStructureSize<FileDescriptor>() * id;
            return FsFileStream.ReadStruct<FileDescriptor>(offset);
        }

        public override void SetFileDescriptor(ushort id, FileDescriptor fileDescriptor)
        {
            if (id >= _descriptorsCount)
            {
                throw new Exception("Trying to set descriptor with index that bigger that maximal");
            }

            var offset = FileSystemSettings.DescriptorsOffset + SizeHelper.GetStructureSize<FileDescriptor>() * id;
            FsFileStream.WriteObject(fileDescriptor, offset);
        }

        public override BitArray GetBitmask(int offset, int bytes = 16)
        {
            var byteArray = FsFileStream.ReadBytes(bytes, offset + FileSystemSettings.BitMapOffset);
            return new BitArray(byteArray);
        }

        public override void SetBitmask(int offset, BitArray bitArray)
        {
            var buff = new byte[OpHelper.DivWithRoundUp(bitArray.Count, 8)];
            bitArray.CopyTo(buff, 0);
            FsFileStream.WriteBytes(buff, offset + FileSystemSettings.BitMapOffset);
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

        #endregion Setters

        public override void WriteToFile(byte[] file, int fd, int offset, ushort size)
        {
            if (!_openedFiles.ContainsKey(fd))
            {
                throw new Exception("File with numeric descriptor fd is not open");
            }

            var descriptorId = _openedFiles[fd];
            WriteToFile(descriptorId, file, offset, size);
        }

        private void WriteToFile(ushort descriptorId, byte[] file, int offset, ushort size)
        {
            var descriptor = GetFileDescriptor(descriptorId);
            if (descriptor.FileSize < offset + size)
            {
                throw new Exception("file too small to write");
            }

            var startBlockIndexInList = offset / FileSystemSettings.BlockSize;
            var endBlockIndexInList = (offset + size) / FileSystemSettings.BlockSize;
            var blocksToWrite = endBlockIndexInList - startBlockIndexInList + 1;
            ushort fileMapId = 0;
            var positionInMap = 0;
            var mapsUsed = false;

            var blocks = new List<ushort>(blocksToWrite);
            if (startBlockIndexInList < FileSystemSettings.DefaultBlocksInDescriptor)
            {
                for (
                    var i = startBlockIndexInList;
                    i <= endBlockIndexInList &&
                    i < FileSystemSettings.DefaultBlocksInDescriptor;
                    i++)
                {
                    blocks.Add(descriptor.Blocks[i]);
                }

                if (blocksToWrite > FileSystemSettings.DefaultBlocksInDescriptor - startBlockIndexInList)
                {
                    mapsUsed = true;
                    fileMapId = descriptor.MapIndex;
                    var blocksToWriteToMaps = blocksToWrite -
                                              (FileSystemSettings.DefaultBlocksInDescriptor - startBlockIndexInList);
                    blocks.AddRange(ReadNElementsFromFileMap(descriptor.MapIndex, 0, blocksToWriteToMaps));
                }
            }
            else
            {
                mapsUsed = true;
                var mapWithFirstBlockIndex = (startBlockIndexInList - FileSystemSettings.DefaultBlocksInDescriptor) /
                                             FileSystemSettings.RefsInFileMap;
                fileMapId = GetNthFileMap(descriptorId, mapWithFirstBlockIndex);
                positionInMap = (startBlockIndexInList - FileSystemSettings.DefaultBlocksInDescriptor) %
                                FileSystemSettings.RefsInFileMap;
                blocks.AddRange(ReadNElementsFromFileMap(fileMapId, positionInMap, blocksToWrite - blocks.Count));
            }

            var byteOffset = offset % FileSystemSettings.BlockSize;
            var writtenBlocks = WriteDataToBlocks(blocks, file, byteOffset);
#if DEBUG
            var writtenText = Encoding.Default.GetString(ReadAllBlocks(writtenBlocks).ToArray());
#endif

            if (startBlockIndexInList < FileSystemSettings.DefaultBlocksInDescriptor)
            {
                var blocksToWriteToDescriptor = FileSystemSettings.DefaultBlocksInDescriptor - startBlockIndexInList >
                                                writtenBlocks.Count
                    ? writtenBlocks.Count
                    : FileSystemSettings.DefaultBlocksInDescriptor - startBlockIndexInList;

                for (
                    var i = startBlockIndexInList;
                    i < FileSystemSettings.DefaultBlocksInDescriptor && i - startBlockIndexInList < writtenBlocks.Count;
                    i++)
                {
                    descriptor.Blocks[i] = writtenBlocks[i - startBlockIndexInList];
                }

                if (writtenBlocks.Count > blocksToWriteToDescriptor)
                {
                    WriteNElementsToFileMap(fileMapId,
                        writtenBlocks.Skip(
                            FileSystemSettings.DefaultBlocksInDescriptor - startBlockIndexInList).ToList(),
                        positionInMap,
                        writtenBlocks.Count - (FileSystemSettings.DefaultBlocksInDescriptor - startBlockIndexInList));
                }
            }
            else
            {
                var mapWithFirstBlockIndex = (startBlockIndexInList - FileSystemSettings.DefaultBlocksInDescriptor) /
                                             FileSystemSettings.RefsInFileMap;
                fileMapId = GetNthFileMap(descriptorId, mapWithFirstBlockIndex);
                positionInMap = (startBlockIndexInList - FileSystemSettings.DefaultBlocksInDescriptor) %
                                FileSystemSettings.RefsInFileMap;
                WriteNElementsToFileMap(fileMapId, writtenBlocks, positionInMap, writtenBlocks.Count);
            }

            SetFileDescriptor(descriptorId, descriptor);
        }

        public override void CreateFile(string filename)
        {
            if (filename == "file-150")
            {
                Console.WriteLine();
            }

            var descriptorId = GetFirstFreeDescriptor();
            var descriptor = new FileDescriptor
            {
                Id = descriptorId,
                References = 1,
                FileSize = 0, // Convert.ToUInt16(file.Length),
                FileDescriptorType = FileDescriptorType.File,
                Blocks = new ushort[FileSystemSettings.DefaultBlocksInDescriptor]
            };

            SetFileDescriptor(descriptorId, descriptor);
            AddLinkToDirectory(Root.Id, filename, descriptorId);
        }

        public override byte[] ReadFile(int fd, int offset, ushort size)
        {
            if (!_openedFiles.ContainsKey(fd))
            {
                throw new Exception("File with numeric descriptor fd is not open");
            }

            var descriptorId = _openedFiles[fd];
            return ReadFile(descriptorId, offset, size);
        }

        private byte[] ReadFile(ushort descriptorId, int offset, ushort size)
        {
            var descriptor = GetFileDescriptor(descriptorId);
            if (descriptor.FileSize < offset + size)
            {
                throw new Exception("Wrong offset");
            }

            if (descriptor.FileSize == 0)
            {
                return Array.Empty<byte>();
            }

            var startBlockIndexInList = offset / FileSystemSettings.BlockSize;
            var endBlockIndexInList = (offset + size) / FileSystemSettings.BlockSize;

            var blocksCount = endBlockIndexInList - startBlockIndexInList + 1;

            var blocks = new List<ushort>(blocksCount);
            if (startBlockIndexInList < FileSystemSettings.DefaultBlocksInDescriptor)
            {
                for (var i = startBlockIndexInList;
                    i < FileSystemSettings.DefaultBlocksInDescriptor &&
                    i <= endBlockIndexInList;
                    i++)
                {
                    blocks.Add(descriptor.Blocks[i]);
                }
            }

            ushort mapId = descriptor.MapIndex;
            int fileMapOffset;
            int fileMapBlocksCount;
            if (startBlockIndexInList < FileSystemSettings.DefaultBlocksInDescriptor)
            {
                mapId = descriptor.MapIndex;
                fileMapOffset = 0;
                fileMapBlocksCount = blocksCount -
                                     (FileSystemSettings.DefaultBlocksInDescriptor - startBlockIndexInList);
            }
            else
            {
                mapId = GetNthFileMap(descriptorId, startBlockIndexInList / FileSystemSettings.RefsInFileMap);
                fileMapOffset = startBlockIndexInList - FileSystemSettings.DefaultBlocksInDescriptor;
                fileMapBlocksCount = blocksCount;
            }

            if (endBlockIndexInList >= FileSystemSettings.DefaultBlocksInDescriptor)
            {
                var elementsFromFileMap =
                    ReadNElementsFromFileMap(mapId, fileMapOffset, fileMapBlocksCount);
                blocks.AddRange(elementsFromFileMap);
            }
#if DEBUG
            var test = System.Text.Encoding.Default.GetString(ReadAllBlocks(blocks).ToArray());
#endif
            var bytesToSkip = offset % FileSystemSettings.BlockSize;

            return ReadAllBlocks(blocks).Skip(bytesToSkip).Take(size).ToArray();
        }

        public override void UnlinkFile(string filename)
        {
            var descriptor = LookUp(filename);
            var dirPath = filename[..filename.LastIndexOf(FileSystemSettings.Separator, StringComparison.Ordinal)];
            var directory = LookUp(dirPath);
            RemoveLinkFromDirectory(GetFileDescriptor(directory.Id), PathHelper.GetFilenameByPath(filename));
            if (descriptor.References > 1 ||
                _openedFiles.ContainsValue(descriptor.Id))
            {
                descriptor.References -= 1;
                SetFileDescriptor(descriptor.Id, descriptor);
                return;
            }

            RemoveFileFromFs(descriptor.Id);
        }

        public override void LinkFile(string existingFileName, string linkName)
        {
            var descriptor = LookUp(existingFileName);
            var descriptorId = descriptor.Id;
            descriptor.References++;
            SetFileDescriptor(descriptorId, descriptor);
            AddLinkToDirectory(Root.Id, linkName, descriptorId);
        }

        public override FileDescriptor Truncate(string filename, ushort size)
        {
            var descriptor = LookUp(filename);
            var descriptorId = descriptor.Id;
            var oldSize = descriptor.FileSize;
            descriptor.FileSize = size;
            var oldBlocksCount = OpHelper.DivWithRoundUp(oldSize, FileSystemSettings.BlockSize);
            var newBlocksCount = OpHelper.DivWithRoundUp(size, FileSystemSettings.BlockSize);
            var oldMapsCount = OpHelper.DivWithRoundUp(
                oldBlocksCount - FileSystemSettings.DefaultBlocksInDescriptor,
                FileSystemSettings.RefsInFileMap);
            var newMapsCount = OpHelper.DivWithRoundUp(
                newBlocksCount - FileSystemSettings.DefaultBlocksInDescriptor,
                FileSystemSettings.RefsInFileMap);
            if (oldSize > size)
            {
                var blocksToFree = oldBlocksCount - newBlocksCount;
                var lastBlockInMapIndex = (newBlocksCount - FileSystemSettings.DefaultBlocksInDescriptor)
                                          % FileSystemSettings.RefsInFileMap;
                var lastMapId = GetNthFileMap(descriptorId, newMapsCount - 1);
                var blocks = ReadNBlocksAndMaps(lastMapId, lastBlockInMapIndex, Convert.ToUInt16(lastMapId));
                FreeBlocks(blocks);
                SetFileDescriptor(descriptorId, descriptor);
                return descriptor;
            }

            if (oldBlocksCount < FileSystemSettings.DefaultBlocksInDescriptor)
            {
                for (
                    var i = oldBlocksCount;
                    i < FileSystemSettings.DefaultBlocksInDescriptor && i < newBlocksCount;
                    i++)
                {
                    descriptor.Blocks[i] = FileSystemSettings.NullDescriptor;
                }
            }

            if (newBlocksCount > FileSystemSettings.DefaultBlocksInDescriptor)
            {
                FileMap lastMap;
                int mapNum = 1;
                ushort mapId = FileSystemSettings.NullDescriptor;
                if (oldMapsCount == 0)
                {
                    descriptor.MapIndex = GetFirstFreeBlockAndReserve();
                    mapId = descriptor.MapIndex;
                    lastMap = GetFileMap(FileSystemSettings.NullDescriptor);
                    SetFileMap(descriptor.MapIndex, lastMap);
                    SetFileDescriptor(descriptorId, descriptor);
                }
                else
                {
                    mapId = GetNthFileMap(descriptorId, oldMapsCount);
                    lastMap = GetFileMap(mapId);
                }

                var placeUsedInLastFileMap = oldBlocksCount - FileSystemSettings.DefaultBlocksInDescriptor >= 0
                    ? (oldBlocksCount - FileSystemSettings.DefaultBlocksInDescriptor) % FileSystemSettings.RefsInFileMap
                    : 0;
                var placeLeftInLastFileMap = FileSystemSettings.RefsInFileMap - placeUsedInLastFileMap;

                if (placeLeftInLastFileMap > 0)
                {
                    for (
                        var i = FileSystemSettings.RefsInFileMap - placeLeftInLastFileMap;
                        i < FileSystemSettings.RefsInFileMap;
                        i++)
                    {
                        lastMap.Indexes[i] = FileSystemSettings.NullDescriptor;
                    }

                    SetFileMap(mapId, lastMap);
                }

                while (mapNum < newMapsCount)
                {
                    mapId = lastMap.NextMapIndex;
                    lastMap = GetFileMap(mapId);
                    mapNum++;
                    for (
                        var i = 0;
                        i < FileSystemSettings.RefsInFileMap;
                        i++)
                    {
                        lastMap.Indexes[i] = FileSystemSettings.NullDescriptor;
                    }

                    SetFileMap(mapId, lastMap);
                }
            }

            SetFileDescriptor(descriptorId, descriptor);
            return descriptor;
        }

        public override int OpenFile(string filename)
        {
            var descriptor = LookUp(filename);
            _openedFiles.Add(++_fd, descriptor.Id);
            return _fd;
        }

        public override void CloseFile(int fd)
        {
            if (!_openedFiles.ContainsKey(fd))
            {
                throw new Exception("File with numeric descriptor fd is not open");
            }

            var descriptorId = _openedFiles[fd];
            _openedFiles.Remove(fd);

            var descriptor = GetFileDescriptor(descriptorId);
            if (descriptor.References == 0)
            {
                RemoveFileFromFs(descriptorId);
            }
        }

        public override void MakeDirectory(string directoryName)
        {
            throw new NotImplementedException();
        }

        public override void RemoveDirectory(string directoryName)
        {
            throw new NotImplementedException();
        }

        public override void ChangeDirectory(string directoryName)
        {
            throw new NotImplementedException();
        }

        public override void CreateSymlink(string path, string payload, ushort cwd = 0)
        {
            var pathToDir = PathHelper.GetDirectoryByPath(path);
            var fileName = PathHelper.GetFilenameByPath(path);
            var dirDescriptor = LookUp(pathToDir, cwd);
            if (dirDescriptor.FileDescriptorType != FileDescriptorType.Directory)
            {
                throw new Exception("Directory not found");
            }

            var descriptorId = GetFirstFreeDescriptor();

            var fileDescriptor = new FileDescriptor
            {
                Id = descriptorId,
                FileDescriptorType = FileDescriptorType.Symlink,
                Blocks = new[]
                {
                    FileSystemSettings.NullDescriptor,
                    FileSystemSettings.NullDescriptor,
                    FileSystemSettings.NullDescriptor,
                    FileSystemSettings.NullDescriptor,
                },
                References = 1,
                FileSize = Convert.ToUInt16(payload.Length),
                MapIndex = FileSystemSettings.NullDescriptor,
            };
            SetFileDescriptor(descriptorId, fileDescriptor);

            AddLinkToDirectory(dirDescriptor.Id, fileName, fileDescriptor.Id);
            WriteToFile(fileDescriptor.Id, Encoding.Default.GetBytes(payload), 0, Convert.ToUInt16(payload.Length));
        }

        public override FileDescriptor LookUp(string path,
            ushort cwd = 0,
            bool resolveSymlink = true,
            int symlinkMaxCount = FileSystemSettings.MaxSymlinkInOneLookup)
        {
            if (path == FileSystemSettings.Separator)
            {
                return Root;
            }

            // remove trailing slash
            if (path.EndsWith(FileSystemSettings.Separator))
            {
                path = path[..^1];
            }

            var directory = path.StartsWith(FileSystemSettings.Separator) ? Root : GetFileDescriptor(cwd);

            if (path.StartsWith(FileSystemSettings.Separator))
            {
                path = path[1..];
            }

            if (path.Contains(FileSystemSettings.Separator))
            {
                var name = path[..path.IndexOf(FileSystemSettings.Separator, StringComparison.Ordinal)];
                var continuePath = path[(path.IndexOf(FileSystemSettings.Separator, StringComparison.Ordinal) + 1)..];

                var directoryEntries = GetDirectoryEntries(directory.Id);
                var nextDir = directoryEntries.SelectMany(d => d)
                    .First(d => d.Name == name);

                var nextDirDescriptor = GetFileDescriptor(nextDir.FileDescriptorId);
                if (nextDirDescriptor.FileDescriptorType == FileDescriptorType.Symlink)
                {
                    if (symlinkMaxCount == 0)
                    {
                        throw new Exception("Exceeded max symlink count");
                    }

                    var value = ReadFile(nextDirDescriptor.Id, 0, nextDirDescriptor.FileSize);
                    var symlinkString = System.Text.Encoding.Default.GetString(value);
                    continuePath = $"{symlinkString}/{continuePath}";
                }

                return LookUp(
                    continuePath,
                    nextDirDescriptor.Id,
                    resolveSymlink,
                    symlinkMaxCount - 1);
            }
            else
            {
                var name = path;
                var directoryEntries = GetDirectoryEntries(directory.Id);
                var file = directoryEntries.SelectMany(d => d)
                    .First(d => d.Name == name);
                var fileDescriptor = GetFileDescriptor(file.FileDescriptorId);
                if (fileDescriptor.FileDescriptorType == FileDescriptorType.Symlink && resolveSymlink)
                {
                    var value = ReadFile(fileDescriptor.Id, 0, fileDescriptor.FileSize);
                    var symlinkString = System.Text.Encoding.Default.GetString(value);

                    return LookUp(
                        symlinkString,
                        cwd,
                        true,
                        symlinkMaxCount - 1);
                }

                return GetFileDescriptor(file.FileDescriptorId);
            }
        }

        public override List<DirectoryEntry> DirectoryList(ushort directoryDescriptorId = 0)
        {
            var directoryEntries = GetDirectoryEntries(directoryDescriptorId)
                .SelectMany(d => d)
                .Where(d => d.IsValid);
            return directoryEntries.ToList();
        }

        private ushort GetFirstFreeDescriptor()
        {
            for (ushort i = 0; i < _descriptorsCount; i++)
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
            for (short i = 0; i < FileSystemSettings.BlocksCount / 8 / 16; i++)
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
                if (firstFree == -1)
                {
                    continue;
                }

                bitmask[firstFree] = !FileSystemSettings.BitmaskFreeBit;
                SetBitmask(i * takeBytes, bitmask);
                return Convert.ToUInt16(i * takeBytes * 8 + firstFree);
            }

            throw new Exception("No free block found");
        }

        private int GetBlockOffset(ushort blockIndex)
        {
            return SizeHelper.GetBlocksOffset(_descriptorsCount) + FileSystemSettings.BlockSize * blockIndex;
        }

        private void AddLinkToDirectory(ushort directoryDescriptorId, string filename, ushort fileDescriptorId)
        {
            var directoryDescriptor = GetFileDescriptor(directoryDescriptorId);
            if (directoryDescriptor.FileDescriptorType != FileDescriptorType.Directory)
            {
                throw new Exception("Internal error, directory descriptor");
            }

            var info = GetFirstFreeDirectoryEntry(directoryDescriptorId);
            directoryDescriptor = GetFileDescriptor(directoryDescriptorId);
            var directoryEntry = new DirectoryEntry
            {
                Name = filename,
                IsValid = true,
                FileDescriptorId = fileDescriptorId,
            }.ToByteArray();
            var blockId = info.blockId;
            var block = GetBlock(blockId);
            if (blockId == FileSystemSettings.NullDescriptor)
            {
                blockId = GetFirstFreeBlockAndReserve();
            }

            directoryEntry.CopyTo(block.Data, info.dEntryId * SizeHelper.GetStructureSize<DirectoryEntry>());
            SetBlock(blockId, block);
        }

        private void RemoveLinkFromDirectory(FileDescriptor directoryDescriptor, string filename)
        {
            if (directoryDescriptor.FileDescriptorType != FileDescriptorType.Directory)
            {
                throw new Exception("Internal error, directory descriptor");
            }

            var (entry, blockId, result) = GetFirstDirectoryEntry(directoryDescriptor.Id, filename);
            if (!result)
            {
                throw new Exception($"file with name {filename} not found");
            }

            var block = GetBlock(blockId);

            var entries = FsFileStream
                .ReadStructs<DirectoryEntry>(GetBlockOffset(blockId), _directoryEntriesInBlock)
                .ToList();
            var entryIndex = entries
                .Select((entry, index) => (entry, index))
                .First(tuple => tuple.entry.Name == filename)
                .index;

            var newEntry = new DirectoryEntry
            {
                Name = "file",
                IsValid = false,
                FileDescriptorId = 0
            };
            var byteArr = newEntry.ToByteArray();
            byteArr.CopyTo(block.Data, entryIndex * SizeHelper.GetStructureSize<DirectoryEntry>());
            SetBlock(blockId, block);
        }

        private byte[] ReadFileMapEntries(ushort fileMapIndex, int offset, ushort fileSize, int? totalBlocks = null)
        {
            var result = new byte[fileSize];
            totalBlocks ??= OpHelper.DivWithRoundUp(fileSize, FileSystemSettings.BlockSize);
            var blocksToRead = totalBlocks > FileSystemSettings.RefsInFileMap
                ? FileSystemSettings.RefsInFileMap
                : totalBlocks.Value;
            var fileMap = GetFileMap(fileMapIndex);
            var blockIndexes =
                FsFileStream.ReadStructs<ushort>(GetBlockOffset(fileMapIndex), FileSystemSettings.RefsInFileMap)
                    .Skip(offset)
                    .ToList();
            for (var i = 0; i < blocksToRead; i++)
            {
                var bytesToRead = fileSize - i * FileSystemSettings.BlockSize >= FileSystemSettings.BlockSize
                    ? FileSystemSettings.BlockSize
                    : fileSize - i * FileSystemSettings.BlockSize;
                var block = GetBlock(blockIndexes[i]);
                block.Data.Take(bytesToRead).ToArray().CopyTo(result, i * FileSystemSettings.BlockSize);
            }

            if (blocksToRead < totalBlocks)
            {
                ReadFileMapEntries(fileMap.NextMapIndex,
                        0,
                        Convert.ToUInt16(fileSize - FileSystemSettings.BlockSize * blocksToRead),
                        totalBlocks - FileSystemSettings.BlockSize)
                    .CopyTo(result, blocksToRead * FileSystemSettings.BlockSize);
            }

            return result;
        }

        private byte[] ReadFileMapEntries(ushort fileMapIndex, int offset, int count)
        {
            if (offset > FileSystemSettings.RefsInFileMap)
            {
                throw new Exception("offset is too big");
            }

            var result = new byte[FileSystemSettings.BlockSize * count];
            var fileMap = GetFileMap(fileMapIndex);
            var blockIndexes =
                FsFileStream.ReadStructs<ushort>(GetBlockOffset(fileMapIndex), FileSystemSettings.RefsInFileMap)
                    .Skip(offset)
                    .Take(count)
                    .ToList();
            for (var i = 0; i < FileSystemSettings.RefsInFileMap - offset && i < count; i++)
            {
                var block = GetBlock(blockIndexes[i]);
                block.Data.CopyTo(result, i * FileSystemSettings.BlockSize);
            }

            if (FileSystemSettings.RefsInFileMap - offset < count)
            {
                ReadFileMapEntries(fileMap.NextMapIndex,
                        0,
                        count - (FileSystemSettings.RefsInFileMap - offset))
                    .CopyTo(result, FileSystemSettings.BlockSize * offset - FileSystemSettings.RefsInFileMap);
            }

            return result;
        }

        private ( ushort fileId, ushort directoryId ) FileLookUpWithDirectory(string fileName)
        {
            var filesCount = Root.FileSize / SizeHelper.GetStructureSize<DirectoryEntry>();
            try
            {
                return new ValueTuple<ushort, ushort>(FsFileStream
                    .ReadStructs<DirectoryEntry>(SizeHelper.GetBlocksOffset(_descriptorsCount), filesCount)
                    .Single(de => de.Name == fileName)
                    .FileDescriptorId, 0);
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

        private ushort GetNthFileMap(ushort fileDescriptorId, int N)
        {
            var mapId = GetFileDescriptor(fileDescriptorId).MapIndex;
            for (var mapNum = 0; mapNum != N; mapNum++)
            {
                var fileMap = GetFileMap(mapId);
                mapId = fileMap.NextMapIndex;
            }

            return mapId;
        }

        private IEnumerable<ushort> ReadNElementsFromFileMap(ushort mapId, int offset, int count)
        {
            if (offset > FileSystemSettings.RefsInFileMap)
            {
                throw new Exception($"offset is bigger than count of refs in fileMap");
            }

            var result = new List<ushort>(count);
            var map = GetFileMap(mapId);
            if (FileSystemSettings.RefsInFileMap - offset < count)
            {
                result.AddRange(map.Indexes.Skip(offset));
                result.AddRange(ReadNElementsFromFileMap(map.NextMapIndex, 0,
                    count - (FileSystemSettings.RefsInFileMap - offset)));
                return result;
            }

            result.AddRange(map.Indexes.Skip(offset).Take(count));
            return result;
        }

        private void WriteNElementsToFileMap(ushort mapId, IReadOnlyList<ushort> data, int offset, int count)
        {
            if (offset > FileSystemSettings.RefsInFileMap)
            {
                throw new Exception($"offset is bigger than count of refs in fileMap");
            }

            var map = GetFileMap(mapId);
            if (FileSystemSettings.RefsInFileMap - offset < count)
            {
                for (var i = offset; i < FileSystemSettings.RefsInFileMap; i++)
                {
                    map.Indexes[i] = data[i - offset];
                }

                SetFileMap(mapId, map);
                WriteNElementsToFileMap(map.NextMapIndex, data.Skip(FileSystemSettings.RefsInFileMap - offset).ToList(),
                    0,
                    count - (FileSystemSettings.RefsInFileMap - offset));
            }

            for (var i = offset; i < data.Count + offset; i++)
            {
                map.Indexes[i] = data[i - offset];
            }

            SetFileMap(mapId, map);
        }

        private List<ushort> WriteDataToBlocks(
            IReadOnlyList<ushort> blockIds,
            byte[] data,
            int offset,
            List<ushort> result = null)
        {
            result ??= new List<ushort>(blockIds.Count);

            if (offset > FileSystemSettings.BlockSize)
            {
                throw new Exception($"offset is bigger than block's size");
            }

            var blockId = blockIds[0];
            if (blockId == FileSystemSettings.NullDescriptor)
            {
                blockId = GetFirstFreeBlockAndReserve();
            }

            var block = GetBlock(blockIds[0]);
            if (FileSystemSettings.BlockSize - offset < data.Length)
            {
                for (var i = offset; i < FileSystemSettings.BlockSize; i++)
                {
                    block.Data[i] = data[i - offset];
                }

                SetBlock(blockId, block);
                result.Add(blockId);
                if (blockIds.Count > 1)
                {
                    WriteDataToBlocks(
                        blockIds.Skip(1).ToList(),
                        data.Skip(FileSystemSettings.BlockSize - offset).ToArray(),
                        0,
                        result);
                }

                return result;
            }

            for (var i = offset; i < offset + data.Length; i++)
            {
                block.Data[i] = data[i - offset];
            }

            SetBlock(blockId, block);
            result.Add(blockId);
            return result;
        }

        private List<byte> ReadAllBlocks(List<ushort> blocks)
        {
            var result = new List<byte>(FileSystemSettings.BlockSize * blocks.Count);
            foreach (var block in blocks)
            {
                result.AddRange(GetBlock(block).Data);
            }

            return result;
        }

        private List<ushort> ReadNBlocksAndMaps(ushort mapId, int offset, ushort count)
        {
            if (offset > FileSystemSettings.RefsInFileMap)
            {
                throw new Exception($"offset is bigger than count of refs in fileMap");
            }

            var result = new List<ushort>(count);
            var map = GetFileMap(mapId);
            if (FileSystemSettings.RefsInFileMap - offset < count)
            {
                result.AddRange(map.Indexes.Skip(offset));
                result.Add(map.NextMapIndex);
                result.AddRange(ReadNBlocksAndMaps(map.NextMapIndex, 0,
                    Convert.ToUInt16(count - (FileSystemSettings.RefsInFileMap - offset))));
                return result;
            }

            result.AddRange(map.Indexes.Skip(offset).Take(count));
            return result;
        }

        private void FreeBlocks(List<ushort> blockIds)
        {
            foreach (var blockId in blockIds)
            {
                SetBitFree(blockId);
            }
        }

        private List<List<DirectoryEntry>> GetDirectoryEntries(ushort directoryDescriptorId)
        {
            var descriptor = GetFileDescriptor(directoryDescriptorId);
            var blocksToTake = descriptor.FileSize / FileSystemSettings.BlockSize;
            var blocks = new List<ushort>(blocksToTake);
            var blocksToTakeFromDescriptor = blocksToTake <= FileSystemSettings.DefaultBlocksInDescriptor
                ? blocksToTake
                : FileSystemSettings.DefaultBlocksInDescriptor;
            for (
                ushort i = 0;
                i < blocksToTakeFromDescriptor;
                i++)
            {
                blocks.Add(descriptor.Blocks[i]);
            }

            if (blocksToTake > blocksToTakeFromDescriptor)
            {
                var blocksToTakeFromMaps = blocksToTake - blocksToTakeFromDescriptor;
                blocks.AddRange(ReadNElementsFromFileMap(descriptor.MapIndex, 0, blocksToTakeFromDescriptor));
            }

            var result = blocks.Select(b => FsFileStream
                    .ReadStructs<DirectoryEntry>(GetBlockOffset(b), _directoryEntriesInBlock).ToList())
                .ToList();
            return result;
        }

        private (ushort blockId, ushort dEntryId) GetFirstFreeDirectoryEntry(ushort directoryDescriptorId)
        {
            var descriptor = GetFileDescriptor(directoryDescriptorId);
            var blocksToTake = descriptor.FileSize / FileSystemSettings.BlockSize;
            var blocks = new List<ushort>(blocksToTake);
            var blocksToTakeFromDescriptor = blocksToTake <= FileSystemSettings.DefaultBlocksInDescriptor
                ? blocksToTake
                : FileSystemSettings.DefaultBlocksInDescriptor;
            for (
                ushort i = 0;
                i < blocksToTakeFromDescriptor;
                i++)
            {
                blocks.Add(descriptor.Blocks[i]);
            }

            foreach (var descriptorBlock in blocks)
            {
                var (id, result) = FindFreeEntryInBlock(descriptorBlock);
                if (!result) continue;
                return (descriptorBlock, id);
            }

            blocks.RemoveAll(_ => true);

            if (blocksToTakeFromDescriptor < blocksToTake)
            {
                blocks.AddRange(
                    ReadNElementsFromFileMap(descriptor.MapIndex, 0, blocksToTake - blocksToTakeFromDescriptor));
                foreach (var mapBlock in blocks)
                {
                    var (id, result) = FindFreeEntryInBlock(mapBlock);
                    if (!result) continue;
                    return (mapBlock, id);
                }
            }

            var newBlockId = GetFirstFreeBlockAndReserve();
            var newBlock = GetBlock(FileSystemSettings.NullDescriptor);
            var entry = new DirectoryEntry
            {
                Name = "empty",
                IsValid = false,
                FileDescriptorId = 0,
            }.ToByteArray();
            entry.CopyTo(newBlock.Data, 0);
            SetBlock(newBlockId, newBlock);

            if (descriptor.FileSize < FileSystemSettings.DefaultBlocksSize)
            {
                var newDefaultBlockId = descriptor.FileSize / FileSystemSettings.BlockSize;
                descriptor.Blocks[newDefaultBlockId] = newBlockId;
                descriptor.FileSize += FileSystemSettings.BlockSize;
                SetFileDescriptor(directoryDescriptorId, descriptor);
                return (newBlockId, 0);
            }

            ushort lastFileMapId;
            FileMap lastFileMap;
            ushort newFileMapId;
            FileMap newFileMap;

            if (descriptor.FileSize == FileSystemSettings.DefaultBlocksSize)
            {
                newFileMapId = GetFirstFreeBlockAndReserve();
                newFileMap = GetFileMap(FileSystemSettings.NullDescriptor);
                SetFileMap(newFileMapId, newFileMap);
                descriptor.MapIndex = newFileMapId;
                SetFileDescriptor(directoryDescriptorId, descriptor);
                newFileMap.Indexes[0] = newBlockId;
                SetFileMap(newFileMapId, newFileMap);
            }
            else if (blocksToTake - blocksToTakeFromDescriptor < FileSystemSettings.RefsInFileMap)
            {
                lastFileMapId = GetNthFileMap(directoryDescriptorId,
                    (blocksToTake - FileSystemSettings.DefaultBlocksInDescriptor) / FileSystemSettings.RefsInFileMap);
                lastFileMap = GetFileMap(lastFileMapId);
                var blocksInFileMap = blocksToTake - blocksToTakeFromDescriptor;
                lastFileMap.Indexes[blocksInFileMap] = newBlockId;
                SetFileMap(lastFileMapId, lastFileMap);
            }
            else
            {
                lastFileMapId = GetNthFileMap(directoryDescriptorId,
                    (blocksToTake - FileSystemSettings.DefaultBlocksInDescriptor) / FileSystemSettings.RefsInFileMap);
                lastFileMap = GetFileMap(lastFileMapId);
                newFileMapId = GetFirstFreeBlockAndReserve();
                newFileMap = GetFileMap(FileSystemSettings.NullDescriptor);
                lastFileMap.NextMapIndex = newFileMapId;
                SetFileMap(lastFileMapId, lastFileMap);
                newFileMap.Indexes[0] = newBlockId;
                SetFileMap(newFileMapId, newFileMap);
            }

            descriptor.FileSize += FileSystemSettings.BlockSize;
            SetFileDescriptor(directoryDescriptorId, descriptor);
            return (newBlockId, 0);
        }

        private (DirectoryEntry entry, ushort blockId, bool result) GetFirstDirectoryEntry(ushort directoryDescriptorId,
            string fileName)
        {
            var descriptor = GetFileDescriptor(directoryDescriptorId);
            var blocksToTake = descriptor.FileSize / FileSystemSettings.BlockSize;
            var blocks = new List<ushort>(blocksToTake);
            var blocksToTakeFromDescriptor = blocksToTake <= FileSystemSettings.DefaultBlocksInDescriptor
                ? blocksToTake
                : FileSystemSettings.DefaultBlocksInDescriptor;
            for (
                ushort i = 0;
                i < blocksToTakeFromDescriptor;
                i++)
            {
                blocks.Add(descriptor.Blocks[i]);
            }

            foreach (var descriptorBlock in blocks)
            {
                var (foundEntry, result) = FindEntryInBlock(descriptorBlock, fileName);
                if (!result) continue;
                return (foundEntry, descriptorBlock, true);
            }

            blocks.RemoveAll(_ => true);

            if (blocksToTakeFromDescriptor < blocksToTake)
            {
                blocks.AddRange(
                    ReadNElementsFromFileMap(descriptor.MapIndex, 0, blocksToTake - blocksToTakeFromDescriptor));
                foreach (var mapBlock in blocks)
                {
                    var (foundEntry, result) = FindEntryInBlock(mapBlock, fileName);
                    if (!result) continue;
                    return (foundEntry, mapBlock, true);
                }
            }

            return (new DirectoryEntry(), 0, false);
        }

        private (ushort id, bool result) FindFreeEntryInBlock(ushort blockId)
        {
            var entries = FsFileStream
                .ReadStructs<DirectoryEntry>(GetBlockOffset(blockId), _directoryEntriesInBlock)
                .ToList();
            var entryExist = entries
                .Select((entry, index) => (entry, index))
                .Any(tuple => !tuple.entry.IsValid);
            if (!entryExist) return (0, false);
            var entryIndex = Convert.ToUInt16(entries
                .Select((entry, index) => (entry, index))
                .First(tuple => !tuple.entry.IsValid)
                .index);
            return (entryIndex, true);
        }

        private (DirectoryEntry entry, bool result) FindEntryInBlock(ushort blockId, string fileName)
        {
            var entries = FsFileStream
                .ReadStructs<DirectoryEntry>(GetBlockOffset(blockId), _directoryEntriesInBlock)
                .ToList();
            var entryExist = entries
                .Select((entry, index) => (entry, index))
                .Any(tuple => tuple.entry.Name == fileName);
            if (!entryExist) return (new DirectoryEntry(), false);
            var entry = entries
                .Select((entry, index) => (entry, index))
                .First(tuple => tuple.entry.Name == fileName)
                .entry;
            return (entry, true);
        }

        private void Init()
        {
            var root = new FileDescriptor
            {
                Id = 0,
                FileDescriptorType = FileDescriptorType.Directory,
                FileSize = 0,
                References = 1,
                Blocks = new ushort[]
                {
                    FileSystemSettings.NullDescriptor,
                    FileSystemSettings.NullDescriptor,
                    FileSystemSettings.NullDescriptor,
                    FileSystemSettings.NullDescriptor,
                },
                MapIndex = 0
            };
            SetFileDescriptor(root.Id, root);

            var rootDirBlockId = GetFirstFreeBlockAndReserve();
            var rootDirBlock = GetBlock(FileSystemSettings.NullDescriptor);
            Encoding.Default.GetBytes(FileSystemSettings.Separator).CopyTo(rootDirBlock.Data, 0);
            SetBlock(rootDirBlockId, rootDirBlock);
            var rootDir = new FileDescriptor
            {
                Id = 1,
                FileDescriptorType = FileDescriptorType.Symlink,
                FileSize = Convert.ToUInt16(FileSystemSettings.CurrentDirSymlink.Length),
                References = 1,
                Blocks = new ushort[]
                {
                    rootDirBlockId,
                    FileSystemSettings.NullDescriptor,
                    FileSystemSettings.NullDescriptor,
                    FileSystemSettings.NullDescriptor,
                },
                MapIndex = 0
            };
            SetFileDescriptor(rootDir.Id, rootDir);
            AddLinkToDirectory(0, FileSystemSettings.CurrentDirSymlink, 1);
        }
    }
}