using System;
using System.Linq;
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
        
        state.TryAddSubtree(path, out var cts).Should().BeTrue();
        cts.Task.IsCompleted.Should().BeFalse();

        var subtreesToRequest = state.GetSubtreesToRequest();
        cts.Task.IsCompleted.Should().BeFalse();
        subtreesToRequest.Single().Path.Should().Be(path);
        
        state.FinalizeSubtrees(subtreesToRequest, DateTime.UtcNow);
        cts.Task.IsCompleted.Should().BeTrue();
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
}