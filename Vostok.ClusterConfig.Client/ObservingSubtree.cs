using System;
using System.Threading.Tasks;
using Vostok.ClusterConfig.Client.Abstractions;
using Vostok.Commons.Helpers.Observable;

namespace Vostok.ClusterConfig.Client;

internal class ObservingSubtree
{
    private readonly object observablePropagationLock;
    private IDisposable lastSubscription;
    private CachingObservable<ClusterConfigClientState> lastObservable;

    public ObservingSubtree(ClusterConfigPath path)
    {
        observablePropagationLock = new object();
        
        Path = path;
        SubtreeStateObservable = new CachingObservable<ClusterConfigClientState>();
        AtLeastOnceObtaining = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        LastVersion = null;
    }

    public TaskCompletionSource<bool> AtLeastOnceObtaining { get; }

    public ClusterConfigPath Path { get; }
    public CachingObservable<ClusterConfigClientState> SubtreeStateObservable { get; private set; }
    public DateTime? LastVersion { get; set; }

    public void FinalizeSubtree()
    {
        AtLeastOnceObtaining.TrySetResult(true);
    }

    public void EnsureSubscribed(CachingObservable<ClusterConfigClientState> stateObservable)
    {
        if (lastObservable == stateObservable)
            return;
        
        Task.Run(() =>
        {
            lock (observablePropagationLock)
            {
                lastSubscription?.Dispose();
                lastObservable = stateObservable;
                lastSubscription = stateObservable.Subscribe(new TransmittingObserver(SubtreeStateObservable));
            }
        });
    }

    public void RefreshObservable(CachingObservable<ClusterConfigClientState> stateObservable)
    {
        Task.Run(() =>
        {
            lock (observablePropagationLock)
            {
                if (SubtreeStateObservable.IsCompleted)
                    SubtreeStateObservable = new CachingObservable<ClusterConfigClientState>();

                lastSubscription?.Dispose();
                lastObservable = stateObservable;
                lastSubscription = stateObservable.Subscribe(new TransmittingObserver(SubtreeStateObservable));
            }
        });
    }

    private class TransmittingObserver : IObserver<ClusterConfigClientState>
    {
        private readonly CachingObservable<ClusterConfigClientState> stateObservable;

        public TransmittingObserver(CachingObservable<ClusterConfigClientState> stateObservable)
        {
            this.stateObservable = stateObservable;
        }

        public void OnCompleted()
        {
            stateObservable.Complete();
        }

        public void OnError(Exception error)
        {
            stateObservable.Error(error);
        }

        public void OnNext(ClusterConfigClientState value)
        {
            stateObservable.Next(value);
        }
    }
}