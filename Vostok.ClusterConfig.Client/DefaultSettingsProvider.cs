using System;
using System.IO;
using System.Threading;
using JetBrains.Annotations;
using Vostok.ClusterConfig.Client.Helpers;
using Vostok.ClusterConfig.Core.Parsers;

namespace Vostok.ClusterConfig.Client
{
    /// <summary>
    /// <para>Provides default settings used in parameterless ctor of <see cref="ClusterConfigClient"/>.</para>
    /// <para>Looks in an optional configuration file located in <see cref="ClusterConfigClientDefaults.LocalFolder"/> and named <see cref="ClusterConfigClientDefaults.ConfigurationFile"/>.</para>
    /// <para>Examples of valid configuration files:</para>
    /// <list type="bullet">
    ///     <item><description>https://github.com/vostok/clusterconfig.client/blob/master/Vostok.ClusterConfig.Client/config-2.example</description></item>
    ///     <item><description>https://github.com/vostok/clusterconfig.client/blob/master/Vostok.ClusterConfig.Client/config-1.example</description></item>
    /// </list>
    /// </summary>
    [PublicAPI]
    public static class DefaultSettingsProvider
    {
        private static readonly ClusterConfigClientSettings Default
            = new ClusterConfigClientSettings();

        private static volatile Lazy<ClusterConfigClientSettings> Source
            = new Lazy<ClusterConfigClientSettings>(Initialize, LazyThreadSafetyMode.ExecutionAndPublication);

        [NotNull]
        public static ClusterConfigClientSettings Settings => Source.Value;

        internal static void Reset()
            => Source = new Lazy<ClusterConfigClientSettings>(Initialize, LazyThreadSafetyMode.ExecutionAndPublication);

        private static ClusterConfigClientSettings Initialize()
        {
            try
            {
                var folder = FolderLocator.Locate(AppDomain.CurrentDomain.BaseDirectory, ClusterConfigClientDefaults.LocalFolder);
                if (!folder.Exists)
                    return Default;

                var file = new FileInfo(Path.Combine(folder.FullName, ClusterConfigClientDefaults.ConfigurationFile));
                if (!file.Exists)
                    return Default;

                var fileParser = new FileParser(new FileParserSettings());

                var fileSettings = fileParser.Parse(file);
                if (fileSettings == null)
                    return Default;

                var settings = new ClusterConfigClientSettings();

                ConfigurationFileHelper.Apply(fileSettings, settings);

                return settings;
            }
            catch
            {
                return Default;
            }
        }
    }
}
