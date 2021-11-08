using System.Collections;
using TinyFs.Domain.Models;

namespace TinyFs.Interop
{
    public interface IFileSystemInterop
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

        void CreateFile(byte[] file, string filename);
        
        byte[] ReadFile(string filename);
        
        void DeleteFile(string filename);

        void OpenFile(string filename);

        void CloseFile(string filename);
    }
}