using System;
using System.IO;

namespace Vostok.ClusterConfig.Client.Helpers
{
    internal static class FolderLocator
    {
        public static DirectoryInfo Locate(string relativePath, int maxOutwardHops)
        {
            var baseDirectory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);

            var initialGuess = new DirectoryInfo(Path.Combine(baseDirectory.FullName, relativePath));
            if (initialGuess.Exists)
                return initialGuess;

            for (var i = 0; i < maxOutwardHops; i++)
            {
                baseDirectory = baseDirectory.Parent;

                if (baseDirectory == null)
                    break;

                var folder = new DirectoryInfo(Path.Combine(baseDirectory.FullName, relativePath));
                if (folder.Exists)
                    return folder;
            }

            return initialGuess;
        }
    }
}