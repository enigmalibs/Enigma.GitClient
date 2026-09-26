using System;
using System.IO;
using System.Threading;

namespace Enigma.GitClient.Core.IntegrationTests.Infrastructure;

/// <summary>
/// Deletes a directory tree that may contain read-only files. git marks everything under
/// <c>.git/objects</c> read-only, which makes a plain recursive delete fail on Windows.
/// </summary>
internal static class DirectoryCleanup
{
    public static void Delete(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                ClearReadOnlyAttributes(path);
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(50);
            }
        }

        // A leftover temporary directory is not worth failing a test run over; the operating
        // system's temp cleaner will reclaim it.
    }

    private static void ClearReadOnlyAttributes(string path)
    {
        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            FileAttributes attributes = File.GetAttributes(file);
            if (attributes.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
        }
    }
}
