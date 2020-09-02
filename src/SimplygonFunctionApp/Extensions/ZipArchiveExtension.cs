using System.IO;
using System.IO.Compression;
using System.Linq;

namespace SimplygonFunctionApp.Extensions
{
    public static class ZipArchiveExtension
    {
        public static void CreateEntryFromAny(this ZipArchive archive, string sourceName, string entryName = "")
        {
            var fileName = Path.GetFileName(sourceName);
            if (File.GetAttributes(sourceName).HasFlag(FileAttributes.Directory))
            {
                archive.CreateEntryFromDirectory(sourceName, Path.Combine(entryName, fileName));
            }
            else
            {
                archive.CreateEntryFromFile(sourceName, Path.Combine(entryName, fileName), CompressionLevel.Fastest);
            }
        }

        public static void CreateEntryFromDirectory(this ZipArchive archive, string sourceDirName, string entryName = "")
        {
            foreach (var file in Directory.GetFiles(sourceDirName).Concat(Directory.GetDirectories(sourceDirName)))
            {
                archive.CreateEntryFromAny(file, entryName);
            }
        }
    }
}
