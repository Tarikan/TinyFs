using System;
using System.Collections;
using System.Collections.Generic;
using TinyFs.Domain.Models;

namespace TinyFs.Interop
{
    public interface IFileSystemInterop : IDisposable
    {
        Block GetBlock(int index);

        void SetBlock(int index, Block block);

        FileMap GetFileMap(int index);

        void SetFileMap(int index, FileMap fileMap);

        FileDescriptor GetFileDescriptor(ushort id);

        void SetFileDescriptor(ushort id, FileDescriptor fileDescriptor);

        BitArray GetBitmask(int offset, int bytes = 16);

        void SetBitmask(int offset, BitArray bitArray);
        
        void SetBitFree(int index);

        void UnsetBitFree(int index);

        void CreateFile(string filename, ushort cwd = 0);

        void WriteToFile(byte[] file, int fd, int offset, ushort size);
        
        List<DirectoryEntry> DirectoryList(ushort directoryDescriptorId = 0);

        byte[] ReadFile(int fd, int offset, ushort size);
        
        void UnlinkFile(string filename, ushort cwd = 0);

        void LinkFile(string existingFileName, string linkName, ushort cwd = 0);

        FileDescriptor Truncate(string filename, ushort size, ushort cwd = 0);

        int OpenFile(string filename, ushort cwd = 0);

        void CloseFile(int fd);

        void MakeDirectory(string directoryName, ushort cwd = 0);

        void RemoveDirectory(string directoryName, ushort cwd = 0);

        void CreateSymlink(string path, string payload, ushort cwd = 0);

        FileDescriptor LookUp(string path,
            ushort cwd = 0,
            bool resolveSymlink = true,
            int symlinkMaxCount = FileSystemSettings.MaxSymlinkInOneLookup);
    }
}