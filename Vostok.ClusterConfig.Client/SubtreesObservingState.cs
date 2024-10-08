using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.ClusterConfig.Client.Abstractions;

namespace Vostok.ClusterConfig.Client;

/// <summary>
/// ObservingSubtrees has an invariants:
/// 1. <see cref="TryAddSubtree"/> adds only to the end, if there is no prefixes of us.
/// 2. if (s[i].IsPrefix(s[j]) && i != j) than i > j
/// 3. if (s[i].IsPrefix(s[j]) && i != j) and we want to set s[i].IsCompleted = true, than s[j] should be removed (without changing order of others)
/// </summary>
internal class SubtreesObservingState
{
    private readonly TaskCompletionSource<bool> completed = new();
    private readonly object lockObject = new();
    private readonly int maxSubtrees;
    
    //TODO актуализировать доку, когда разберемся с nullы 
    /// <summary>
    /// Empty = no one subtree is under observation (initial state)
    /// Null = too much subtrees are under observation, need to get whole tree (terminal state)
    /// </summary>
    private List<ObservingSubtree> observingSubtrees;
    
    public SubtreesObservingState(int maxSubtrees)
    {
        this.maxSubtrees = maxSubtrees;
        completed.SetResult(true);
        observingSubtrees = new List<ObservingSubtree>();
    }

    public List<ObservingSubtree> GetSubtreesToRequest()
    {
        var cachedObservingSubtrees = observingSubtrees;
        //TODO убрать, если будем класть корень сюда же
        if (cachedObservingSubtrees == null)
            return null;

        var subtrees = new List<ObservingSubtree>(cachedObservingSubtrees.Count);
        //(deniaa): It's better to do it in the opposite direction, if s[i] is a prefix of s[j], then i > j, so it's easier to not add than to remove.
        for (var i = cachedObservingSubtrees.Count - 1; i >= 0; i++)
        {
            var toAdd = cachedObservingSubtrees[i];
            if (!AlreadyHavePrefix(subtrees, toAdd))
                subtrees.Add(toAdd);
        }

        return subtrees;
    }

    private static bool AlreadyHavePrefix(List<ObservingSubtree> subtrees, ObservingSubtree toAdd)
    {
        foreach (var subtree in subtrees)
            if (subtree.Path.IsPrefixOf(toAdd.Path))
                return true;

        return false;
    }

    /// <param name="newSubtree"></param>
    /// <param name="taskCompletionSource">Source to indicate was this subtree downloaded at least once</param>
    /// <returns>Returns True if new subtree was added and need to initiate downloading.</returns>
    public bool TryAddSubtree(ClusterConfigPath newSubtree, out TaskCompletionSource<bool> taskCompletionSource)
    {
        var cachedObservingSubtrees = observingSubtrees;
        //TODO убрать, если будем класть корень сюда же
        if (cachedObservingSubtrees == null)
        {
            taskCompletionSource = completed;
            return false;
        }

        if (AlreadyUnderObservation(cachedObservingSubtrees, newSubtree, out taskCompletionSource))
            return false;
        
        return TryAddObservingSubtrees(newSubtree, out taskCompletionSource);
    }

    private bool TryAddObservingSubtrees(ClusterConfigPath newSubtree, out TaskCompletionSource<bool> taskCompletionSource)
    {
        lock (lockObject)
        {
            taskCompletionSource = completed;
            var cachedObservingSubtrees = observingSubtrees;
            //TODO убрать, если будем класть корень сюда же
            if (cachedObservingSubtrees == null)
                return false;

            //(deniaa): It's important to double check it
            if (AlreadyUnderObservation(cachedObservingSubtrees, newSubtree, out taskCompletionSource))
                return false;

            //(deniaa): Here we can add a bit more prefixes, because some of them we can remove in finalization phase.
            if (cachedObservingSubtrees.Count > maxSubtrees * 2)
            {
                return AddRootOrSetNull(out taskCompletionSource);
            }
            
            var newSubtrees = new List<ObservingSubtree>(cachedObservingSubtrees.Count + 1);
            newSubtrees.AddRange(cachedObservingSubtrees);
            var newObservingSubtree = new ObservingSubtree(newSubtree);
            newSubtrees.Add(newObservingSubtree);
            
            observingSubtrees = newSubtrees;

            taskCompletionSource = newObservingSubtree.AtLeastOnceObtaining;
            return true;
        }
    }

    private bool AddRootOrSetNull(out TaskCompletionSource<bool> taskCompletionSource)
    {
        //TODO Переделать на добавление корня, если выберем такой путь.
        taskCompletionSource = completed;
        return false;
    }

    public void FinalizeSubtrees(List<ObservingSubtree> observingSubtreesToFinalize, DateTime dateTime)
    {
        foreach (var subtreeToFinalize in observingSubtreesToFinalize)
        {
            subtreeToFinalize.LastVersion = dateTime;
            
            if (subtreeToFinalize.AtLeastOnceObtaining.Task.IsCompleted)
                continue;

            subtreeToFinalize.AtLeastOnceObtaining.TrySetResult(true);

            CleanupLeafSubtrees(subtreeToFinalize);
        }
        
        var cachedObservingSubtrees = observingSubtrees;
        if (cachedObservingSubtrees != null && cachedObservingSubtrees.Count > maxSubtrees)
        {
            //TODO Add root, or set null to ObservingSubtrees!
        }
    }

    private void CleanupLeafSubtrees(ObservingSubtree finalizedSubtree)
    {
        //(deniaa): No LINQ code under lock please!
        lock (lockObject)
        {
            var cachedObservingSubtrees = observingSubtrees;
            //TODO если тут null, то можно ничего не делать, потому что мы откатились к полному дерево
            //TODO но если полное дерево будем хранить здесь же, то null не возможен.
            if (cachedObservingSubtrees == null)
                return;
            
            //(deniaa): Delete those subtrees that are to the left of us and we are a prefix for them. 
            HashSet<ObservingSubtree> subtreesToRemove = null;
            foreach (var observingSubtree in cachedObservingSubtrees)
            {
                //(deniaa): If we reach our node, not need to continue. We can't be prefix for something at the right of us. 
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

            var newSubtrees = new List<ObservingSubtree>(cachedObservingSubtrees.Count - subtreesToRemove.Count);

            foreach (var observingSubtree in cachedObservingSubtrees)
            {
                if (!subtreesToRemove.Contains(observingSubtree))
                    newSubtrees.Add(observingSubtree);
            }

            observingSubtrees = newSubtrees;
        }
    }

    private static bool AlreadyUnderObservation([NotNull] List<ObservingSubtree> observingSubtrees, ClusterConfigPath newSubtree, out TaskCompletionSource<bool> taskCompletionSource)
    {
        taskCompletionSource = null;
        
        foreach (var observingSubtree in observingSubtrees)
        {
            if (!observingSubtree.Path.IsPrefixOf(newSubtree))
                continue;
            taskCompletionSource = observingSubtree.AtLeastOnceObtaining;
            return true;
        }

        return false;
    }
}