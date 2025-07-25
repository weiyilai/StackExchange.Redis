﻿using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
public class GeoTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    private static readonly GeoEntry
        Palermo = new GeoEntry(13.361389, 38.115556, "Palermo"),
        Catania = new GeoEntry(15.087269, 37.502669, "Catania"),
        Agrigento = new GeoEntry(13.5765, 37.311, "Agrigento"),
        Cefalù = new GeoEntry(14.0188, 38.0084, "Cefalù");

    private static readonly GeoEntry[] All = [Palermo, Catania, Agrigento, Cefalù];

    [Fact]
    public async Task GeoAdd()
    {
        await using var conn = Create(require: RedisFeatures.v3_2_0);

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        // add while not there
        Assert.True(db.GeoAdd(key, Cefalù.Longitude, Cefalù.Latitude, Cefalù.Member));
        Assert.Equal(2, db.GeoAdd(key, [Palermo, Catania]));
        Assert.True(db.GeoAdd(key, Agrigento));

        // now add again
        Assert.False(db.GeoAdd(key, Cefalù.Longitude, Cefalù.Latitude, Cefalù.Member));
        Assert.Equal(0, db.GeoAdd(key, [Palermo, Catania]));
        Assert.False(db.GeoAdd(key, Agrigento));

        // Validate
        var pos = db.GeoPosition(key, Palermo.Member);
        Assert.NotNull(pos);
        Assert.Equal(Palermo.Longitude, pos!.Value.Longitude, 5);
        Assert.Equal(Palermo.Latitude, pos!.Value.Latitude, 5);
    }

    [Fact]
    public async Task GetDistance()
    {
        await using var conn = Create(require: RedisFeatures.v3_2_0);

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.GeoAdd(key, All, CommandFlags.FireAndForget);
        var val = db.GeoDistance(key, "Palermo", "Catania", GeoUnit.Meters);
        Assert.True(val.HasValue);
        Assert.Equal(166274.1516, val);

        val = db.GeoDistance(key, "Palermo", "Nowhere", GeoUnit.Meters);
        Assert.False(val.HasValue);
    }

    [Fact]
    public async Task GeoHash()
    {
        await using var conn = Create(require: RedisFeatures.v3_2_0);

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.GeoAdd(key, All, CommandFlags.FireAndForget);

        var hashes = db.GeoHash(key, [Palermo.Member, "Nowhere", Agrigento.Member]);
        Assert.NotNull(hashes);
        Assert.Equal(3, hashes.Length);
        Assert.Equal("sqc8b49rny0", hashes[0]);
        Assert.Null(hashes[1]);
        Assert.Equal("sq9skbq0760", hashes[2]);

        var hash = db.GeoHash(key, "Palermo");
        Assert.Equal("sqc8b49rny0", hash);

        hash = db.GeoHash(key, "Nowhere");
        Assert.Null(hash);
    }

    [Fact]
    public async Task GeoGetPosition()
    {
        await using var conn = Create(require: RedisFeatures.v3_2_0);

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.GeoAdd(key, All, CommandFlags.FireAndForget);

        var pos = db.GeoPosition(key, Palermo.Member);
        Assert.True(pos.HasValue);
        Assert.Equal(Math.Round(Palermo.Longitude, 6), Math.Round(pos.Value.Longitude, 6));
        Assert.Equal(Math.Round(Palermo.Latitude, 6), Math.Round(pos.Value.Latitude, 6));

        pos = db.GeoPosition(key, "Nowhere");
        Assert.False(pos.HasValue);
    }

    [Fact]
    public async Task GeoRemove()
    {
        await using var conn = Create(require: RedisFeatures.v3_2_0);

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.GeoAdd(key, All, CommandFlags.FireAndForget);

        var pos = db.GeoPosition(key, "Palermo");
        Assert.True(pos.HasValue);

        Assert.False(db.GeoRemove(key, "Nowhere"));
        Assert.True(db.GeoRemove(key, "Palermo"));
        Assert.False(db.GeoRemove(key, "Palermo"));

        pos = db.GeoPosition(key, "Palermo");
        Assert.False(pos.HasValue);
    }

    [Fact]
    public async Task GeoRadius()
    {
        await using var conn = Create(require: RedisFeatures.v3_2_0);

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.GeoAdd(key, All, CommandFlags.FireAndForget);

        var results = db.GeoRadius(key, Cefalù.Member, 60, GeoUnit.Miles, 2, Order.Ascending);
        Assert.Equal(2, results.Length);

        Assert.Equal(results[0].Member, Cefalù.Member);
        Assert.Equal(0, results[0].Distance);
        var position0 = results[0].Position;
        Assert.NotNull(position0);
        Assert.Equal(Math.Round(position0!.Value.Longitude, 5), Math.Round(Cefalù.Position.Longitude, 5));
        Assert.Equal(Math.Round(position0!.Value.Latitude, 5), Math.Round(Cefalù.Position.Latitude, 5));
        Assert.False(results[0].Hash.HasValue);

        Assert.Equal(results[1].Member, Palermo.Member);
        var distance1 = results[1].Distance;
        Assert.NotNull(distance1);
        Assert.Equal(Math.Round(36.5319, 6), Math.Round(distance1!.Value, 6));
        var position1 = results[1].Position;
        Assert.NotNull(position1);
        Assert.Equal(Math.Round(position1!.Value.Longitude, 5), Math.Round(Palermo.Position.Longitude, 5));
        Assert.Equal(Math.Round(position1!.Value.Latitude, 5), Math.Round(Palermo.Position.Latitude, 5));
        Assert.False(results[1].Hash.HasValue);

        results = db.GeoRadius(key, Cefalù.Member, 60, GeoUnit.Miles, 2, Order.Ascending, GeoRadiusOptions.None);
        Assert.Equal(2, results.Length);
        Assert.Equal(results[0].Member, Cefalù.Member);
        Assert.False(results[0].Position.HasValue);
        Assert.False(results[0].Distance.HasValue);
        Assert.False(results[0].Hash.HasValue);

        Assert.Equal(results[1].Member, Palermo.Member);
        Assert.False(results[1].Position.HasValue);
        Assert.False(results[1].Distance.HasValue);
        Assert.False(results[1].Hash.HasValue);
    }

    [Fact]
    public async Task GeoRadiusOverloads()
    {
        await using var conn = Create(require: RedisFeatures.v3_2_0);

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        Assert.True(db.GeoAdd(key, -1.759925, 52.19493, "steve"));
        Assert.True(db.GeoAdd(key, -3.360655, 54.66395, "dave"));

        // Invalid overload
        // Since this would throw ERR could not decode requested zset member, we catch and return something more useful to the user earlier.
        var ex = Assert.Throws<ArgumentException>(() => db.GeoRadius(key, -1.759925, 52.19493, GeoUnit.Miles, 500, Order.Ascending, GeoRadiusOptions.WithDistance));
        Assert.StartsWith("Member should not be a double, you likely want the GeoRadius(RedisKey, double, double, ...) overload.", ex.Message);
        Assert.Equal("member", ex.ParamName);
        ex = await Assert.ThrowsAsync<ArgumentException>(() => db.GeoRadiusAsync(key, -1.759925, 52.19493, GeoUnit.Miles, 500, Order.Ascending, GeoRadiusOptions.WithDistance)).ForAwait();
        Assert.StartsWith("Member should not be a double, you likely want the GeoRadius(RedisKey, double, double, ...) overload.", ex.Message);
        Assert.Equal("member", ex.ParamName);

        // The good stuff
        GeoRadiusResult[] result = db.GeoRadius(key, -1.759925, 52.19493, 500, unit: GeoUnit.Miles, order: Order.Ascending, options: GeoRadiusOptions.WithDistance);
        Assert.NotNull(result);

        result = await db.GeoRadiusAsync(key, -1.759925, 52.19493, 500, unit: GeoUnit.Miles, order: Order.Ascending, options: GeoRadiusOptions.WithDistance).ForAwait();
        Assert.NotNull(result);
    }

    private async Task GeoSearchSetupAsync(RedisKey key, IDatabase db)
    {
        await db.KeyDeleteAsync(key);
        await db.GeoAddAsync(key, 82.6534, 27.7682, "rays");
        await db.GeoAddAsync(key, 79.3891, 43.6418, "blue jays");
        await db.GeoAddAsync(key, 76.6217, 39.2838, "orioles");
        await db.GeoAddAsync(key, 71.0927, 42.3467, "red sox");
        await db.GeoAddAsync(key, 73.9262, 40.8296, "yankees");
    }

    private void GeoSearchSetup(RedisKey key, IDatabase db)
    {
        db.KeyDelete(key);
        db.GeoAdd(key, 82.6534, 27.7682, "rays");
        db.GeoAdd(key, 79.3891, 43.6418, "blue jays");
        db.GeoAdd(key, 76.6217, 39.2838, "orioles");
        db.GeoAdd(key, 71.0927, 42.3467, "red sox");
        db.GeoAdd(key, 73.9262, 40.8296, "yankees");
    }

    [Fact]
    public async Task GeoSearchCircleMemberAsync()
    {
        await using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        await GeoSearchSetupAsync(key, db);

        var circle = new GeoSearchCircle(500, GeoUnit.Miles);
        var res = await db.GeoSearchAsync(key, "yankees", circle);
        Assert.Contains(res, x => x.Member == "yankees");
        Assert.Contains(res, x => x.Member == "red sox");
        Assert.Contains(res, x => x.Member == "orioles");
        Assert.Contains(res, x => x.Member == "blue jays");
        Assert.NotNull(res[0].Distance);
        Assert.NotNull(res[0].Position);
        Assert.Null(res[0].Hash);
        Assert.Equal(4, res.Length);
    }

    [Fact]
    public async Task GeoSearchCircleMemberAsyncOnlyHash()
    {
        await using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        await GeoSearchSetupAsync(key, db);

        var circle = new GeoSearchCircle(500, GeoUnit.Miles);
        var res = await db.GeoSearchAsync(key, "yankees", circle, options: GeoRadiusOptions.WithGeoHash);
        Assert.Contains(res, x => x.Member == "yankees");
        Assert.Contains(res, x => x.Member == "red sox");
        Assert.Contains(res, x => x.Member == "orioles");
        Assert.Contains(res, x => x.Member == "blue jays");
        Assert.Null(res[0].Distance);
        Assert.Null(res[0].Position);
        Assert.NotNull(res[0].Hash);
        Assert.Equal(4, res.Length);
    }

    [Fact]
    public async Task GeoSearchCircleMemberAsyncHashAndDistance()
    {
        await using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        await GeoSearchSetupAsync(key, db);

        var circle = new GeoSearchCircle(500, GeoUnit.Miles);
        var res = await db.GeoSearchAsync(key, "yankees", circle, options: GeoRadiusOptions.WithGeoHash | GeoRadiusOptions.WithDistance);
        Assert.Contains(res, x => x.Member == "yankees");
        Assert.Contains(res, x => x.Member == "red sox");
        Assert.Contains(res, x => x.Member == "orioles");
        Assert.Contains(res, x => x.Member == "blue jays");
        Assert.NotNull(res[0].Distance);
        Assert.Null(res[0].Position);
        Assert.NotNull(res[0].Hash);
        Assert.Equal(4, res.Length);
    }

    [Fact]
    public async Task GeoSearchCircleLonLatAsync()
    {
        await using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        await GeoSearchSetupAsync(key, db);

        var circle = new GeoSearchCircle(500, GeoUnit.Miles);
        var res = await db.GeoSearchAsync(key, 73.9262, 40.8296, circle);
        Assert.Contains(res, x => x.Member == "yankees");
        Assert.Contains(res, x => x.Member == "red sox");
        Assert.Contains(res, x => x.Member == "orioles");
        Assert.Contains(res, x => x.Member == "blue jays");
        Assert.Equal(4, res.Length);
    }

    [Fact]
    public async Task GeoSearchCircleMember()
    {
        await using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        GeoSearchSetup(key, db);

        var circle = new GeoSearchCircle(500 * 1609);
        var res = db.GeoSearch(key, "yankees", circle);
        Assert.Contains(res, x => x.Member == "yankees");
        Assert.Contains(res, x => x.Member == "red sox");
        Assert.Contains(res, x => x.Member == "orioles");
        Assert.Contains(res, x => x.Member == "blue jays");
        Assert.Equal(4, res.Length);
    }

    [Fact]
    public async Task GeoSearchCircleLonLat()
    {
        await using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        GeoSearchSetup(key, db);

        var circle = new GeoSearchCircle(500 * 5280, GeoUnit.Feet);
        var res = db.GeoSearch(key, 73.9262, 40.8296, circle);
        Assert.Contains(res, x => x.Member == "yankees");
        Assert.Contains(res, x => x.Member == "red sox");
        Assert.Contains(res, x => x.Member == "orioles");
        Assert.Contains(res, x => x.Member == "blue jays");
        Assert.Equal(4, res.Length);
    }

    [Fact]
    public async Task GeoSearchBoxMemberAsync()
    {
        await using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        await GeoSearchSetupAsync(key, db);

        var box = new GeoSearchBox(500, 500, GeoUnit.Kilometers);
        var res = await db.GeoSearchAsync(key, "yankees", box);
        Assert.Contains(res, x => x.Member == "yankees");
        Assert.Contains(res, x => x.Member == "red sox");
        Assert.Contains(res, x => x.Member == "orioles");
        Assert.Equal(3, res.Length);
    }

    [Fact]
    public async Task GeoSearchBoxLonLatAsync()
    {
        await using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        await GeoSearchSetupAsync(key, db);

        var box = new GeoSearchBox(500, 500, GeoUnit.Kilometers);
        var res = await db.GeoSearchAsync(key, 73.9262, 40.8296, box);
        Assert.Contains(res, x => x.Member == "yankees");
        Assert.Contains(res, x => x.Member == "red sox");
        Assert.Contains(res, x => x.Member == "orioles");
        Assert.Equal(3, res.Length);
    }

    [Fact]
    public async Task GeoSearchBoxMember()
    {
        await using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        GeoSearchSetup(key, db);

        var box = new GeoSearchBox(500, 500, GeoUnit.Kilometers);
        var res = db.GeoSearch(key, "yankees", box);
        Assert.Contains(res, x => x.Member == "yankees");
        Assert.Contains(res, x => x.Member == "red sox");
        Assert.Contains(res, x => x.Member == "orioles");
        Assert.Equal(3, res.Length);
    }

    [Fact]
    public async Task GeoSearchBoxLonLat()
    {
        await using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        GeoSearchSetup(key, db);

        var box = new GeoSearchBox(500, 500, GeoUnit.Kilometers);
        var res = db.GeoSearch(key, 73.9262, 40.8296, box);
        Assert.Contains(res, x => x.Member == "yankees");
        Assert.Contains(res, x => x.Member == "red sox");
        Assert.Contains(res, x => x.Member == "orioles");
        Assert.Equal(3, res.Length);
    }

    [Fact]
    public async Task GeoSearchLimitCount()
    {
        await using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        GeoSearchSetup(key, db);

        var box = new GeoSearchBox(500, 500, GeoUnit.Kilometers);
        var res = db.GeoSearch(key, 73.9262, 40.8296, box, count: 2);
        Assert.Contains(res, x => x.Member == "yankees");
        Assert.Contains(res, x => x.Member == "orioles");
        Assert.Equal(2, res.Length);
    }

    [Fact]
    public async Task GeoSearchLimitCountMakeNoDemands()
    {
        await using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        GeoSearchSetup(key, db);

        var box = new GeoSearchBox(500, 500, GeoUnit.Kilometers);
        var res = db.GeoSearch(key, 73.9262, 40.8296, box, count: 2, demandClosest: false);
        Assert.Contains(res, x => x.Member == "red sox"); // this order MIGHT not be fully deterministic, seems to work for our purposes.
        Assert.Contains(res, x => x.Member == "orioles");
        Assert.Equal(2, res.Length);
    }

    [Fact]
    public async Task GeoSearchBoxLonLatDescending()
    {
        await using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        await GeoSearchSetupAsync(key, db);

        var box = new GeoSearchBox(500, 500, GeoUnit.Kilometers);
        var res = await db.GeoSearchAsync(key, 73.9262, 40.8296, box, order: Order.Descending);
        Assert.Contains(res, x => x.Member == "yankees");
        Assert.Contains(res, x => x.Member == "red sox");
        Assert.Contains(res, x => x.Member == "orioles");
        Assert.Equal(3, res.Length);
        Assert.Equal("red sox", res[0].Member);
    }

    [Fact]
    public async Task GeoSearchBoxMemberAndStoreAsync()
    {
        await using var conn = Create(require: RedisFeatures.v6_2_0);

        var me = Me();
        var db = conn.GetDatabase();
        RedisKey sourceKey = $"{me}:source";
        RedisKey destinationKey = $"{me}:destination";
        await db.KeyDeleteAsync(destinationKey);
        await GeoSearchSetupAsync(sourceKey, db);

        var box = new GeoSearchBox(500, 500, GeoUnit.Kilometers);
        var res = await db.GeoSearchAndStoreAsync(sourceKey, destinationKey, "yankees", box);
        var set = await db.GeoSearchAsync(destinationKey, "yankees", new GeoSearchCircle(10000, GeoUnit.Miles));
        Assert.Contains(set, x => x.Member == "yankees");
        Assert.Contains(set, x => x.Member == "red sox");
        Assert.Contains(set, x => x.Member == "orioles");
        Assert.Equal(3, set.Length);
        Assert.Equal(3, res);
    }

    [Fact]
    public async Task GeoSearchBoxLonLatAndStoreAsync()
    {
        await using var conn = Create(require: RedisFeatures.v6_2_0);

        var me = Me();
        var db = conn.GetDatabase();
        RedisKey sourceKey = $"{me}:source";
        RedisKey destinationKey = $"{me}:destination";
        await db.KeyDeleteAsync(destinationKey);
        await GeoSearchSetupAsync(sourceKey, db);

        var box = new GeoSearchBox(500, 500, GeoUnit.Kilometers);
        var res = await db.GeoSearchAndStoreAsync(sourceKey, destinationKey, 73.9262, 40.8296, box);
        var set = await db.GeoSearchAsync(destinationKey, "yankees", new GeoSearchCircle(10000, GeoUnit.Miles));
        Assert.Contains(set, x => x.Member == "yankees");
        Assert.Contains(set, x => x.Member == "red sox");
        Assert.Contains(set, x => x.Member == "orioles");
        Assert.Equal(3, set.Length);
        Assert.Equal(3, res);
    }

    [Fact]
    public async Task GeoSearchCircleMemberAndStoreAsync()
    {
        await using var conn = Create(require: RedisFeatures.v6_2_0);

        var me = Me();
        var db = conn.GetDatabase();
        RedisKey sourceKey = $"{me}:source";
        RedisKey destinationKey = $"{me}:destination";
        await db.KeyDeleteAsync(destinationKey);
        await GeoSearchSetupAsync(sourceKey, db);

        var circle = new GeoSearchCircle(500, GeoUnit.Kilometers);
        var res = await db.GeoSearchAndStoreAsync(sourceKey, destinationKey, "yankees", circle);
        var set = await db.GeoSearchAsync(destinationKey, "yankees", new GeoSearchCircle(10000, GeoUnit.Miles));
        Assert.Contains(set, x => x.Member == "yankees");
        Assert.Contains(set, x => x.Member == "red sox");
        Assert.Contains(set, x => x.Member == "orioles");
        Assert.Equal(3, set.Length);
        Assert.Equal(3, res);
    }

    [Fact]
    public async Task GeoSearchCircleLonLatAndStoreAsync()
    {
        await using var conn = Create(require: RedisFeatures.v6_2_0);

        var me = Me();
        var db = conn.GetDatabase();
        RedisKey sourceKey = $"{me}:source";
        RedisKey destinationKey = $"{me}:destination";
        await db.KeyDeleteAsync(destinationKey);
        await GeoSearchSetupAsync(sourceKey, db);

        var circle = new GeoSearchCircle(500, GeoUnit.Kilometers);
        var res = await db.GeoSearchAndStoreAsync(sourceKey, destinationKey, 73.9262, 40.8296, circle);
        var set = await db.GeoSearchAsync(destinationKey, "yankees", new GeoSearchCircle(10000, GeoUnit.Miles));
        Assert.Contains(set, x => x.Member == "yankees");
        Assert.Contains(set, x => x.Member == "red sox");
        Assert.Contains(set, x => x.Member == "orioles");
        Assert.Equal(3, set.Length);
        Assert.Equal(3, res);
    }

    [Fact]
    public async Task GeoSearchCircleMemberAndStore()
    {
        await using var conn = Create(require: RedisFeatures.v6_2_0);

        var me = Me();
        var db = conn.GetDatabase();
        RedisKey sourceKey = $"{me}:source";
        RedisKey destinationKey = $"{me}:destination";
        db.KeyDelete(destinationKey);
        GeoSearchSetup(sourceKey, db);

        var circle = new GeoSearchCircle(500, GeoUnit.Kilometers);
        var res = db.GeoSearchAndStore(sourceKey, destinationKey, "yankees", circle);
        var set = db.GeoSearch(destinationKey, "yankees", new GeoSearchCircle(10000, GeoUnit.Miles));
        Assert.Contains(set, x => x.Member == "yankees");
        Assert.Contains(set, x => x.Member == "red sox");
        Assert.Contains(set, x => x.Member == "orioles");
        Assert.Equal(3, set.Length);
        Assert.Equal(3, res);
    }

    [Fact]
    public async Task GeoSearchCircleLonLatAndStore()
    {
        await using var conn = Create(require: RedisFeatures.v6_2_0);

        var me = Me();
        var db = conn.GetDatabase();
        RedisKey sourceKey = $"{me}:source";
        RedisKey destinationKey = $"{me}:destination";
        db.KeyDelete(destinationKey);
        GeoSearchSetup(sourceKey, db);

        var circle = new GeoSearchCircle(500, GeoUnit.Kilometers);
        var res = db.GeoSearchAndStore(sourceKey, destinationKey, 73.9262, 40.8296, circle);
        var set = db.GeoSearch(destinationKey, "yankees", new GeoSearchCircle(10000, GeoUnit.Miles));
        Assert.Contains(set, x => x.Member == "yankees");
        Assert.Contains(set, x => x.Member == "red sox");
        Assert.Contains(set, x => x.Member == "orioles");
        Assert.Equal(3, set.Length);
        Assert.Equal(3, res);
    }

    [Fact]
    public async Task GeoSearchCircleAndStoreDistOnly()
    {
        await using var conn = Create(require: RedisFeatures.v6_2_0);

        var me = Me();
        var db = conn.GetDatabase();
        RedisKey sourceKey = $"{me}:source";
        RedisKey destinationKey = $"{me}:destination";
        db.KeyDelete(destinationKey);
        GeoSearchSetup(sourceKey, db);

        var circle = new GeoSearchCircle(500, GeoUnit.Kilometers);
        var res = db.GeoSearchAndStore(sourceKey, destinationKey, 73.9262, 40.8296, circle, storeDistances: true);
        var set = db.SortedSetRangeByRankWithScores(destinationKey);
        Assert.Contains(set, x => x.Element == "yankees");
        Assert.Contains(set, x => x.Element == "red sox");
        Assert.Contains(set, x => x.Element == "orioles");
        Assert.InRange(Array.Find(set, x => x.Element == "yankees").Score, 0, .2);
        Assert.InRange(Array.Find(set, x => x.Element == "orioles").Score, 286, 287);
        Assert.InRange(Array.Find(set, x => x.Element == "red sox").Score, 289, 290);
        Assert.Equal(3, set.Length);
        Assert.Equal(3, res);
    }

    [Fact]
    public async Task GeoSearchBadArgs()
    {
        await using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        db.KeyDelete(key);
        var circle = new GeoSearchCircle(500, GeoUnit.Kilometers);
        var exception = Assert.Throws<ArgumentException>(() =>
            db.GeoSearch(key, "irrelevant", circle, demandClosest: false));

        Assert.Contains("demandClosest must be true if you are not limiting the count for a GEOSEARCH", exception.Message);
    }
}
