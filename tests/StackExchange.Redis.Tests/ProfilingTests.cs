﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Profiling;
using Xunit;

namespace StackExchange.Redis.Tests;

[Collection(NonParallelCollection.Name)]
public class ProfilingTests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public async Task Simple()
    {
        await using var conn = Create();

        var server = conn.GetServer(TestConfig.Current.PrimaryServerAndPort);
        var script = LuaScript.Prepare("return redis.call('get', @key)");
        var loaded = script.Load(server);
        var key = Me();

        var session = new ProfilingSession();

        conn.RegisterProfiler(() => session);

        var dbId = TestConfig.GetDedicatedDB(conn);
        var db = conn.GetDatabase(dbId);
        db.StringSet(key, "world");
        var result = db.ScriptEvaluate(script, new { key = (RedisKey)key });
        Assert.NotNull(result);
        Assert.Equal("world", result.AsString());
        var loadedResult = db.ScriptEvaluate(loaded, new { key = (RedisKey)key });
        Assert.NotNull(loadedResult);
        Assert.Equal("world", loadedResult.AsString());
        var val = db.StringGet(key);
        Assert.Equal("world", val);
        var s = (string?)db.Execute("ECHO", "fii");
        Assert.Equal("fii", s);

        var cmds = session.FinishProfiling();
        var evalCmds = cmds.Where(c => c.Command == "EVAL").ToList();
        Assert.Equal(2, evalCmds.Count);
        var i = 0;
        foreach (var cmd in cmds)
        {
            Log($"Command {i++} (DB: {cmd.Db}): {cmd?.ToString()?.Replace("\n", ", ")}");
        }

        var all = string.Join(",", cmds.Select(x => x.Command));
        Assert.Equal("SET,EVAL,EVAL,GET,ECHO", all);
        Log("Checking for SET");
        var set = cmds.SingleOrDefault(cmd => cmd.Command == "SET");
        Assert.NotNull(set);
        Log("Checking for GET");
        var get = cmds.SingleOrDefault(cmd => cmd.Command == "GET");
        Assert.NotNull(get);
        Log("Checking for EVAL");
        var eval1 = evalCmds[0];
        Log("Checking for EVAL");
        var eval2 = evalCmds[1];
        var echo = cmds.SingleOrDefault(cmd => cmd.Command == "ECHO");
        Assert.NotNull(echo);

        Assert.Equal(5, cmds.Count());

        Assert.True(set.CommandCreated <= eval1.CommandCreated);
        Assert.True(eval1.CommandCreated <= eval2.CommandCreated);
        Assert.True(eval2.CommandCreated <= get.CommandCreated);

        AssertProfiledCommandValues(set, conn, dbId);

        AssertProfiledCommandValues(get, conn, dbId);

        AssertProfiledCommandValues(eval1, conn, dbId);

        AssertProfiledCommandValues(eval2, conn, dbId);

        AssertProfiledCommandValues(echo, conn, dbId);
    }

    private static void AssertProfiledCommandValues(IProfiledCommand command, IConnectionMultiplexer conn, int dbId)
    {
        Assert.Equal(dbId, command.Db);
        Assert.Equal(conn.GetEndPoints()[0], command.EndPoint);
        Assert.True(command.CreationToEnqueued > TimeSpan.Zero, nameof(command.CreationToEnqueued));
        Assert.True(command.EnqueuedToSending > TimeSpan.Zero, nameof(command.EnqueuedToSending));
        Assert.True(command.SentToResponse > TimeSpan.Zero, nameof(command.SentToResponse));
        Assert.True(command.ResponseToCompletion >= TimeSpan.Zero, nameof(command.ResponseToCompletion));
        Assert.True(command.ElapsedTime > TimeSpan.Zero, nameof(command.ElapsedTime));
        Assert.True(command.ElapsedTime > command.CreationToEnqueued && command.ElapsedTime > command.EnqueuedToSending && command.ElapsedTime > command.SentToResponse, "Comparisons");
        Assert.True(command.RetransmissionOf == null, nameof(command.RetransmissionOf));
        Assert.True(command.RetransmissionReason == null, nameof(command.RetransmissionReason));
    }

    [Fact]
    public async Task ManyThreads()
    {
        Skip.UnlessLongRunning();
        await using var conn = Create();

        var session = new ProfilingSession();
        var prefix = Me();

        conn.RegisterProfiler(() => session);

        var threads = new List<Thread>();
        const int CountPer = 100;
        for (var i = 1; i <= 16; i++)
        {
            var db = conn.GetDatabase(i);

            threads.Add(new Thread(() =>
            {
                var threadTasks = new List<Task>();

                for (var j = 0; j < CountPer; j++)
                {
                    var task = db.StringSetAsync(prefix + j, "" + j);
                    threadTasks.Add(task);
                }

                Task.WaitAll(threadTasks.ToArray());
            }));
        }

        threads.ForEach(thread => thread.Start());
        threads.ForEach(thread => thread.Join());

        var allVals = session.FinishProfiling();
        var relevant = allVals.Where(cmd => cmd.Db > 0).ToList();

        var kinds = relevant.Select(cmd => cmd.Command).Distinct().ToList();
        foreach (var k in kinds)
        {
            Log("Kind Seen: " + k);
        }
        Assert.True(kinds.Count <= 2);
        Assert.Contains("SET", kinds);
        if (kinds.Count == 2 && !kinds.Contains("SELECT") && !kinds.Contains("GET"))
        {
            Assert.Fail("Non-SET, Non-SELECT, Non-GET command seen");
        }

        Assert.Equal(16 * CountPer, relevant.Count);
        Assert.Equal(16, relevant.Select(cmd => cmd.Db).Distinct().Count());

        for (var i = 1; i <= 16; i++)
        {
            var setsInDb = relevant.Count(cmd => cmd.Db == i);
            Assert.Equal(CountPer, setsInDb);
        }
    }

    [Fact]
    public async Task ManyContexts()
    {
        Skip.UnlessLongRunning();
        await using var conn = Create();

        var profiler = new AsyncLocalProfiler();
        var prefix = Me();
        conn.RegisterProfiler(profiler.GetSession);

        var tasks = new Task[16];

        var results = new ProfiledCommandEnumerable[tasks.Length];

        for (var i = 0; i < tasks.Length; i++)
        {
            var ix = i;
            tasks[ix] = Task.Run(async () =>
            {
                var db = conn.GetDatabase(ix);

                var allTasks = new List<Task>();

                for (var j = 0; j < 1000; j++)
                {
                    var g = db.StringGetAsync(prefix + ix);
                    var s = db.StringSetAsync(prefix + ix, "world" + ix);
                    // overlap the g+s, just for fun
                    await g;
                    await s;
                }

                results[ix] = profiler.GetSession().FinishProfiling();
            });
        }
        Task.WhenAll(tasks).Wait();

        for (var i = 0; i < results.Length; i++)
        {
            var res = results[i];

            var numGets = res.Count(r => r.Command == "GET");
            var numSets = res.Count(r => r.Command == "SET");

            Assert.Equal(1000, numGets);
            Assert.Equal(1000, numSets);
            Assert.True(res.All(cmd => cmd.Db == i));
        }
    }

    internal class PerThreadProfiler
    {
        private readonly ThreadLocal<ProfilingSession> perThreadSession = new ThreadLocal<ProfilingSession>(() => new ProfilingSession());

        public ProfilingSession GetSession() => perThreadSession.Value!;
    }

    internal class AsyncLocalProfiler
    {
        private readonly AsyncLocal<ProfilingSession> perThreadSession = new AsyncLocal<ProfilingSession>();

        public ProfilingSession GetSession()
        {
            var val = perThreadSession.Value;
            if (val == null)
            {
                perThreadSession.Value = val = new ProfilingSession();
            }
            return val;
        }
    }

    [Fact]
    public async Task LowAllocationEnumerable()
    {
        await using var conn = Create();

        const int OuterLoop = 1000;
        var session = new ProfilingSession();
        conn.RegisterProfiler(() => session);

        var prefix = Me();
        var db = conn.GetDatabase(1);

        var allTasks = new List<Task<string?>>();

        foreach (var i in Enumerable.Range(0, OuterLoop))
        {
            var t = db.StringSetAsync(prefix + i, "bar" + i).ContinueWith(async _ => (string?)(await db.StringGetAsync(prefix + i).ForAwait()));

            var finalResult = t.Unwrap();
            allTasks.Add(finalResult);
        }

        conn.WaitAll(allTasks.ToArray());

        var res = session.FinishProfiling();
        Assert.True(res.GetType().IsValueType);

        using (var e = res.GetEnumerator())
        {
            Assert.True(e.GetType().IsValueType);

            Assert.True(e.MoveNext());
            var i = e.Current;

            e.Reset();
            Assert.True(e.MoveNext());
            var j = e.Current;

            Assert.True(ReferenceEquals(i, j));
        }

        Assert.Equal(OuterLoop, res.Count(r => r.Command == "GET" && r.Db > 0));
        Assert.Equal(OuterLoop, res.Count(r => r.Command == "SET" && r.Db > 0));
        Assert.Equal(OuterLoop * 2, res.Count(r => r.Db > 0));
    }

    [Fact]
    public async Task ProfilingMD_Ex1()
    {
        Skip.UnlessLongRunning();
        await using var conn = Create();

        var session = new ProfilingSession();
        var prefix = Me();

        conn.RegisterProfiler(() => session);

        var threads = new List<Thread>();

        for (var i = 0; i < 16; i++)
        {
            var db = conn.GetDatabase(i);

            var thread = new Thread(() =>
            {
                var threadTasks = new List<Task>();

                for (var j = 0; j < 1000; j++)
                {
                    var task = db.StringSetAsync(prefix + j, "" + j);
                    threadTasks.Add(task);
                }

                Task.WaitAll(threadTasks.ToArray());
            });

            threads.Add(thread);
        }

        threads.ForEach(thread => thread.Start());
        threads.ForEach(thread => thread.Join());

        IEnumerable<IProfiledCommand> timings = session.FinishProfiling();

        Assert.Equal(16000, timings.Count());
    }

    [Fact]
    public async Task ProfilingMD_Ex2()
    {
        Skip.UnlessLongRunning();
        await using var conn = Create();

        var profiler = new PerThreadProfiler();
        var prefix = Me();

        conn.RegisterProfiler(profiler.GetSession);

        var threads = new List<Thread>();

        var perThreadTimings = new ConcurrentDictionary<Thread, List<IProfiledCommand>>();

        for (var i = 0; i < 16; i++)
        {
            var db = conn.GetDatabase(i);

            var thread = new Thread(() =>
            {
                var threadTasks = new List<Task>();

                for (var j = 0; j < 1000; j++)
                {
                    var task = db.StringSetAsync(prefix + j, "" + j);
                    threadTasks.Add(task);
                }

                Task.WaitAll(threadTasks.ToArray());

                perThreadTimings[Thread.CurrentThread] = profiler.GetSession().FinishProfiling().ToList();
            });

            threads.Add(thread);
        }

        threads.ForEach(thread => thread.Start());
        threads.ForEach(thread => thread.Join());

        Assert.Equal(16, perThreadTimings.Count);
        Assert.True(perThreadTimings.All(kv => kv.Value.Count == 1000));
    }

    [Fact]
    public async Task ProfilingMD_Ex2_Async()
    {
        Skip.UnlessLongRunning();
        await using var conn = Create();

        var profiler = new AsyncLocalProfiler();
        var prefix = Me();

        conn.RegisterProfiler(profiler.GetSession);

        var tasks = new List<Task>();

        var perThreadTimings = new ConcurrentBag<List<IProfiledCommand>>();

        for (var i = 0; i < 16; i++)
        {
            var db = conn.GetDatabase(i);

            var task = Task.Run(async () =>
            {
                for (var j = 0; j < 100; j++)
                {
                    await db.StringSetAsync(prefix + j, "" + j).ForAwait();
                }

                perThreadTimings.Add(profiler.GetSession().FinishProfiling().ToList());
            });

            tasks.Add(task);
        }

        var timeout = Task.Delay(10000);
        var complete = Task.WhenAll(tasks);
        if (timeout == await Task.WhenAny(timeout, complete).ForAwait())
        {
            throw new TimeoutException();
        }

        Assert.Equal(16, perThreadTimings.Count);
        foreach (var item in perThreadTimings)
        {
            Assert.Equal(100, item.Count);
        }
    }
}
