﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Maintenance;
using StackExchange.Redis.Profiling;
using Xunit;

[assembly: AssemblyFixture(typeof(StackExchange.Redis.Tests.SharedConnectionFixture))]

namespace StackExchange.Redis.Tests;

public class SharedConnectionFixture : IDisposable
{
    public bool IsEnabled { get; }

    private readonly ConnectionMultiplexer _actualConnection;
    public string Configuration { get; }

    public SharedConnectionFixture()
    {
        IsEnabled = TestConfig.Current.UseSharedConnection;
        Configuration = TestBase.GetDefaultConfiguration();
        _actualConnection = TestBase.CreateDefault(
            output: null,
            clientName: nameof(SharedConnectionFixture),
            configuration: Configuration,
            allowAdmin: true);
        _actualConnection.InternalError += OnInternalError;
        _actualConnection.ConnectionFailed += OnConnectionFailed;
    }

    private NonDisposingConnection? resp2, resp3;
    internal IInternalConnectionMultiplexer GetConnection(TestBase obj, RedisProtocol protocol, [CallerMemberName] string caller = "")
    {
        Version? require = protocol == RedisProtocol.Resp3 ? RedisFeatures.v6_0_0 : null;
        lock (this)
        {
            ref NonDisposingConnection? field = ref protocol == RedisProtocol.Resp3 ? ref resp3 : ref resp2;
            if (field is { IsConnected: false })
            {
                // abandon memoized connection if disconnected
                var muxer = field.UnderlyingMultiplexer;
                field = null;
                muxer.Dispose();
            }
            return field ??= VerifyAndWrap(obj.Create(protocol: protocol, require: require, caller: caller, shared: false, allowAdmin: true), protocol);
        }

        static NonDisposingConnection VerifyAndWrap(IInternalConnectionMultiplexer muxer, RedisProtocol protocol)
        {
            var ep = muxer.GetEndPoints().FirstOrDefault();
            Assert.NotNull(ep);
            var server = muxer.GetServer(ep);
            server.Ping();
            var sep = muxer.GetServerEndPoint(ep);
            if (sep.Protocol is null)
            {
                throw new InvalidOperationException("No RESP protocol; this means no connection?");
            }
            Assert.Equal(protocol, sep.Protocol);
            Assert.Equal(protocol, server.Protocol);
            return new NonDisposingConnection(muxer);
        }
    }

    internal sealed class NonDisposingConnection(IInternalConnectionMultiplexer inner) : IInternalConnectionMultiplexer
    {
        public IInternalConnectionMultiplexer UnderlyingConnection => _inner;

        public bool AllowConnect
        {
            get => _inner.AllowConnect;
            set => _inner.AllowConnect = value;
        }

        public bool IgnoreConnect
        {
            get => _inner.IgnoreConnect;
            set => _inner.IgnoreConnect = value;
        }

        public ServerSelectionStrategy ServerSelectionStrategy => _inner.ServerSelectionStrategy;

        public ServerEndPoint GetServerEndPoint(EndPoint endpoint) => _inner.GetServerEndPoint(endpoint);

        public ReadOnlySpan<ServerEndPoint> GetServerSnapshot() => _inner.GetServerSnapshot();

        public ConnectionMultiplexer UnderlyingMultiplexer => _inner.UnderlyingMultiplexer;

        private readonly IInternalConnectionMultiplexer _inner = inner;

        public int GetSubscriptionsCount() => _inner.GetSubscriptionsCount();
        public ConcurrentDictionary<RedisChannel, ConnectionMultiplexer.Subscription> GetSubscriptions() => _inner.GetSubscriptions();

        public void AddLibraryNameSuffix(string suffix) => _inner.AddLibraryNameSuffix(suffix);

        public string ClientName => _inner.ClientName;

        public string Configuration => _inner.Configuration;

        public int TimeoutMilliseconds => _inner.TimeoutMilliseconds;

        public long OperationCount => _inner.OperationCount;

#pragma warning disable CS0618 // Type or member is obsolete
        public bool PreserveAsyncOrder { get => false; set { } }
#pragma warning restore CS0618

        public bool IsConnected => _inner.IsConnected;

        public bool IsConnecting => _inner.IsConnecting;

        public ConfigurationOptions RawConfig => _inner.RawConfig;

        public bool IncludeDetailInExceptions { get => _inner.RawConfig.IncludeDetailInExceptions; set => _inner.RawConfig.IncludeDetailInExceptions = value; }

        public int StormLogThreshold { get => _inner.StormLogThreshold; set => _inner.StormLogThreshold = value; }

        public event EventHandler<RedisErrorEventArgs> ErrorMessage
        {
            add => _inner.ErrorMessage += value;
            remove => _inner.ErrorMessage -= value;
        }

        public event EventHandler<ConnectionFailedEventArgs> ConnectionFailed
        {
            add => _inner.ConnectionFailed += value;
            remove => _inner.ConnectionFailed -= value;
        }

        public event EventHandler<InternalErrorEventArgs> InternalError
        {
            add => _inner.InternalError += value;
            remove => _inner.InternalError -= value;
        }

        public event EventHandler<ConnectionFailedEventArgs> ConnectionRestored
        {
            add => _inner.ConnectionRestored += value;
            remove => _inner.ConnectionRestored -= value;
        }

        public event EventHandler<EndPointEventArgs> ConfigurationChanged
        {
            add => _inner.ConfigurationChanged += value;
            remove => _inner.ConfigurationChanged -= value;
        }

        public event EventHandler<EndPointEventArgs> ConfigurationChangedBroadcast
        {
            add => _inner.ConfigurationChangedBroadcast += value;
            remove => _inner.ConfigurationChangedBroadcast -= value;
        }

        public event EventHandler<HashSlotMovedEventArgs> HashSlotMoved
        {
            add => _inner.HashSlotMoved += value;
            remove => _inner.HashSlotMoved -= value;
        }

        public event EventHandler<ServerMaintenanceEvent> ServerMaintenanceEvent
        {
            add => _inner.ServerMaintenanceEvent += value;
            remove => _inner.ServerMaintenanceEvent -= value;
        }

        public void Close(bool allowCommandsToComplete = true) => _inner.Close(allowCommandsToComplete);

        public Task CloseAsync(bool allowCommandsToComplete = true) => _inner.CloseAsync(allowCommandsToComplete);

        public bool Configure(TextWriter? log = null) => _inner.Configure(log);

        public Task<bool> ConfigureAsync(TextWriter? log = null) => _inner.ConfigureAsync(log);

        public void Dispose() { } // DO NOT call _inner.Dispose();

        public ValueTask DisposeAsync() => default; // DO NOT call _inner.DisposeAsync();

        public ServerCounters GetCounters() => _inner.GetCounters();

        public IDatabase GetDatabase(int db = -1, object? asyncState = null) => _inner.GetDatabase(db, asyncState);

        public EndPoint[] GetEndPoints(bool configuredOnly = false) => _inner.GetEndPoints(configuredOnly);

        public int GetHashSlot(RedisKey key) => _inner.GetHashSlot(key);

        public IServer GetServer(string host, int port, object? asyncState = null) => _inner.GetServer(host, port, asyncState);

        public IServer GetServer(string hostAndPort, object? asyncState = null) => _inner.GetServer(hostAndPort, asyncState);

        public IServer GetServer(IPAddress host, int port) => _inner.GetServer(host, port);

        public IServer GetServer(EndPoint endpoint, object? asyncState = null) => _inner.GetServer(endpoint, asyncState);

        public IServer[] GetServers() => _inner.GetServers();

        public string GetStatus() => _inner.GetStatus();

        public void GetStatus(TextWriter log) => _inner.GetStatus(log);

        public string? GetStormLog() => _inner.GetStormLog();

        public ISubscriber GetSubscriber(object? asyncState = null) => _inner.GetSubscriber(asyncState);

        public int HashSlot(RedisKey key) => _inner.HashSlot(key);

        public long PublishReconfigure(CommandFlags flags = CommandFlags.None) => _inner.PublishReconfigure(flags);

        public Task<long> PublishReconfigureAsync(CommandFlags flags = CommandFlags.None) => _inner.PublishReconfigureAsync(flags);

        public void RegisterProfiler(Func<ProfilingSession?> profilingSessionProvider) => _inner.RegisterProfiler(profilingSessionProvider);

        public void ResetStormLog() => _inner.ResetStormLog();

        public void Wait(Task task) => _inner.Wait(task);

        public T Wait<T>(Task<T> task) => _inner.Wait(task);

        public void WaitAll(params Task[] tasks) => _inner.WaitAll(tasks);

        public void ExportConfiguration(Stream destination, ExportOptions options = ExportOptions.All)
            => _inner.ExportConfiguration(destination, options);

        public override string ToString() => _inner.ToString();
        long? IInternalConnectionMultiplexer.GetConnectionId(EndPoint endPoint, ConnectionType type)
            => _inner.GetConnectionId(endPoint, type);
    }

    public void Dispose()
    {
        resp2?.UnderlyingConnection?.Dispose();
        resp3?.UnderlyingConnection?.Dispose();
        GC.SuppressFinalize(this);
    }

    protected void OnInternalError(object? sender, InternalErrorEventArgs e)
    {
        Interlocked.Increment(ref privateFailCount);
        lock (privateExceptions)
        {
            privateExceptions.Add(TestBase.Time() + ": Internal error: " + e.Origin + ", " + EndPointCollection.ToString(e.EndPoint) + "/" + e.ConnectionType);
        }
    }
    protected void OnConnectionFailed(object? sender, ConnectionFailedEventArgs e)
    {
        Interlocked.Increment(ref privateFailCount);
        lock (privateExceptions)
        {
            privateExceptions.Add($"{TestBase.Time()}: Connection failed ({e.FailureType}): {EndPointCollection.ToString(e.EndPoint)}/{e.ConnectionType}: {e.Exception}");
        }
    }
    private readonly List<string> privateExceptions = [];
    private int privateFailCount;

    public void Teardown(TextWriter output)
    {
        var innerPrivateFailCount = Interlocked.Exchange(ref privateFailCount, 0);
        if (innerPrivateFailCount != 0)
        {
            lock (privateExceptions)
            {
                foreach (var item in privateExceptions.Take(5))
                {
                    TestBase.Log(output, item);
                }
                privateExceptions.Clear();
            }
            // Assert.True(false, $"There were {privateFailCount} private ambient exceptions.");
        }

        if (_actualConnection != null)
        {
            TestBase.Log(output, "Connection Counts: " + _actualConnection.GetCounters().ToString());
            foreach (var ep in _actualConnection.GetServerSnapshot())
            {
                var interactive = ep.GetBridge(ConnectionType.Interactive);
                TestBase.Log(output, $"  {Format.ToString(interactive)}: {interactive?.GetStatus()}");

                var subscription = ep.GetBridge(ConnectionType.Subscription);
                TestBase.Log(output, $"  {Format.ToString(subscription)}: {subscription?.GetStatus()}");
            }
        }
    }
}
