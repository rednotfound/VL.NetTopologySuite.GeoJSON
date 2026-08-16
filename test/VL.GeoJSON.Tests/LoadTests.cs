using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using NetTopologySuite.Features;
using Xunit;
using Xunit.Abstractions;

namespace VL.GeoJSON.Tests;

/// <summary>
/// A <c>[Fact]</c> that only runs when <c>VLGEOJSON_LOAD=1</c>.
/// </summary>
/// <remarks>
/// The load measurements take seconds and allocate hundreds of megabytes, which is the wrong
/// trade for a suite whose whole point is that it finishes in under a second and can be run on
/// every edit. They are still checked in rather than thrown away, because a measurement nobody
/// can repeat is an anecdote.
/// <para><c>$env:VLGEOJSON_LOAD=1; dotnet test</c> to run them.</para>
/// </remarks>
public sealed class LoadFactAttribute : FactAttribute
{
    public LoadFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("VLGEOJSON_LOAD") != "1")
            Skip = "Load test. Set VLGEOJSON_LOAD=1 to run.";
    }
}

/// <summary>
/// Where the time and the memory actually go, staged so that a slow patch can be blamed on the
/// right layer.
/// </summary>
/// <remarks>
/// <para><b>The point is the staging, not the totals.</b> If 10,000 polygons are slow, the
/// question is which of file IO, JSON tokenising, geometry construction, normalisation, or the
/// map's own rendering is responsible — and a single number cannot answer that. Rendering and
/// Mapsui layer construction are deliberately absent here: they need vvvv, and guessing at them
/// from a test host is exactly the false proof this ecosystem has already made once.</para>
/// </remarks>
public class LoadTests(ITestOutputHelper output)
{
    static double Best(Action act, int runs = 3)
    {
        var best = double.MaxValue;
        for (var i = 0; i < runs; i++)
        {
            var sw = Stopwatch.StartNew();
            act();
            if (sw.Elapsed.TotalMilliseconds < best) best = sw.Elapsed.TotalMilliseconds;
        }
        return best;
    }

    [LoadFact]
    public void Scaling_by_feature_count()
    {
        output.WriteLine($"{"features",9} {"MB text",8} {"parse ms",9} {"live MB",8} {"coords",10} {"ms/frame budget",16}");

        foreach (var count in new[] { 10, 100, 1_000, 10_000, 50_000 })
        {
            var text = Fixtures.Synthetic(count);
            var node = new ReadGeoJsonNode();

            node.Update(out _, out _, out _, text);           // warm: JIT and converter caches
            var fresh = () => new ReadGeoJsonNode().Update(out _, out _, out _, text);
            var ms = Best(() => fresh());

            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var before = GC.GetTotalMemory(true);
            var kept = new ReadGeoJsonNode().Update(out _, out _, out _, text);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var live = (GC.GetTotalMemory(true) - before) / 1048576.0;

            var coords = kept.Sum(f => f.Geometry?.NumPoints ?? 0);
            output.WriteLine($"{count,9} {Encoding.UTF8.GetByteCount(text) / 1048576.0,8:F1} {ms,9:F1} {live,8:F1} {coords,10:N0} {ms / 16.7,16:F1}x");
            GC.KeepAlive(kept);
        }
    }

    [LoadFact]
    public void Stages_A_to_D_separated()
    {
        // A: file IO   B: JSON tokenising   C: GeoJSON to NTS geometry   D: feature construction
        var text = Fixtures.Synthetic(10_000);
        var utf8 = Encoding.UTF8.GetBytes(text);
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vlgeojson_load.geojson");
        System.IO.File.WriteAllText(path, text);

        try
        {
            var tIo = Best(() => System.IO.File.ReadAllText(path));

            var tTokens = Best(() =>
            {
                var reader = new System.Text.Json.Utf8JsonReader(utf8);
                while (reader.Read()) { }
            });

            var node = new ReadGeoJsonNode();
            node.Update(out _, out _, out _, text);
            var tWhole = Best(() => new ReadGeoJsonNode().Update(out _, out _, out _, text));

            // D on its own: normalising an already-parsed collection.
            var raw = System.Text.Json.JsonSerializer.Deserialize<FeatureCollection>(
                utf8, Defaults.ReaderOptionsFor(Defaults.Factory))!;
            var tNormalise = Best(() => Defaults.Normalise(raw));

            output.WriteLine($"10,000 polygons, {utf8.Length / 1048576.0:F1} MB, 210,000 coordinates");
            output.WriteLine($"  A  File.ReadAllText                 {tIo,8:F1} ms");
            output.WriteLine($"  B  Utf8JsonReader token scan only   {tTokens,8:F1} ms");
            output.WriteLine($"  C  GeoJSON to NTS  (B+C minus B)    {tWhole - tTokens - tNormalise,8:F1} ms");
            output.WriteLine($"  D  normalisation to Feature         {tNormalise,8:F1} ms");
            output.WriteLine($"     Read GeoJSON node, total         {tWhole,8:F1} ms");
            output.WriteLine($"     -> D is {tNormalise / tWhole * 100:F0}% of the node's cost");
        }
        finally { System.IO.File.Delete(path); }
    }

    [LoadFact]
    public void An_unchanged_input_costs_nothing_per_frame()
    {
        // The number that decides whether a large dataset is usable at all. Parsing is ~120 ms
        // for 10,000 features against a 16.7 ms frame; the cached path has to be free.
        var text = Fixtures.Synthetic(10_000);
        var node = new ReadGeoJsonNode();
        node.Update(out _, out _, out _, text);

        var sw = Stopwatch.StartNew();
        for (var frame = 0; frame < 600; frame++)
            node.Update(out _, out _, out _, text);
        sw.Stop();

        output.WriteLine($"600 frames on an unchanged 10,000-feature document: {sw.Elapsed.TotalMilliseconds:F1} ms total");
        output.WriteLine($"  {sw.Elapsed.TotalMilliseconds / 600:F4} ms/frame, parses = {node.Parses}");

        Assert.Equal(1, node.Parses);
        Assert.True(sw.Elapsed.TotalMilliseconds / 600 < 1.0, "the cached path must be far under a frame");
    }

    [LoadFact]
    public void Geometry_complexity_versus_feature_count()
    {
        // Two ways to arrive at the same coordinate count, to see which one costs more.
        output.WriteLine($"{"features",9} {"verts",6} {"coords",10} {"parse ms",9}");
        foreach (var (count, verts) in new[] { (10_000, 20), (2_000, 100), (1_000, 200), (200, 1_000) })
        {
            var text = Fixtures.Synthetic(count, verts);
            new ReadGeoJsonNode().Update(out _, out _, out _, text);
            var ms = Best(() => new ReadGeoJsonNode().Update(out _, out _, out _, text));
            output.WriteLine($"{count,9} {verts,6} {count * (verts + 1),10:N0} {ms,9:F1}");
        }
    }
}
