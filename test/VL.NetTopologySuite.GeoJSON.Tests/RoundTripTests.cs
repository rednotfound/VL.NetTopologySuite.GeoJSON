using System.Linq;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using VL.Lib.Collections;
using Xunit;

namespace VL.NetTopologySuite.GeoJSON.Tests;

/// <summary>
/// Write, read back, and check what survived.
/// </summary>
/// <remarks>
/// A round trip is the only way "the properties came through" stops being a claim and starts
/// being a fact. It is also where the one genuinely surprising upstream behaviour lives — ring
/// rewinding, see <see cref="Exact_round_trip_off_rewinds_rings_to_the_rfc_rule"/>.
/// </remarks>
public class RoundTripTests
{
    static Spread<Feature> Read(string text)
        => new ReadGeoJsonNode().Update(out _, out _, out _, text);

    static Spread<Feature> RoundTrip(string text, bool exact = true)
    {
        var first = Read(text);
        var written = WriteNodes.WriteGeoJson(first, out var success, exact);
        Assert.True(success);
        return Read(written);
    }

    [Fact]
    public void Geometry_survives_exactly_for_every_type_in_the_mixed_fixture()
    {
        var before = Read(Fixtures.Read(Fixtures.Mixed));
        var after = RoundTrip(Fixtures.Read(Fixtures.Mixed));

        Assert.Equal(before.Count, after.Count);

        for (var i = 0; i < before.Count; i++)
        {
            var b = before[i].Geometry;
            var a = after[i].Geometry;

            if (b is null) Assert.Null(a);
            else Assert.True(b.EqualsExact(a), $"feature {i} ({b.GeometryType}) did not survive exactly");
        }
    }

    [Fact]
    public void A_multipolygon_with_a_hole_survives_exactly()
    {
        // This is the case that fails with the library's default ring handling, which is why
        // Exact Round Trip defaults to on.
        var before = (MultiPolygon)Read(Fixtures.Read(Fixtures.MultiPolygon))[0].Geometry!;
        var after = (MultiPolygon)RoundTrip(Fixtures.Read(Fixtures.MultiPolygon))[0].Geometry!;

        Assert.True(before.EqualsExact(after));
    }

    [Fact]
    public void Exact_round_trip_off_rewinds_rings_to_the_rfc_rule()
    {
        // Documenting the upstream behaviour rather than hiding it. RFC 7946 wants a
        // counter-clockwise exterior ring and clockwise holes; enforcing that rewinds any ring
        // that disagrees. The result is topologically the same shape and NOT the same coordinate
        // sequence - a difference that would otherwise show up as a mysterious failing assertion
        // somewhere far away.
        //
        // Written inline with a deliberately CLOCKWISE exterior ring rather than using fixture C,
        // which is already wound the way the RFC wants and would therefore prove nothing. That is
        // the whole point of the exercise: a test that cannot go red is not a test, and this one
        // silently could not until the input was chosen to make it.
        const string clockwiseShell = """
            {"type":"FeatureCollection","features":[{"type":"Feature",
              "geometry":{"type":"Polygon","coordinates":[[[0,0],[0,4],[4,4],[4,0],[0,0]]]},
              "properties":{"winding":"clockwise"}}]}
            """;

        var before = (Polygon)Read(clockwiseShell)[0].Geometry!;
        var after = (Polygon)RoundTrip(clockwiseShell, exact: false)[0].Geometry!;

        Assert.False(before.EqualsExact(after));
        Assert.True(before.EqualsTopologically(after));
    }

    [Fact]
    public void Properties_survive_with_their_types()
    {
        var after = RoundTrip(Fixtures.Read(Fixtures.Points));
        var berlin = after.Single(f => Equals(Fixtures.Get(f, "name"), "Berlin"));

        Assert.Equal("DE", berlin.Attributes["country"]);
        Assert.Equal(3677472L, berlin.Attributes["population"]);
        Assert.Equal(891.7, (double)berlin.Attributes["area_km2"], 6);
        Assert.Equal(true, berlin.Attributes["capital"]);
    }

    [Fact]
    public void Z_survives_the_round_trip()
    {
        var after = RoundTrip(Fixtures.Read(Fixtures.Points));
        var tokyo = after.Single(f => Equals(Fixtures.Get(f, "name"), "Tokyo"));

        Assert.Equal(40.0, tokyo.Geometry!.Coordinate.Z);
    }

    [Fact]
    public void A_null_geometry_writes_as_null_and_reads_back_as_null()
    {
        var after = RoundTrip(Fixtures.Read(Fixtures.Mixed));

        Assert.Null(after.Single(f => Equals(Fixtures.Get(f, "kind"), "null geometry")).Geometry);
    }

    [Fact]
    public void The_id_property_survives_as_a_property()
    {
        // It went in as a GeoJSON `id` member, became an ordinary property, and goes back out as
        // one. That is a documented, deliberate asymmetry rather than round-trip fidelity.
        var after = RoundTrip(Fixtures.Read(Fixtures.Points));
        var berlin = after.Single(f => Equals(Fixtures.Get(f, "name"), "Berlin"));

        Assert.Equal("berlin", berlin.Attributes["id"]);
    }

    [Fact]
    public void Writing_produces_a_FeatureCollection_document()
    {
        var written = WriteNodes.WriteGeoJson(Read(Fixtures.Read(Fixtures.Points)), out var success);

        Assert.True(success);
        Assert.Contains("\"type\":\"FeatureCollection\"", written);
    }

    [Fact]
    public void A_bare_sequence_of_features_can_be_written_too()
    {
        // Not everything handed to Write GeoJSON came from Read GeoJSON: a patch may have built
        // its features itself, in which case it holds a spread rather than a FeatureCollection.
        var factory = new GeometryFactory(new PrecisionModel(), 4326);
        var features = new[]
        {
            new Feature(factory.CreatePoint(new Coordinate(1, 2)), new AttributesTable { { "n", 1L } }),
            new Feature(factory.CreatePoint(new Coordinate(3, 4)), new AttributesTable { { "n", 2L } }),
        };

        var written = WriteNodes.WriteGeoJson(features, out var success);

        Assert.True(success);
        Assert.Equal(2, Read(written).Count);
    }

    [Fact]
    public void Writing_nothing_gives_empty_text_rather_than_throwing()
    {
        var written = WriteNodes.WriteGeoJson(null, out var success);

        Assert.False(success);
        Assert.Equal("", written);
    }

    [Fact]
    public void Indent_is_a_pin_and_it_works()
    {
        var collection = Read(Fixtures.Read(Fixtures.Points));

        Assert.DoesNotContain('\n', WriteNodes.WriteGeoJson(collection, out _, indent: false));
        Assert.Contains('\n', WriteNodes.WriteGeoJson(collection, out _, indent: true));
    }

    [Fact]
    public void No_srid_leaves_the_document_because_geojson_has_nowhere_to_put_one()
    {
        var factory = new GeometryFactory(new PrecisionModel(), 3857);
        var features = new[] { new Feature(factory.CreatePoint(new Coordinate(1, 2)), new AttributesTable()) };

        var written = WriteNodes.WriteGeoJson(features, out _);

        Assert.DoesNotContain("3857", written);
        Assert.DoesNotContain("crs", written);
        // ... and it reads back as 4326, because the reader's factory decides. Writing a
        // projected geometry to GeoJSON loses the fact that it was projected. Nothing warns.
        Assert.Equal(4326, Read(written)[0].Geometry!.SRID);
    }
}
