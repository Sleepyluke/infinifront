using System.IO;

namespace SimCore.Tests.Packs;

internal static class RepoPaths
{
    public static string Root()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && dir.GetFiles("*.sln").Length == 0)
            dir = dir.Parent;
        if (dir is null)
            throw new DirectoryNotFoundException("repo root (directory containing a .sln) not found");
        return dir.FullName;
    }

    public static string Pack(string relative) => Path.Combine(Root(), "packs", relative);

    /// <summary>Normalize line endings so git's CRLF/LF conversion never breaks text equality.</summary>
    public static string Normalize(string s) => s.Replace("\r\n", "\n");
}
