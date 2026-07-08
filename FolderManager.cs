namespace ScreenshotOrganizer;

internal static class FolderManager
{
    public static readonly string ScreenshotsRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "Screenshots");

    public static string EnsureAppFolder(string appName)
    {
        var targetPath = Path.Combine(ScreenshotsRoot, appName);
        if (!Directory.Exists(targetPath))
        {
            Directory.CreateDirectory(targetPath);
            Logger.Info($"Created folder: {targetPath}");
        }
        return targetPath;
    }

    public static string GetUniqueFilePath(string folder, string fileName)
    {
        var filePath = Path.Combine(folder, fileName);
        if (!File.Exists(filePath))
            return filePath;

        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var counter = 1;

        do
        {
            filePath = Path.Combine(folder, $"{nameWithoutExt} ({counter}){extension}");
            counter++;
        }
        while (File.Exists(filePath));

        return filePath;
    }
}
