﻿using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class RealWorldTests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public async Task WhyDoesThisNotWork()
    {
        Log("first:");
        var config = ConfigurationOptions.Parse("localhost:6379,localhost:6380,name=Core (Q&A),tiebreaker=:RedisPrimary,abortConnect=False");
        Assert.Equal(2, config.EndPoints.Count);
        Log("Endpoint 0: {0} (AddressFamily: {1})", config.EndPoints[0], config.EndPoints[0].AddressFamily);
        Log("Endpoint 1: {0} (AddressFamily: {1})", config.EndPoints[1], config.EndPoints[1].AddressFamily);

        await using (var conn = ConnectionMultiplexer.Connect("localhost:6379,localhost:6380,name=Core (Q&A),tiebreaker=:RedisPrimary,abortConnect=False", Writer))
        {
            Log("");
            Log("pausing...");
            await Task.Delay(200).ForAwait();
            Log("second:");

            bool result = conn.Configure(Writer);
            Log("Returned: {0}", result);
        }
    }
}
