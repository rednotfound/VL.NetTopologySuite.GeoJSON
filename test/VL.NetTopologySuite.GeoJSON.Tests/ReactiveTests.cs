using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using VL.Lib.Collections;
using Xunit;

using NtsFeature = NetTopologySuite.Features.Feature;
using VLPath = VL.Lib.IO.Path;

namespace VL.NetTopologySuite.GeoJSON.Tests;

/// <summary>
/// <c>GeoJSONReader (Reactive)</c>: the same features as <c>Read GeoJSON</c>, off the main thread.
/// </summary>
/// <remarks>
/// <para>The first group is the one that matters: the file reader parses one feature at a time
/// with its own loop around the upstream converter, and that loop must not drift from
/// <c>Deserialize&lt;FeatureCollection&gt;</c>. Every fixture is read both ways and compared
/// feature by feature, geometry exactly and every attribute by type and value.</para>
/// <para>The rest drive <c>Update</c> the way vvvv does - one call per "frame" - and wait on the
/// pins, never on internals.</para>
/// </remarks>
public sealed class ReactiveTests : IDisposable
{
    readonly List<string> _temp = [];

    public void Dispose()
    {
        foreach (var file in _temp)
            File.Delete(file);
    }

    string TempFile(string text)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vlgeojson-{Guid.NewGuid():N}.geojson");
        File.WriteAllText(path, text, new UTF8Encoding(false));
        _temp.Add(path);
        return path;
    }

    sealed record Frame(float Progress, bool InProgress, bool OnCompleted, bool Success, string Status);

    static Frame Tick(GeoJsonReaderReactiveNode node, string? path = null, bool read = false, GeometryFactory? factory = null)
    {
        node.Update(out var progress, out var inProgress, out var onCompleted, out var success, out var status,
            path is null ? null : new VLPath(path), read, factory);
        return new Frame(progress, inProgress, onCompleted, success, status);
    }

    /// <summary>Tick until On Completed fires; return every frame seen.</summary>
    static List<Frame> RunToCompletion(GeoJsonReaderReactiveNode node, string path, GeometryFactory? factory = null)
    {
        var frames = new List<Frame> { Tick(node, path, read: true, factory) };
        var clock = Stopwatch.StartNew();
        while (!frames[^1].OnCompleted)
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), "the read never completed");
            Thread.Sleep(2);
            frames.Add(Tick(node, path, factory: factory));
        }
        return frames;
    }

    static (List<Spread<NtsFeature>> Received, List<Frame> Frames) ReadFile(string path, GeometryFactory? factory = null)
    {
        using var node = new GeoJsonReaderReactiveNode();
        var received = new List<Spread<NtsFeature>>();
        using var subscription = Tick(node) is var _ ? Subscribe(node, received) : null;
        var frames = RunToCompletion(node, path, factory);
        return (received, frames);
    }

    static IDisposable Subscribe(GeoJsonReaderReactiveNode node, List<Spread<NtsFeature>> into)
        => node.Update(out _, out _, out _, out _, out _).Subscribe(s => { lock (into) into.Add(s); });

    static void AssertSameFeatures(Spread<NtsFeature> expected, Spread<NtsFeature> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            var (e, a) = (expected[i], actual[i]);
            Assert.Equal(e.Geometry?.SRID, a.Geometry?.SRID);
            Assert.True(e.Geometry is null ? a.Geometry is null : e.Geometry.EqualsExact(a.Geometry), $"geometry {i}");
            AssertSameTable(e.Attributes, a.Attributes);
        }
    }

    static void AssertSameTable(IAttributesTable expected, IAttributesTable actual)
    {
        Assert.Equal(expected.GetNames(), actual.GetNames());
        foreach (var name in expected.GetNames())
            AssertSameValue(expected[name], actual[name]);
    }

    static void AssertSameValue(object? expected, object? actual)
    {
        Assert.Equal(expected?.GetType(), actual?.GetType());
        switch (expected)
        {
            case IAttributesTable table: AssertSameTable(table, (IAttributesTable)actual!); break;
            case object[] array:
                var other = (object[])actual!;
                Assert.Equal(array.Length, other.Length);
                for (var i = 0; i < array.Length; i++) AssertSameValue(array[i], other[i]);
                break;
            default: Assert.Equal(expected, actual); break;
        }
    }

    [Theory]
    [InlineData(Fixtures.Points)]
    [InlineData(Fixtures.Polygons)]
    [InlineData(Fixtures.MultiPolygon)]
    [InlineData(Fixtures.Mixed)]
    public void A_file_gives_exactly_the_features_Read_GeoJSON_gives(string fixture)
    {
        var expected = new ReadGeoJsonNode().Update(out var ok, out _, out _, Fixtures.Read(fixture));
        Assert.True(ok);

        var (received, frames) = ReadFile(Fixtures.PathOf(fixture));

        Assert.True(frames[^1].Success, frames[^1].Status);
        AssertSameFeatures(expected, Assert.Single(received));
    }

    [Fact]
    public void A_large_synthetic_file_gives_the_same_features_too()
    {
        var text = Fixtures.Synthetic(2_000);
        var expected = new ReadGeoJsonNode().Update(out _, out _, out _, text);

        var (received, frames) = ReadFile(TempFile(text));

        Assert.Equal("2000 features", frames[^1].Status);
        AssertSameFeatures(expected, Assert.Single(received));
    }

    [Fact]
    public void The_factory_pin_sets_the_srid_as_it_does_on_Read_GeoJSON()
    {
        var (received, _) = ReadFile(Fixtures.PathOf(Fixtures.Points), new GeometryFactory(new PrecisionModel(), 3857));

        Assert.All(Assert.Single(received), f => Assert.Equal(3857, f.Geometry.SRID));
    }

    [Theory]
    [InlineData(true)]              // a byte order mark, which Utf8JsonReader alone rejects
    [InlineData(false)]
    public void A_byte_order_mark_is_skipped(bool bom)
    {
        var path = TempFile("");
        File.WriteAllText(path, Fixtures.Read(Fixtures.Points), new UTF8Encoding(bom));
        Assert.Equal(bom, File.ReadAllBytes(path)[0] == 0xEF);

        var (received, frames) = ReadFile(path);

        Assert.True(frames[^1].Success, frames[^1].Status);
        Assert.Equal(4, Assert.Single(received).Count);
    }

    [Fact]
    public void Members_after_features_and_foreign_members_are_ignored_as_Read_GeoJSON_ignores_them()
    {
        const string text = """
            {"features":[{"type":"Feature","geometry":{"type":"Point","coordinates":[1,2]},"properties":{"a":1}}],
             "crs":{"type":"name","properties":{"name":"EPSG:4326"}},"bbox":[1,2,1,2],"foreign":{"x":[1,2]},
             "type":"FeatureCollection"}
            """;
        var expected = new ReadGeoJsonNode().Update(out _, out _, out _, text);

        var (received, _) = ReadFile(TempFile(text));

        AssertSameFeatures(expected, Assert.Single(received));
    }

    [Theory]
    [InlineData("""{"type":"Point","coordinates":[1,2]}""")]
    [InlineData("""[1,2,3]""")]
    [InlineData("""{ not json""")]
    public void A_file_that_is_not_a_feature_collection_fails_and_publishes_nothing(string text)
    {
        var (received, frames) = ReadFile(TempFile(text));

        Assert.False(frames[^1].Success);
        Assert.Contains("JsonException", frames[^1].Status);
        Assert.Empty(received);
    }

    [Fact]
    public void Valid_json_that_is_not_geojson_is_zero_features_as_on_Read_GeoJSON()
    {
        var (received, frames) = ReadFile(TempFile("""{"hello":"world"}"""));

        Assert.Equal("0 features", frames[^1].Status);
        Assert.Empty(Assert.Single(received));
    }

    [Fact]
    public void The_malformed_fixture_fails_on_both_readers()
    {
        new ReadGeoJsonNode().Update(out var ok, out _, out _, Fixtures.Read(Fixtures.Malformed));
        var (received, frames) = ReadFile(Fixtures.PathOf(Fixtures.Malformed));

        Assert.False(ok);
        Assert.False(frames[^1].Success);
        Assert.Empty(received);
    }

    [Fact]
    public void A_missing_file_says_so()
    {
        var (received, frames) = ReadFile(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.geojson"));

        Assert.False(frames[^1].Success);
        Assert.StartsWith("FileNotFoundException", frames[^1].Status);
        Assert.Empty(received);
    }

    [Fact]
    public void An_empty_path_is_a_failure_not_the_documents_folder()
    {
        using var node = new GeoJsonReaderReactiveNode();

        var frame = Tick(node, "", read: true);

        Assert.True(frame.OnCompleted);
        Assert.False(frame.Success);
        Assert.Equal("no file path", frame.Status);
    }

    [Fact]
    public void Before_any_read_nothing_is_in_progress()
    {
        using var node = new GeoJsonReaderReactiveNode();

        var frame = Tick(node, Fixtures.PathOf(Fixtures.Points));

        Assert.False(frame.InProgress);
        Assert.False(frame.OnCompleted);
        Assert.Equal("nothing read yet", frame.Status);
    }

    [Fact]
    public void In_progress_until_done_then_on_completed_for_exactly_one_frame()
    {
        using var node = new GeoJsonReaderReactiveNode();
        var path = TempFile(Fixtures.Synthetic(5_000));

        var frames = RunToCompletion(node, path);
        var after = Enumerable.Range(0, 3).Select(_ => Tick(node, path)).ToList();

        Assert.True(frames[0].InProgress);
        Assert.False(frames[^1].InProgress);
        Assert.Equal(1f, frames[^1].Progress);
        Assert.All(frames.SkipLast(1), f => Assert.False(f.OnCompleted));
        Assert.All(after, f => Assert.False(f.OnCompleted));
        // Progress never goes backwards while one read runs.
        Assert.Equal(frames.Select(f => f.Progress).OrderBy(p => p), frames.Select(f => f.Progress));
    }

    [Fact]
    public void A_new_read_cancels_the_one_in_progress_and_only_the_new_one_is_published()
    {
        using var node = new GeoJsonReaderReactiveNode();
        var received = new List<Spread<NtsFeature>>();
        using var _ = Subscribe(node, received);
        var big = TempFile(Fixtures.Synthetic(20_000));
        var small = Fixtures.PathOf(Fixtures.Points);

        // How long the big read takes when nothing cancels it - the time a stale result needs.
        var clock = Stopwatch.StartNew();
        ReadFile(big);
        var uncancelled = clock.Elapsed;

        Tick(node, big, read: true);
        Thread.Sleep(20);                       // let it get going
        RunToCompletion(node, small);
        Thread.Sleep(uncancelled * 2);          // long enough for a stale result to slip out

        Assert.Equal(4, Assert.Single(received).Count);
    }

    [Fact]
    public void Dispose_cancels_the_read_and_nothing_is_published_afterwards()
    {
        var node = new GeoJsonReaderReactiveNode();
        var received = new List<Spread<NtsFeature>>();
        var completed = false;
        node.Update(out _, out _, out _, out _, out _).Subscribe(s => { lock (received) received.Add(s); }, () => completed = true);

        Tick(node, TempFile(Fixtures.Synthetic(50_000)), read: true);
        Thread.Sleep(50);
        node.Dispose();
        Thread.Sleep(1_000);

        Assert.Empty(received);
        Assert.True(completed);
    }

    [Fact]
    public void The_observable_is_the_same_instance_every_frame()
    {
        // VL's change detection is reference identity; a new observable per frame would make
        // everything downstream re-subscribe sixty times a second.
        using var node = new GeoJsonReaderReactiveNode();

        var first = node.Update(out _, out _, out _, out _, out _);
        var second = node.Update(out _, out _, out _, out _, out _, new VLPath(Fixtures.PathOf(Fixtures.Points)), read: true);

        Assert.Same(first, second);
    }
}
