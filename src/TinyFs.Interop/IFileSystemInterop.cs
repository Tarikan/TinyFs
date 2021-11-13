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

        void CreateFile(string filename);

        void WriteToFile(byte[] file, int fd, int offset, ushort size);
        
        List<DirectoryEntry> DirectoryList();

        byte[] ReadFile(int fd, int offset, ushort size);
        
        void UnlinkFile(string filename);

        void LinkFile(string existingFileName, string linkName);

        FileDescriptor Truncate(string filename, ushort size);

        int OpenFile(string filename);

        void CloseFile(int fd);
    }
}