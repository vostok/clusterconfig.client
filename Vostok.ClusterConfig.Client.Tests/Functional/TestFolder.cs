using System;
using System.IO;
using System.Text;
using System.Threading;

// ReSharper disable AssignNullToNotNullAttribute

namespace Vostok.ClusterConfig.Client.Tests.Functional
{
    internal class TestFolder : IDisposable
    {
        public TestFolder()
        {
            Directory = new DirectoryInfo(Guid.NewGuid().ToString());

            if (!Directory.Exists)
                Directory.Create();
        }

        public DirectoryInfo Directory { get; }

        public void CreateFile(string path, Action<StringBuilder> buildContent)
        {
            var relativeDirectory = Path.GetDirectoryName(path);

            var fullDirectory = new DirectoryInfo(Path.Combine(Directory.FullName, relativeDirectory));

            if (!fullDirectory.Exists)
                fullDirectory.Create();
            
            var builder = new StringBuilder();

            buildContent(builder);

            File.WriteAllText(Path.Combine(Directory.FullName, path), builder.ToString());
        }

        public void Dispose()
        {
            for (var i = 0; i < 5; i++)
            {
                Directory.Refresh();

                try
                {
                    if (Directory.Exists)
                        Directory.Delete(true);

                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(200);
                }
            }
        }
    }
}