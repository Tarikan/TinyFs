namespace TinyFs.Interop
{
    public interface IFileSystemProvider
    {
        IFileSystemInterop CreateNewFileSystem(string fsName, ushort descriptorsCount);

        IFileSystemInterop MountExistingFileSystem(string fsName);
    }
}