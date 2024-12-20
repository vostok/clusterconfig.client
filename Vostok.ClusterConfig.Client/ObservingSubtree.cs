using System;
using System.Threading;
using System.Threading.Tasks;
using Vostok.ClusterConfig.Client.Abstractions;
using Vostok.Commons.Helpers.Observable;

namespace Vostok.ClusterConfig.Client;

internal class ObservingSubtree
{
    private readonly object observablePropagationLock;
    private IDisposable lastSubscription;
    private TaskCompletionSource<bool> atLeastOnceObtaining;

    public ObservingSubtree(ClusterConfigPath path)
    {
        observablePropagationLock = new object();

        Path = path;
        SubtreeStateObservable = new CachingObservable<ClusterConfigClientState>();
        atLeastOnceObtaining = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        LastVersion = null;
    }

    public ClusterConfigPath Path { get; }
    public CachingObservable<ClusterConfigClientState> SubtreeStateObservable { get; private set; }
    public DateTime? LastVersion { get; set; }

    public bool IsFinalized() => atLeastOnceObtaining.Task.IsCompleted && !atLeastOnceObtaining.Task.IsFaulted;

    public TaskCompletionSource<bool> GetTaskCompletionSource() => atLeastOnceObtaining;

    public void FinalizeSubtree(Task<CachingObservable<ClusterConfigClientState>> rootObservablePropagationTask, CancellationToken cancellationToken)
    {
        var needToSubscribe = false;
        //If the first attempt to update a subtree fails, we must recreate the task so that the next Get(path) succeeds.
        //Until this point, all calls on this subtree have failed.
        if (atLeastOnceObtaining.Task.IsFaulted)
        {
            // (iloktionov): No point in overwriting ObjectDisposedException stored in 'stateSource' in case the client was disposed.
            if (cancellationToken.IsCancellationRequested)
                return;

            var newStateSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            newStateSource.SetResult(true);

            Interlocked.Exchange(ref atLeastOnceObtaining, newStateSource);
            needToSubscribe = true;
        }

        if (atLeastOnceObtaining!.TrySetResult(true) || needToSubscribe)
        {
            Task.Run(async () =>
            {
                var stateObservable = await rootObservablePropagationTask.ConfigureAwait(false);
                lock (observablePropagationLock)
                {
                    if (SubtreeStateObservable.IsCompleted)
                        SubtreeStateObservable = new CachingObservable<ClusterConfigClientState>();

                    lastSubscription?.Dispose();
                    lastSubscription = stateObservable.Subscribe(new TransmittingObserver(SubtreeStateObservable));
                }
            });
        }
    }

    public void FailUnfinalizedSubtrees(Exception error, bool alwaysPushToObservable, Task propagationTask)
    {
        var pushedToSource = atLeastOnceObtaining.TrySetException(error);
        if (pushedToSource || alwaysPushToObservable)
            Task.Run(async () =>
            {
                await propagationTask.ConfigureAwait(false);
                lock (observablePropagationLock)
                {
                    if (atLeastOnceObtaining.Task.IsFaulted || alwaysPushToObservable)
                        SubtreeStateObservable.Error(error);
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