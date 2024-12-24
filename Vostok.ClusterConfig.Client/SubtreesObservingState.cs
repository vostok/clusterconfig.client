using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.ClusterConfig.Client.Abstractions;
using Vostok.Commons.Helpers.Observable;

namespace Vostok.ClusterConfig.Client;

/// <summary>
/// ObservingSubtrees has an invariants:
/// 1. <see cref="TryAddSubtree"/> adds only to the end, if there is no prefixes of us.
/// 2. if (s[i].IsPrefix(s[j]) && i != j) than i > j
/// 3. if (s[i].IsPrefix(s[j]) && i != j) and we want to set s[i].IsCompleted = true, than s[j] should be removed (without changing order of others)
/// </summary>
internal class SubtreesObservingState
{
    private readonly TaskCompletionSource<bool> completedTaskCompletionSource = new();
    private readonly object lockObject = new();
    private readonly int maxSubtrees;

    private volatile bool cancelled = false;
    
    /// <summary>
    /// Empty = no one subtree is under observation (initial state)
    /// 1+ ObservingSubtrees = common situation (intermediate and normal state) 
    /// Single ObservingSubtree with empty ("") path = downgraded to full tree downloading (terminal state).
    /// </summary>
    [NotNull] private ObservingSubtree[] observingSubtrees;
    
    public SubtreesObservingState(int maxSubtrees)
    {
        this.maxSubtrees = maxSubtrees;
        completedTaskCompletionSource.SetResult(true);
        observingSubtrees = Array.Empty<ObservingSubtree>();
    }

    [NotNull] 
    public List<ObservingSubtree> GetSubtreesToRequest()
    {
        var cachedObservingSubtrees = observingSubtrees;

        var subtrees = new List<ObservingSubtree>(cachedObservingSubtrees.Length);
        //(deniaa): It's better to do it in the opposite direction, if s[i] is a prefix of s[j], then i > j, so it's easier to not add than to remove.
        for (var i = cachedObservingSubtrees.Length - 1; i >= 0; i--)
        {
            var toAdd = cachedObservingSubtrees[i];
            if (!AlreadyHavePrefix(subtrees, toAdd))
                subtrees.Add(toAdd);
        }

        return subtrees;
    }

    /// <param name="newSubtree">Path to subtree.</param>
    /// <param name="taskCompletionSource">Source to indicate was this subtree downloaded at least once</param>
    /// <param name="stateObservable">Is null if State is cancelled</param>
    /// <returns>Returns True if new subtree was added and need to initiate downloading.</returns>
    public bool TryAddSubtree(
        ClusterConfigPath newSubtree, 
        out TaskCompletionSource<bool> taskCompletionSource,
        out CachingObservable<ClusterConfigClientState> stateObservable)
    {
        if (cancelled)
        {
            taskCompletionSource = completedTaskCompletionSource;
            stateObservable = null;
            return false;
        }

        var cachedObservingSubtrees = observingSubtrees;

        if (AlreadyUnderObservation(cachedObservingSubtrees, newSubtree, out taskCompletionSource, out stateObservable))
            return false;
        
        return TryAddObservingSubtrees(newSubtree, out taskCompletionSource, out stateObservable);
    }

    public void FinalizeSubtrees(
        IEnumerable<ObservingSubtree> observingSubtreesToFinalize,
        DateTime? dateTime,
        Task<CachingObservable<ClusterConfigClientState>> rootObservablePropagationTask,
        CancellationToken cancellationToken)
    {
        var finalizationAction = new Action<ObservingSubtree>(subtreeToFinalize =>
        {
            subtreeToFinalize.FinalizeSubtree(rootObservablePropagationTask, cancellationToken);
        });

        foreach (var subtreeToFinalize in observingSubtreesToFinalize)
        {
            subtreeToFinalize.LastVersion = dateTime;
            
            if (subtreeToFinalize.IsFinalized())
                continue;

            finalizationAction(subtreeToFinalize);

            CleanupLeafSubtrees(subtreeToFinalize, finalizationAction);
        }
    }

    public void FailUnfinalizedSubtrees(
        IEnumerable<ObservingSubtree> observingSubtreesToFinalize,
        Exception error,
        Task propagationTask)
    {        
        var finalizationAction = new Action<ObservingSubtree>(subtreeToFinalize =>
        {
            subtreeToFinalize.FailUnfinalizedSubtree(error, false, propagationTask);
        });
        
        foreach (var subtreeToFinalize in observingSubtreesToFinalize)
        {
            if (subtreeToFinalize.IsFinalized())
                continue;

            finalizationAction(subtreeToFinalize);

            CleanupLeafSubtrees(subtreeToFinalize, finalizationAction);
        }
    }

    private static bool AlreadyHavePrefix(IEnumerable<ObservingSubtree> subtrees, ObservingSubtree toAdd)
    {
        foreach (var subtree in subtrees)
            if (subtree.Path.IsPrefixOf(toAdd.Path))
                return true;

        return false;
    }

    private bool TryAddObservingSubtrees(
        ClusterConfigPath newSubtree, 
        out TaskCompletionSource<bool> taskCompletionSource, 
        out CachingObservable<ClusterConfigClientState> stateObservable)
    {
        lock (lockObject)
        {
            taskCompletionSource = completedTaskCompletionSource;
            stateObservable = null;
            
            var cachedObservingSubtrees = observingSubtrees;

            //(deniaa): It's important to double-check this
            if (cancelled)
            {
                taskCompletionSource = completedTaskCompletionSource;
                return false;
            }
            //(deniaa): It's important to double-check this
            if (AlreadyUnderObservation(cachedObservingSubtrees, newSubtree, out taskCompletionSource, out stateObservable))
                return false;
            
            //(deniaa): Yes, if we add /a/a/a, then /a/a, then /a, after finalization phase we will have only one /a path.
            //(deniaa): And here we can harry to set the root here.
            if (cachedObservingSubtrees.Length >= maxSubtrees)
            {
                newSubtree = "";
            }
            
            var newSubtrees = new ObservingSubtree[cachedObservingSubtrees.Length + 1];
            Array.Copy(cachedObservingSubtrees, newSubtrees, cachedObservingSubtrees.Length);
            var newObservingSubtree = new ObservingSubtree(newSubtree);
            newSubtrees[cachedObservingSubtrees.Length] = newObservingSubtree;
            
            observingSubtrees = newSubtrees;

            taskCompletionSource = newObservingSubtree.GetAtLeastOnceObtainingTaskCompletionSource();
            stateObservable = newObservingSubtree.SubtreeStateObservable;
            return true;
        }
    }

    private void CleanupLeafSubtrees(ObservingSubtree finalizedSubtree, Action<ObservingSubtree> finalize)
    {
        //(deniaa): No LINQ code under lock please!
        lock (lockObject)
        {
            var cachedObservingSubtrees = observingSubtrees;
            
            //(deniaa): Delete those subtrees that are to the left of us, and we are a prefix for them. 
            HashSet<ObservingSubtree> subtreesToRemove = null;
            foreach (var observingSubtree in cachedObservingSubtrees)
            {
                //(deniaa): If we reach our node, not need to continue. We can't be a prefix for something at the right of us. 
                if (ReferenceEquals(observingSubtree, finalizedSubtree))
                    break;

                if (finalizedSubtree.Path.IsPrefixOf(observingSubtree.Path))
                {
                    subtreesToRemove ??= new HashSet<ObservingSubtree>();
                    subtreesToRemove.Add(observingSubtree);
                }
            }

            //(deniaa): Nothing to remove, ObservingSubtrees list has no elements which are prefixes of another.
            if (subtreesToRemove == null)
                return;

            var newSubtrees = new ObservingSubtree[cachedObservingSubtrees.Length - subtreesToRemove.Count];

            var index = 0;
            foreach (var observingSubtree in cachedObservingSubtrees)
            {
                if (!subtreesToRemove.Contains(observingSubtree))
                {
                    newSubtrees[index] = observingSubtree;
                    index++;
                }
            }

            observingSubtrees = newSubtrees;
            //(deniaa): Since we have already finalized our subtree of all these nested sub-subtrees and removed all these sub-subtrees,
            //(deniaa) we have to set their token to unlock waiters.  
            foreach (var removedSubtree in subtreesToRemove)
                finalize(removedSubtree);
        }
    }

    private static bool AlreadyUnderObservation(
        [NotNull] ObservingSubtree[] observingSubtrees, 
        ClusterConfigPath newSubtree, 
        out TaskCompletionSource<bool> taskCompletionSource,
        out CachingObservable<ClusterConfigClientState> stateObservable)
    {
        taskCompletionSource = null;
        stateObservable = null;
        
        foreach (var observingSubtree in observingSubtrees)
        {
            if (!observingSubtree.Path.IsPrefixOf(newSubtree))
                continue;
            taskCompletionSource = observingSubtree.GetAtLeastOnceObtainingTaskCompletionSource();
            stateObservable = observingSubtree.SubtreeStateObservable;
            return true;
        }

        return false;
    }

    public void Cancel(ObjectDisposedException error, bool alwaysPushToObservable, Task propagationTask)
    {
        lock (lockObject)
        {
            cancelled = true;
            
            foreach (var observingSubtree in observingSubtrees)
            {
                observingSubtree.FailUnfinalizedSubtree(error, alwaysPushToObservable, propagationTask);
            }
        }
    }
}