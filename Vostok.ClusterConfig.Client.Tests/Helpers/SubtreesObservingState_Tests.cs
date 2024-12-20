using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FluentAssertions.Extensions;
using NUnit.Framework;
using Vostok.ClusterConfig.Client.Abstractions;
using Vostok.Commons.Collections;
using Vostok.Commons.Helpers.Observable;
using Vostok.Commons.Testing;
using Vostok.Commons.Testing.Observable;
using Vostok.Configuration.Abstractions.SettingsTree;

namespace Vostok.ClusterConfig.Client.Tests.Helpers;

[TestFixture]
public class SubtreesObservingState_Tests
{
    private int maxSubtrees;
    private SubtreesObservingState state;
    private CachingObservable<ClusterConfigClientState> observable;
    private ClusterConfigClientState result;

    [SetUp]
    public void SetUp()
    {
        maxSubtrees = 30;
        state = new SubtreesObservingState(maxSubtrees);
        result = new ClusterConfigClientState(null, null, new RecyclingBoundedCache<ClusterConfigPath, ISettingsNode>(3), 44);
        observable = new CachingObservable<ClusterConfigClientState>();
        observable.Next(result);
    }
    
    [Test]
    public void Single_path_simple_case()
    {
        var path = new ClusterConfigPath("foo/bar");
        
        state.TryAddSubtree(path, out var tcs, out var cachingObservable).Should().BeTrue();
        tcs.Task.IsCompleted.Should().BeFalse();

        var subtreesToRequest = state.GetSubtreesToRequest();
        tcs.Task.IsCompleted.Should().BeFalse();
        subtreesToRequest.Single().Path.Should().Be(path);

        var obs = new TestObserver<ClusterConfigClientState>();
        cachingObservable.Subscribe(obs);
        new Action(() => obs.Values.Should().BeEmpty()).ShouldNotFailIn(5.Seconds());
        
        state.FinalizeSubtrees(subtreesToRequest, DateTime.UtcNow, Task.FromResult(observable), CancellationToken.None);
        tcs.Task.IsCompleted.Should().BeTrue();
        tcs.Task.GetAwaiter().GetResult().Should().BeTrue();
        new Action(() =>
        {
            obs.Values.Should().HaveCount(1);
            obs.Values.Single().Should().BeSameAs(result);
        }).ShouldPassIn(10.Seconds());
    }
    
    [Test]
    public void Sequential_TryAdd_should_work()
    {
        var path = new ClusterConfigPath("foo/bar");
        
        state.TryAddSubtree(path, out var tcs, out _).Should().BeTrue();
        var subtreesToRequest = state.GetSubtreesToRequest();
        state.FinalizeSubtrees(subtreesToRequest, DateTime.UtcNow, Task.FromResult(observable), CancellationToken.None);
        tcs.Task.IsCompleted.Should().BeTrue();
        tcs.Task.GetAwaiter().GetResult().Should().BeTrue();

        for (var i = 0; i < 10; i++)
        {
            state.TryAddSubtree(path, out tcs, out var newCachingObservable).Should().BeFalse();
            tcs.Task.IsCompleted.Should().BeTrue();
            tcs.Task.GetAwaiter().GetResult().Should().BeTrue();
            var obs = new TestObserver<ClusterConfigClientState>();
            newCachingObservable.Subscribe(obs);
            new Action(() =>
            {
                obs.Values.Should().HaveCount(1);
                obs.Values.Single().Should().BeSameAs(result);
            }).ShouldPassIn(10.Seconds());
        }
    }
    
    [Test]
    public void TryAddSubtree_adding_a_subtree_of_smth_already_added_should_work()
    {
        var path = new ClusterConfigPath("foo/bar");
        var nestedPath = new ClusterConfigPath("foo/bar/baz");
        
        state.TryAddSubtree(path, out var tcs, out var cachingObservable).Should().BeTrue();
        tcs.Task.IsCompleted.Should().BeFalse();
        state.TryAddSubtree(nestedPath, out var nestedTcs, out var nestedCachingObservable).Should().BeFalse();
        nestedTcs.Task.IsCompleted.Should().BeFalse();
        //(deniaa): Due to current realization they are just the same object it that case.
        tcs.Should().BeSameAs(nestedTcs);
        cachingObservable.Should().BeSameAs(nestedCachingObservable);

        var subtreesToRequest = state.GetSubtreesToRequest();
        tcs.Task.IsCompleted.Should().BeFalse();
        nestedTcs.Task.IsCompleted.Should().BeFalse();
        
        subtreesToRequest.Single().Path.Should().Be(path);
        
        state.FinalizeSubtrees(subtreesToRequest, DateTime.UtcNow, Task.FromResult(observable), CancellationToken.None);
        
        tcs.Task.IsCompleted.Should().BeTrue();
        nestedTcs.Task.IsCompleted.Should().BeTrue();
        var obs = new TestObserver<ClusterConfigClientState>();
        cachingObservable.Subscribe(obs);
        var nestedObs = new TestObserver<ClusterConfigClientState>();
        nestedCachingObservable.Subscribe(nestedObs);
        new Action(() =>
        {
            obs.Values.Should().HaveCount(1);
            obs.Values.Single().Should().BeSameAs(result);
            
            nestedObs.Values.Should().HaveCount(1);
            nestedObs.Values.Single().Should().BeSameAs(result);
        }).ShouldPassIn(10.Seconds());
    }
    
    [Test]
    public void TryAddSubtree_adding_a_subtree_for_smth_already_added_should_work()
    {
        var path = new ClusterConfigPath("foo/bar");
        var nestedPath = new ClusterConfigPath("foo/bar/baz");
        
        state.TryAddSubtree(nestedPath, out var nestedTcs, out var nestedCachingObservable).Should().BeTrue();
        nestedTcs.Task.IsCompleted.Should().BeFalse();
        state.TryAddSubtree(path, out var tcs, out var cachingObservable).Should().BeTrue();
        tcs.Task.IsCompleted.Should().BeFalse();
        //(deniaa): Due to current realization they are both should stay in state with different TCS's it that case.
        tcs.Should().NotBeSameAs(nestedTcs);
        nestedCachingObservable.Should().NotBeSameAs(cachingObservable);

        var subtreesToRequest = state.GetSubtreesToRequest();
        tcs.Task.IsCompleted.Should().BeFalse();
        nestedTcs.Task.IsCompleted.Should().BeFalse();
        
        subtreesToRequest.Single().Path.Should().Be(path);
        
        state.FinalizeSubtrees(subtreesToRequest, DateTime.UtcNow, Task.FromResult(observable), CancellationToken.None);
        
        tcs.Task.IsCompleted.Should().BeTrue();
        nestedTcs.Task.IsCompleted.Should().BeTrue();

        var obs = new TestObserver<ClusterConfigClientState>();
        cachingObservable.Subscribe(obs);
        var nestedObs = new TestObserver<ClusterConfigClientState>();
        nestedCachingObservable.Subscribe(nestedObs);
        new Action(() =>
        {
            obs.Values.Should().HaveCount(1);
            obs.Values.Single().Should().BeSameAs(result);
            
            nestedObs.Values.Should().HaveCount(1);
            nestedObs.Values.Single().Should().BeSameAs(result);
        }).ShouldPassIn(10.Seconds());
    }
    
    [Test]
    public void Limit_test()
    {
        var tcss = new List<TaskCompletionSource<bool>>();
        const string pathPrefix = "some/prefix/";
        for (var i = 0; i < maxSubtrees; i++)
        {
            var path = new ClusterConfigPath(pathPrefix + i);
            state.TryAddSubtree(path, out var tcs, out _).Should().BeTrue();
            tcs.Task.IsCompleted.Should().BeFalse();
            tcss.Add(tcs);
        }

        //(deniaa): This one should be converted to root (""), because we have reached maxSubtrees limit.
        state.TryAddSubtree("foo/bar", out var rootTcs, out _).Should().BeTrue();
        rootTcs.Task.IsCompleted.Should().BeFalse();
        //(deniaa): As we already have root in the state, all next additions should return false.
        state.TryAddSubtree("foo/baz", out var nextTcs, out _).Should().BeFalse();
        //(deniaa): Due to current realization they are just the same object it that case.
        nextTcs.Should().BeSameAs(rootTcs);


        var subtreesToRequest = state.GetSubtreesToRequest();
        subtreesToRequest.Single().Path.Should().Be(new ClusterConfigPath(""));
        
        state.FinalizeSubtrees(subtreesToRequest, DateTime.UtcNow, Task.FromResult(observable), CancellationToken.None);
        
        rootTcs.Task.IsCompleted.Should().BeTrue();
        foreach (var tcs in tcss)
            tcs.Task.IsCompleted.Should().BeTrue();
    }

    [Test]
    public void Observable_from_dislocated_element_should_work()
    {
        state.TryAddSubtree("foo/bar", out _, out var subtreeCachingObservable).Should().BeTrue();
        var subtreesToRequest = state.GetSubtreesToRequest();
        state.FinalizeSubtrees(subtreesToRequest, DateTime.UtcNow, Task.FromResult(observable), CancellationToken.None);
        
        var subtreeObs = new TestObserver<ClusterConfigClientState>();
        subtreeCachingObservable.Subscribe(subtreeObs);
        new Action(() =>
        {
            subtreeObs.Values.Should().HaveCount(1);
            subtreeObs.Values.Single().Should().BeSameAs(result);
        }).ShouldPassIn(10.Seconds());
        
        state.TryAddSubtree("foo", out _, out var rootCachingObservable).Should().BeTrue();
        subtreesToRequest = state.GetSubtreesToRequest();
        subtreesToRequest.Should().HaveCount(1);
        subtreesToRequest.Single().Path.Equivalent(new ClusterConfigPath("foo")).Should().BeTrue();
        
        state.FinalizeSubtrees(subtreesToRequest, DateTime.UtcNow, Task.FromResult(observable), CancellationToken.None);

        var newState = new ClusterConfigClientState(null, null, new RecyclingBoundedCache<ClusterConfigPath, ISettingsNode>(11), 77);
        observable.Next(newState);
        
        var rootObs = new TestObserver<ClusterConfigClientState>();
        rootCachingObservable.Subscribe(rootObs);
        new Action(() =>
        {
            subtreeObs.Values.Should().HaveCount(2);
            subtreeObs.Values.LastOrDefault().Should().BeSameAs(newState);
            
            rootObs.Values.Should().HaveCount(1);
            rootObs.Values.Single().Should().BeSameAs(newState);
        }).ShouldPassIn(10.Seconds());
    }

    [Test]
    public void Fail_tests()
    {
        state.TryAddSubtree("foo/bar", out _, out var barCachingObservable).Should().BeTrue();
        var subtreesToRequest = state.GetSubtreesToRequest();
        state.FinalizeSubtrees(subtreesToRequest, DateTime.UtcNow, Task.FromResult(observable), CancellationToken.None);
        
        var barObs = new TestObserver<ClusterConfigClientState>();
        barCachingObservable.Subscribe(barObs);
        new Action(() =>
        {
            barObs.Values.Should().HaveCount(1);
            barObs.Values.Single().Should().BeSameAs(result);
        }).ShouldPassIn(10.Seconds());
        
        
        state.TryAddSubtree("foo/baz", out var bazTcs, out var bazCachingObservable).Should().BeTrue();
        subtreesToRequest = state.GetSubtreesToRequest();
        state.FailUnfinalizedSubtrees(subtreesToRequest, false, new Exception(), Task.CompletedTask);
        bazTcs.Task.IsFaulted.Should().BeTrue();
        
        var bazObs = new TestObserver<ClusterConfigClientState>();
        bazCachingObservable.Subscribe(bazObs);
        new Action(() =>
        {
            bazObs.Messages.Should().HaveCount(1);
            bazObs.Messages.Single().Kind.Should().Be(NotificationKind.OnError);
            bazObs.Messages.Single().Exception.Should().NotBeNull();
            bazObs.Messages.Single().HasValue.Should().BeFalse();
        }).ShouldPassIn(10.Seconds());
        

        var newSubtreesToRequest = state.GetSubtreesToRequest();
        newSubtreesToRequest.Should().BeEquivalentTo(subtreesToRequest);
        
        var newState = new ClusterConfigClientState(null, null, new RecyclingBoundedCache<ClusterConfigPath, ISettingsNode>(11), 77);
        observable.Next(newState);
        state.FinalizeSubtrees(newSubtreesToRequest, DateTime.UtcNow, Task.FromResult(observable), CancellationToken.None);
        

        new Action(() =>
        {
            state.TryAddSubtree("foo/baz", out var newBazTcs, out var newBazCachingObservable).Should().BeFalse();
            newBazTcs.Task.IsCompleted.Should().BeTrue();
            var newBazObs = new TestObserver<ClusterConfigClientState>();
            newBazCachingObservable.Subscribe(newBazObs);
            
            newBazObs.Messages.Should().HaveCount(1);
            newBazObs.Messages.Single().Kind.Should().Be(NotificationKind.OnNext);
            newBazObs.Values.Should().HaveCount(1);
            newBazObs.Values.Single().Should().BeSameAs(newState);
        }).ShouldPassIn(10.Seconds());
    }

    [Test]
    public void Sequential_finalize_should_subscribe_observer_only_once()
    {        
        state.TryAddSubtree("foo/bar", out _, out var subtreeCachingObservable).Should().BeTrue();
        var subtreeObs = new TestObserver<ClusterConfigClientState>();
        subtreeCachingObservable.Subscribe(subtreeObs);

        for (var i = 0; i < 10; i++)
        {
            var subtreesToRequest = state.GetSubtreesToRequest();
            state.FinalizeSubtrees(subtreesToRequest, DateTime.UtcNow, Task.FromResult(observable), CancellationToken.None);
        }

        new Action(() => subtreeObs.Values.Should().HaveCount(1)).ShouldPassIn(10.Seconds());
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

    [Test]
    public void Observable_should_become_cancelled_if_Finalize_with_cancelled_root_obs()
    {
        state.TryAddSubtree("foo/bar", out _, out var subtreeCachingObservable).Should().BeTrue();
        var subtreesToRequest = state.GetSubtreesToRequest();
        var failedObs = new CachingObservable<ClusterConfigClientState>();
        failedObs.Error(new ObjectDisposedException("blah"));
        state.FinalizeSubtrees(subtreesToRequest, DateTime.UtcNow, Task.FromResult(failedObs), CancellationToken.None);
        
        var subtreeObs = new TestObserver<ClusterConfigClientState>();
        subtreeCachingObservable.Subscribe(subtreeObs);
        new Action(() =>
        {
            subtreeObs.Messages.Should().HaveCount(1);
            subtreeObs.Messages.Single().Kind.Should().Be(NotificationKind.OnError);
        }).ShouldPassIn(10.Seconds());    
    }

    private void TryAdd(string[] prefixes, int offset, int length)
    {
        for (var i = offset; i < prefixes.Length && i < offset + length; i++)
        {
            state.TryAddSubtree(new ClusterConfigPath(prefixes[i]), out _, out _);
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