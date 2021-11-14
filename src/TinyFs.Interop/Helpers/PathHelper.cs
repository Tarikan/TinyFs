using System;
using TinyFs.Domain.Models;

namespace TinyFs.Interop.Helpers
{
    public static class PathHelper
    {
        public static string GetDirectoryByPath(string name)
        {
            if (!name.Contains(FileSystemSettings.Separator)) return ".";
            var dir = name[..name.LastIndexOf(FileSystemSettings.Separator, StringComparison.Ordinal)];
            return string.IsNullOrEmpty(dir) ? FileSystemSettings.Separator : dir;
        }

        public static string GetFilenameByPath(string name)
        {
            return name[(name.LastIndexOf(FileSystemSettings.Separator, StringComparison.Ordinal) + 1)..];
        }
    }
}