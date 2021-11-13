using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TinyFs.Domain.Models;

namespace TinyFs.Interop
{
    public abstract class FileSystemBase : IFileSystemInterop, IDisposable
    {
        protected readonly FileStream FsFileStream;

        protected FileSystemBase(FileStream fsFileStream)
        {
            FsFileStream = fsFileStream;
        }

        public abstract Block GetBlock(int index);

        public abstract void SetBlock(int index, Block block);

        public abstract FileMap GetFileMap(int index);

        public abstract void SetFileMap(int index, FileMap fileMap);

        public abstract FileDescriptor GetFileDescriptor(ushort id);

        public abstract void SetFileDescriptor(ushort id, FileDescriptor fileDescriptor);

        public abstract BitArray GetBitmask(int offset, int bytes = 16);

        public abstract void SetBitmask(int offset, BitArray bitArray);

        public abstract void SetBitFree(int index);

        public abstract void UnsetBitFree(int index);
        public abstract void CreateFile(string filename);

        public abstract void WriteToFile(byte[] file, int fd, int offset, ushort size);

        public abstract byte[] ReadFile(int fd, int offset, ushort size);

        public abstract void UnlinkFile(string filename);

        public abstract void LinkFile(string existingFileName, string linkName);

        public abstract FileDescriptor Truncate(string filename, ushort size);

        public abstract List<DirectoryEntry> DirectoryList();

        public abstract int OpenFile(string filename);

        public abstract void CloseFile(int fd);

        public void Dispose()
        {
            FsFileStream?.Dispose();
        }
    }
}