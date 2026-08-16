using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace VL.GeoJSON.Tests;

/// <summary>
/// The repository-local fixtures, and the generator for the large ones.
/// </summary>
/// <remarks>
/// <para><b>The generator lives here and not in the package.</b> It exists so the load tests have
/// something to chew on without committing a 27 MB file, and a fixture generator is not something a
/// patch author has any use for. Test convenience must not become public API.</para>
/// <para>Fixtures A to E are checked in because they are small, hand-written, and readable — the
/// point of them is that you can look at the file and the assertion side by side. F is generated,
/// because the point of it is size.</para>
/// </remarks>
internal static class Fixtures
{
    /// <summary>Where the .geojson files land next to the test assembly.</summary>
    static string Dir => Path.Combine(AppContext.BaseDirectory, "fixtures");

    internal static string Read(string name) => File.ReadAllText(Path.Combine(Dir, name));

    /// <summary>
    /// Look a property up without throwing when it is not there.
    /// </summary>
    /// <remarks>
    /// <c>IAttributesTable</c>'s indexer throws <c>ArgumentException</c> for a name it does not
    /// have (measured), which is the whole reason the <c>Split</c> node hands a patch a Dictionary
    /// instead. The tests need the same courtesy: fixture D deliberately contains a feature with
    /// no properties at all, and a bare indexer turns that into a crash in every test that filters
    /// over the collection.
    /// </remarks>
    internal static object? Get(NetTopologySuite.Features.IFeature feature, string name)
        => feature.Attributes is { } table && table.Exists(name) ? table[name] : null;

    internal const string Points = "A-points.geojson";
    internal const string Polygons = "B-polygons.geojson";
    internal const string MultiPolygon = "C-multipolygon.geojson";
    internal const string Mixed = "D-mixed.geojson";
    internal const string Malformed = "E-malformed.geojson";

    /// <summary>
    /// Fixture F: a synthetic FeatureCollection of <paramref name="count"/> polygons, each with
    /// <paramref name="vertices"/> vertices and three properties.
    /// </summary>
    /// <remarks>
    /// Deliberately built as text rather than by serializing NTS geometry, so that a bug in the
    /// writer cannot quietly become a bug in the reader's test data.
    /// </remarks>
    internal static string Synthetic(int count, int vertices = 20)
    {
        var sb = new StringBuilder(count * vertices * 24);
        sb.Append("{\"type\":\"FeatureCollection\",\"features\":[");

        for (var i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(',');

            var cx = (i % 200) * 0.01 - 1.0;
            var cy = (i / 200) * 0.01 - 1.0;

            sb.Append("{\"type\":\"Feature\",\"geometry\":{\"type\":\"Polygon\",\"coordinates\":[[");
            for (var v = 0; v <= vertices; v++)
            {
                // v == vertices repeats v == 0, which is what closes the ring.
                var a = 2 * Math.PI * (v % vertices) / vertices;
                if (v > 0) sb.Append(',');
                sb.Append('[')
                  .Append((cx + 0.004 * Math.Cos(a)).ToString("F6", CultureInfo.InvariantCulture))
                  .Append(',')
                  .Append((cy + 0.004 * Math.Sin(a)).ToString("F6", CultureInfo.InvariantCulture))
                  .Append(']');
            }
            sb.Append("]]},\"properties\":{\"id\":").Append(i)
              .Append(",\"name\":\"feature ").Append(i)
              .Append("\",\"category\":\"c").Append(i % 7).Append("\"}}");
        }

        return sb.Append("]}").ToString();
    }
}
