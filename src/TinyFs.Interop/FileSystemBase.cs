using System;
using System.Collections;
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

        public abstract void CreateFile(byte[] file, string filename);

        public abstract byte[] ReadFile(string filename);

        public abstract void DeleteFile(string filename);
        
        public abstract void OpenFile(string filename);

        public abstract void CloseFile(string filename);

        public void Dispose()
        {
            FsFileStream?.Dispose();
        }
    }
}