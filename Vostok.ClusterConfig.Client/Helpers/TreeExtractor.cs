using System;
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
        private static readonly SettingsMergeOptions DefaultMergeOptions = new SettingsMergeOptions
        {
            ObjectMergeStyle = ObjectMergeStyle.Deep,
            ArrayMergeStyle = ArrayMergeStyle.Replace
        };

        private static readonly Func<ClusterConfigPath, (ClusterConfigClientState state, SettingsMergeOptions mergeOptions), ISettingsNode> Factory = SettingsNodeFactory;

        [CanBeNull]
        public static ISettingsNode Extract([NotNull] ClusterConfigClientState state, ClusterConfigPath path, [CanBeNull] SettingsMergeOptions mergeOptions)
        {
            mergeOptions ??= DefaultMergeOptions;

            return state.Cache.Obtain(
                path,
                (state, mergeOptions),
                Factory);
        }

        private static ISettingsNode SettingsNodeFactory(ClusterConfigPath path, (ClusterConfigClientState state, SettingsMergeOptions mergeOptions) args)
        {
            var (state, mergeOptions) = args;
            foreach (var prefix in EnumeratePrefixes(path))
            {
                if (state.Cache.TryGetValue(prefix, out var tree))
                    return tree.ScopeTo(path.Segments.Skip(prefix.Segments.Count()));
            }

            var remoteSettings = GetRemoteSettings(state, path);
            var localSettings = state.LocalTree?.ScopeTo(path.Segments);

            return SettingsNodeMerger.Merge(remoteSettings, localSettings, mergeOptions);
        }

        private static ISettingsNode GetRemoteSettings(ClusterConfigClientState state, ClusterConfigPath path)
        {
            if (state.RemoteSubtrees != null && state.RemoteSubtrees.TryGetSettings(path, out var settings))
                return settings;

            return state.RemoteTree?.GetSettings(path);
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