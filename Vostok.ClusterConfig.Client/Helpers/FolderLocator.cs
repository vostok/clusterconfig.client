using System.IO;

namespace Vostok.ClusterConfig.Client.Helpers
{
    internal static class FolderLocator
    {
        public static DirectoryInfo Locate(string from, string relativePath, int maxOutwardHops = 10)
        {
            if (relativePath == Path.GetFullPath(relativePath))
                return new DirectoryInfo(relativePath);

            var baseDirectory = new DirectoryInfo(from);

            var initialGuess = new DirectoryInfo(Path.Combine(baseDirectory.FullName, relativePath));
            if (initialGuess.Exists)
                return initialGuess;

            try
            {
                for (var i = 0; i < maxOutwardHops; i++)
                {
                    baseDirectory = baseDirectory.Parent;

                    if (baseDirectory == null)
                        break;

                    var folder = new DirectoryInfo(Path.Combine(baseDirectory.FullName, relativePath));
                    if (folder.Exists)
                        return folder;
                }
            }
            catch
            {
                // ignored
            }

            return initialGuess;
        }
    }
}