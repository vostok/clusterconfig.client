using System;
using System.Threading;
using System.Threading.Tasks;

namespace Vostok.ClusterConfig.Client.Updaters
{
    internal class RemoteUpdater
    {
        public async Task<RemoteUpdateResult> UpdateAsync(RemoteUpdateResult lastResult, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}