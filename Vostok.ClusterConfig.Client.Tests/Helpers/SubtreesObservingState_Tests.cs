using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Vostok.ClusterConfig.Client.Abstractions;

namespace Vostok.ClusterConfig.Client.Tests.Helpers;

[TestFixture]
public class SubtreesObservingState_Tests
{
    private int maxSubtrees;
    private SubtreesObservingState state;

    [SetUp]
    public void SetUp()
    {
        maxSubtrees = 30;
        state = new SubtreesObservingState(maxSubtrees);
    }
    
    [Test]
    public void Single_path_simple_case()
    {
        var path = new ClusterConfigPath("foo/bar");
        
        state.TryAddSubtree(path, out var tcs).Should().BeTrue();
        tcs.Task.IsCompleted.Should().BeFalse();

        var subtreesToRequest = state.GetSubtreesToRequest();
        tcs.Task.IsCompleted.Should().BeFalse();
        subtreesToRequest.Single().Path.Should().Be(path);
        
        state.FinalizeSubtrees(subtreesToRequest, DateTime.UtcNow);
        tcs.Task.IsCompleted.Should().BeTrue();
        tcs.Task.GetAwaiter().GetResult().Should().BeTrue();
    }
    
    [Test]
    public void Sequential_TryAdd_should_work()
    {
        var path = new ClusterConfigPath("foo/bar");
        
        state.TryAddSubtree(path, out var tcs).Should().BeTrue();
        var subtreesToRequest = state.GetSubtreesToRequest();
        state.FinalizeSubtrees(subtreesToRequest, DateTime.UtcNow);
        tcs.Task.IsCompleted.Should().BeTrue();
        tcs.Task.GetAwaiter().GetResult().Should().BeTrue();

        for (var i = 0; i < 10; i++)
        {
            state.TryAddSubtree(path, out tcs).Should().BeFalse();
            tcs.Task.IsCompleted.Should().BeTrue();
            tcs.Task.GetAwaiter().GetResult().Should().BeTrue();
        }
    }
    
    [Test]
    public void TryAddSubtree_adding_a_subtree_of_smth_already_added_should_work()
    {
        var path = new ClusterConfigPath("foo/bar");
        var nestedPath = new ClusterConfigPath("foo/bar/baz");
        
        state.TryAddSubtree(path, out var tcs).Should().BeTrue();
        tcs.Task.IsCompleted.Should().BeFalse();
        state.TryAddSubtree(nestedPath, out var nestedTcs).Should().BeFalse();
        nestedTcs.Task.IsCompleted.Should().BeFalse();
        //(deniaa): Due to current realization they are just the same object it that case.
        ReferenceEquals(tcs, nestedTcs).Should().BeTrue();

        var subtreesToRequest = state.GetSubtreesToRequest();
        tcs.Task.IsCompleted.Should().BeFalse();
        nestedTcs.Task.IsCompleted.Should().BeFalse();
        
        subtreesToRequest.Single().Path.Should().Be(path);
        
        state.FinalizeSubtrees(subtreesToRequest, DateTime.UtcNow);
        
        tcs.Task.IsCompleted.Should().BeTrue();
        nestedTcs.Task.IsCompleted.Should().BeTrue();
    }
    
    [Test]
    public void TryAddSubtree_adding_a_subtree_for_smth_already_added_should_work()
    {
        var path = new ClusterConfigPath("foo/bar");
        var nestedPath = new ClusterConfigPath("foo/bar/baz");
        
        state.TryAddSubtree(nestedPath, out var nestedTcs).Should().BeTrue();
        nestedTcs.Task.IsCompleted.Should().BeFalse();
        state.TryAddSubtree(path, out var tcs).Should().BeTrue();
        tcs.Task.IsCompleted.Should().BeFalse();
        //(deniaa): Due to current realization they are both should stay in state with different TCS's it that case.
        ReferenceEquals(tcs, nestedTcs).Should().BeFalse();

        var subtreesToRequest = state.GetSubtreesToRequest();
        tcs.Task.IsCompleted.Should().BeFalse();
        nestedTcs.Task.IsCompleted.Should().BeFalse();
        
        subtreesToRequest.Single().Path.Should().Be(path);
        
        state.FinalizeSubtrees(subtreesToRequest, DateTime.UtcNow);
        
        tcs.Task.IsCompleted.Should().BeTrue();
        nestedTcs.Task.IsCompleted.Should().BeTrue();
    }
    
    [Test]
    public void Limit_test()
    {
        var tcss = new List<TaskCompletionSource<bool>>();
        const string pathPrefix = "some/prefix/";
        for (var i = 0; i < maxSubtrees; i++)
        {
            var path = new ClusterConfigPath(pathPrefix + i);
            state.TryAddSubtree(path, out var tcs).Should().BeTrue();
            tcs.Task.IsCompleted.Should().BeFalse();
            tcss.Add(tcs);
        }

        //(deniaa): This one should be converted to root (""), because we have reached maxSubtrees limit.
        state.TryAddSubtree("foo/bar", out var rootTcs).Should().BeTrue();
        rootTcs.Task.IsCompleted.Should().BeFalse();
        //(deniaa): As we already have root in the state, all next additions should return false.
        state.TryAddSubtree("foo/baz", out var nextTcs).Should().BeFalse();
        //(deniaa): Due to current realization they are just the same object it that case.
        ReferenceEquals(nextTcs, rootTcs).Should().BeTrue();


        var subtreesToRequest = state.GetSubtreesToRequest();
        subtreesToRequest.Single().Path.Should().Be(new ClusterConfigPath(""));
        
        state.FinalizeSubtrees(subtreesToRequest, DateTime.UtcNow);
        
        rootTcs.Task.IsCompleted.Should().BeTrue();
        foreach (var tcs in tcss)
            tcs.Task.IsCompleted.Should().BeTrue();
    }

    [Test]
    [Repeat(10)]
    public void Some_concurrency_stress_test()
    {
        var ways = new List<ClusterConfigPath>();

        var prefixes = new[] {"x/", "y/", "z/"};
        for (var i = 0; i < prefixes.Length; i++)
        for (var j = 0; j < maxSubtrees / prefixes.Length; j++)
        {
            var path = new ClusterConfigPath(prefixes[i] + i);
            ways.Add(path);
        }
        Shuffle(new Random(), prefixes);

        var tasks = new[]
        {
            Task.Run(() => TryAdd(prefixes, 0, 10)),
            Task.Run(() => TryAdd(prefixes, 10, 10)),
            Task.Run(() => TryAdd(prefixes, 20, 10)),
        };
        Task.WaitAll(tasks);

        var subtreesToRequest = state.GetSubtreesToRequest();
        subtreesToRequest.Count.Should().Be(3);
    }

    private void TryAdd(string[] prefixes, int offset, int length)
    {
        for (var i = offset; i < prefixes.Length && i < offset + length; i++)
        {
            state.TryAddSubtree(new ClusterConfigPath(prefixes[i]), out _);
        }
    }

    private static void Shuffle<T> (Random rng, IList<T> array)
    {
        var n = array.Count;
        while (n > 1) 
        {
            var k = rng.Next(n--);
            var temp = array[n];
            array[n] = array[k];
            array[k] = temp;
        }
    }
}