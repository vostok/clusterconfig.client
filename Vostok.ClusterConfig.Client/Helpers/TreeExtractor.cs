using System.Collections.Generic;
using System.Linq;
using System.Text;
using JetBrains.Annotations;
using Vostok.ClusterConfig.Client.Abstractions;
using Vostok.Configuration.Abstractions.Merging;
using Vostok.Configuration.Abstractions.SettingsTree;

namespace Vostok.ClusterConfig.Client.Helpers
{
    internal static class TreeExtractor
    {
        private static readonly SettingsMergeOptions MergeOptions = new SettingsMergeOptions
        {
            ObjectMergeStyle = ObjectMergeStyle.Deep,
            ArrayMergeStyle = ArrayMergeStyle.Replace
        };

        [CanBeNull]
        public static ISettingsNode Extract([NotNull] ClusterConfigClientState state, ClusterConfigPath path)
        {
            return state.Cache.Obtain(
                path,
                p =>
                {
                    foreach (var prefix in EnumeratePrefixes(p))
                    {
                        if (state.Cache.TryGetValue(prefix, out var tree))
                            return tree.ScopeTo(path.Segments.Skip(prefix.Segments.Count()));
                    }

                    var remoteSettings = state.RemoteTree?.GetSettings(p);
                    var localSettings = state.LocalTree?.ScopeTo(p.Segments);

                    return SettingsNodeMerger.Merge(remoteSettings, localSettings, MergeOptions);
                });
        }

        private static IEnumerable<ClusterConfigPath> EnumeratePrefixes(ClusterConfigPath path)
        {
            var segments = path.Segments.ToArray();
            var builder = new StringBuilder();

            for (var i = 0; i < segments.Length - 1; i++)
            {
                if (i > 0)
                    builder.Append(ClusterConfigPath.Separator);

                builder.Append(segments[i]);

                yield return builder.ToString();
            }
        }
    }
}
