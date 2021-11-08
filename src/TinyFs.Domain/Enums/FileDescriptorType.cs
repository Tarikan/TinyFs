namespace TinyFs.Domain.Enums
{
    public enum FileDescriptorType : byte
    {
        Unused = 0, // file descriptor that not assigned to any file
        File = 1,
        Directory = 2,
        Symlink = 3, // not implemented yet
    }
}