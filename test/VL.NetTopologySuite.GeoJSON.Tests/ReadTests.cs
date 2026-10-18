using System.Linq;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using VL.Lib.Collections;
using Xunit;

namespace VL.GeoJSON.Tests;

/// <summary>
/// What comes out of <c>Read GeoJSON</c>: how many, of what type, with which SRID.
/// </summary>
public class ReadTests
{
    static Spread<Feature> Read(string text, GeometryFactory? factory = null)
        => new ReadGeoJsonNode().Update(out _, out _, out _, text, factory);

    static (Spread<Feature> Result, bool Success, string Status) ReadFull(
        string text, GeometryFactory? factory = null)
    {
        var result = new ReadGeoJsonNode().Update(out var success, out var status, out _, text, factory);
        return (result, success, status);
    }

    [Fact]
    public void Points_fixture_yields_four_features()
    {
        var (result, success, status) = ReadFull(Fixtures.Read(Fixtures.Points));

        Assert.True(success);
        Assert.Equal(4, result.Count);
        Assert.Equal("4 features", status);
    }

    [Theory]
    [InlineData("Point")]
    [InlineData("MultiPoint")]
    [InlineData("LineString")]
    [InlineData("MultiLineString")]
    [InlineData("Polygon")]
    [InlineData("MultiPolygon")]
    [InlineData("GeometryCollection")]
    public void Every_geojson_geometry_type_survives_as_native_nts(string geometryType)
    {
        var result = Read(Fixtures.Read(Fixtures.Mixed));

        var feature = result.Single(f => Equals(Fixtures.Get(f, "kind"), geometryType));

        Assert.NotNull(feature.Geometry);
        Assert.Equal(geometryType, feature.Geometry!.GeometryType);
        // The whole promise of the package: not a wrapper, not a parallel model.
        Assert.IsAssignableFrom<Geometry>(feature.Geometry);
    }

    [Fact]
    public void Concrete_geometry_subclasses_arrive_not_just_the_base_type()
    {
        var result = Read(Fixtures.Read(Fixtures.Mixed));

        Assert.IsType<Point>(result.Single(f => Equals(Fixtures.Get(f, "kind"), "Point")).Geometry);
        Assert.IsType<Polygon>(result.Single(f => Equals(Fixtures.Get(f, "kind"), "Polygon")).Geometry);
        Assert.IsType<MultiPolygon>(result.Single(f => Equals(Fixtures.Get(f, "kind"), "MultiPolygon")).Geometry);
    }

    [Fact]
    public void A_null_geometry_stays_null_rather_than_becoming_an_empty_one()
    {
        var result = Read(Fixtures.Read(Fixtures.Mixed));

        var feature = result.Single(f => Equals(Fixtures.Get(f, "kind"), "null geometry"));

        Assert.Null(feature.Geometry);
    }

    [Fact]
    public void A_feature_with_no_properties_member_gets_an_empty_table_not_null()
    {
        // Measured: the serializer leaves IFeature.Attributes NULL in this case, which every
        // downstream node would then have to check for. Normalisation removes the distinction.
        var result = Read(Fixtures.Read(Fixtures.Mixed));

        var bare = result.Single(f => f.Geometry is Point p && p.X == 4);

        Assert.NotNull(bare.Attributes);
        Assert.Empty(bare.Attributes!.GetNames());
    }

    [Fact]
    public void Srid_is_4326_by_default_because_rfc_7946_says_so()
    {
        var result = Read(Fixtures.Read(Fixtures.Points));

        Assert.All(result, f => Assert.Equal(4326, f.Geometry!.SRID));
    }

    [Fact]
    public void The_factory_pin_decides_the_srid()
    {
        // This is the escape hatch for a patch that also uses Read WKT, whose SRID is 0. NTS
        // never throws on a mismatch, so the patch has to make them agree deliberately.
        var result = Read(Fixtures.Read(Fixtures.Points), new GeometryFactory(new PrecisionModel(), 0));

        Assert.All(result, f => Assert.Equal(0, f.Geometry!.SRID));
    }

    [Fact]
    public void A_legacy_crs_member_is_accepted_and_ignored()
    {
        // RFC 7946 removed the crs member. The upstream library neither honours nor rejects it,
        // so the SRID still comes from the factory. Recorded as a test because silence is the
        // behaviour, and silence is what nobody would think to check.
        const string withCrs = """
            {"type":"FeatureCollection",
             "crs":{"type":"name","properties":{"name":"urn:ogc:def:crs:EPSG::3857"}},
             "features":[{"type":"Feature","geometry":{"type":"Point","coordinates":[1,2]},"properties":{}}]}
            """;

        var (result, success, _) = ReadFull(withCrs);

        Assert.True(success);
        Assert.Equal(4326, result[0].Geometry!.SRID);
    }

    [Fact]
    public void Z_survives()
    {
        var result = Read(Fixtures.Read(Fixtures.Points));

        var tokyo = result.Single(f => Equals(Fixtures.Get(f, "name"), "Tokyo"));

        Assert.Equal(40.0, tokyo.Geometry!.Coordinate.Z);
    }

    [Fact]
    public void A_polygon_hole_is_kept()
    {
        var result = Read(Fixtures.Read(Fixtures.Polygons));

        var withHole = (Polygon)result.Single(f => Equals(Fixtures.Get(f, "kind"), "with-hole")).Geometry!;

        Assert.Equal(1, withHole.NumInteriorRings);
        Assert.Equal(12.0, withHole.Area);
    }

    [Fact]
    public void A_multipolygon_keeps_all_its_parts()
    {
        var result = Read(Fixtures.Read(Fixtures.MultiPolygon));

        var mp = (MultiPolygon)result[0].Geometry!;

        Assert.Equal(3, mp.NumGeometries);
        Assert.Equal(1, ((Polygon)mp.GetGeometryN(0)).NumInteriorRings);
    }

    // ---------------------------------------------------------------- failure is a pin

    [Fact]
    public void Malformed_json_reports_on_a_pin_rather_than_throwing()
    {
        var (result, success, status) = ReadFull(Fixtures.Read(Fixtures.Malformed));

        Assert.False(success);
        Assert.Empty(result);
        Assert.StartsWith("JsonException", status);
    }

    [Fact]
    public void Valid_json_describing_invalid_geometry_is_reported_as_such()
    {
        // An unclosed ring parses as JSON perfectly well and then fails when NTS builds the
        // LinearRing. The type name on Status is what separates this from "not JSON at all".
        const string unclosed = """
            {"type":"FeatureCollection","features":[
              {"type":"Feature","geometry":{"type":"Polygon","coordinates":[[[0,0],[1,0],[1,1]]]},"properties":{}}]}
            """;

        var (_, success, status) = ReadFull(unclosed);

        Assert.False(success);
        Assert.StartsWith("ArgumentException", status);
    }

    [Fact]
    public void An_unknown_geometry_type_is_reported()
    {
        const string rhombus = """
            {"type":"FeatureCollection","features":[
              {"type":"Feature","geometry":{"type":"Rhombus","coordinates":[1,2]},"properties":{}}]}
            """;

        var (_, success, status) = ReadFull(rhombus);

        Assert.False(success);
        Assert.Contains("Rhombus", status);
    }

    [Fact]
    public void Empty_text_is_the_starting_state_not_a_failure_to_shout_about()
    {
        var (result, success, status) = ReadFull("");

        Assert.False(success);
        Assert.Empty(result);
        Assert.Equal("nothing connected", status);
    }

    [Fact]
    public void Valid_json_that_is_not_geojson_succeeds_with_zero_features()
    {
        // Documented because it is a genuine trap: nothing upstream objects, so the ONLY signal a
        // patch gets is the count. If this ever starts throwing instead, that is a behaviour
        // change worth noticing rather than a bug fix.
        var (result, success, status) = ReadFull("""{"hello":"world"}""");

        Assert.True(success);
        Assert.Empty(result);
        Assert.Equal("0 features", status);
    }
}
