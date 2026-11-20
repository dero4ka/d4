using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace D4;

/// <summary>Поиск папок и файлов по имени в профиле пользователя и на дисках (с правами администратора находит больше).</summary>
internal static class FileSearch
{
    static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "$Recycle.Bin", "System Volume Information", "node_modules", ".git", "Windows", "WinSxS", "ProgramData",
        "Program Files", "Program Files (x86)", "Recovery", "$WinREAgent",
    };

    public static List<(string Path, bool Dir)> Find(string query, Func<bool> cancelled)
    {
        var found = new List<(string Path, bool Dir)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clock = Stopwatch.StartNew();

        var roots = new List<(string Dir, int Depth)>();
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var f in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                     Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                     Path.Combine(profile, "Downloads"),
                     Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                     Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                     Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                 })
            if (!string.IsNullOrEmpty(f) && Directory.Exists(f)) roots.Add((f, 5));

        try
        {
            foreach (var d in DriveInfo.GetDrives())
                if (d.DriveType == DriveType.Fixed && d.IsReady) roots.Add((d.RootDirectory.FullName, 3));
        }
        catch { /* диски недоступны */ }

        foreach (var (root, maxDepth) in roots)
        {
            var queue = new Queue<(string Dir, int Depth)>();
            queue.Enqueue((root, 0));
            while (queue.Count > 0)
            {
                if (cancelled() || clock.ElapsedMilliseconds > 3000 || found.Count >= 60) goto done;
                var (dir, depth) = queue.Dequeue();
                try
                {
                    foreach (var info in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                    {
                        bool isDir = (info.Attributes & FileAttributes.Directory) != 0;
                        if (info.Name.Contains(query, StringComparison.OrdinalIgnoreCase) && seen.Add(info.FullName))
                            found.Add((info.FullName, isDir));

                        if (isDir && depth < maxDepth
                            && (info.Attributes & FileAttributes.ReparsePoint) == 0
                            && !Skip.Contains(info.Name))
                            queue.Enqueue((info.FullName, depth + 1));
                    }
                }
                catch
                {
                    // нет доступа к папке — пропускаем
                }
            }
        }

    done:
        return found
            .OrderByDescending(x => x.Dir)
            .ThenByDescending(x => Path.GetFileName(x.Path).StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => x.Path.Length)
            .Take(14)
            .ToList();
    }
}
