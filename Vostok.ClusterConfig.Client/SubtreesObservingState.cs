using System.Collections.Generic;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.ClusterConfig.Client.Abstractions;

namespace Vostok.ClusterConfig.Client;

internal class SubtreesObservingState
{
    private readonly TaskCompletionSource<bool> completed = new();
    private readonly object lockObject = new object();
    private readonly int maxSubtrees;

    public SubtreesObservingState(int maxSubtrees)
    {
        this.maxSubtrees = maxSubtrees;
        completed.SetResult(true);
        ObservingSubtrees = new List<ObservingSubtree>();
    }

    /// <summary>
    /// Empty = no one subtree is under observation (initial state)
    /// Null = too much subtrees are under observation, need to get whole tree (terminal state)
    /// </summary>
    [CanBeNull] public List<ObservingSubtree> ObservingSubtrees { get; private set; }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="newSubtree"></param>
    /// <param name="taskCompletionSource">Source to indicate was this subtree downloaded at least once</param>
    /// <returns>Returns True if new subtree was added and need to initiate downloading.</returns>
    public bool TryAddSubtree(ClusterConfigPath newSubtree, out TaskCompletionSource<bool> taskCompletionSource)
    {
        if (ObservingSubtrees == null)
        {
            taskCompletionSource = completed;
            return false;
        }

        if (ObservingSubtrees.Count >= maxSubtrees)
        {
            lock (lockObject)
                ObservingSubtrees = null;

            taskCompletionSource = completed;
            return false;
        }

        //TODO double check с кодом под локом, написать бы его один раз
        foreach (var observingSubtree in ObservingSubtrees)
        {
            if (observingSubtree.Path.IsPrefixOf(newSubtree))
            {
                taskCompletionSource = observingSubtree.CompletionSource;
                return false;
            }
        }

        lock (lockObject)
        {
            return TryRebuildObservingSubtrees(newSubtree, out taskCompletionSource);
        }
    }

    private bool TryRebuildObservingSubtrees(ClusterConfigPath newSubtree, out TaskCompletionSource<bool> taskCompletionSource)
    {
        var cachedObservingSubtrees = ObservingSubtrees;

        foreach (var observingSubtree in ObservingSubtrees)
        {
            if (observingSubtree.Path.IsPrefixOf(newSubtree))
            {
                taskCompletionSource = observingSubtree.CompletionSource;
                return false;
            }
        }
        
        List<int> indicesToRemove = null;
        for (var index = 0; index < cachedObservingSubtrees.Count; index++)
        {
            var observingSubtree = cachedObservingSubtrees[index];
            if (newSubtree.IsPrefixOf(observingSubtree.Path))
            {
                indicesToRemove ??= new List<int>();
                indicesToRemove.Add(index);
            }
        }

        var newSubtrees = new List<ObservingSubtree>(cachedObservingSubtrees.Count - indicesToRemove?.Count ?? 0 + 1);

        var tcs = new TaskCompletionSource<bool>();
        if (indicesToRemove != null)
        {
            var tcsToContinue = new List<TaskCompletionSource<bool>>(indicesToRemove.Count);
            for (var index = 0; index < cachedObservingSubtrees.Count; index++)
            {
                if (!indicesToRemove.Contains(index))
                    newSubtrees.Add(cachedObservingSubtrees[index]);
                else
                    tcsToContinue.Add(cachedObservingSubtrees[index].CompletionSource);
            }

            tcs.Task.ContinueWith(task =>
            {
                var result = task.Result;
                foreach (var taskCompletionSource in tcsToContinue)
                {
                    taskCompletionSource.TrySetResult(result);
                }
            });
        }
        newSubtrees.Add(new ObservingSubtree(newSubtree, tcs, null));

        ObservingSubtrees = newSubtrees;

        taskCompletionSource = tcs;
        return true;
    }
}