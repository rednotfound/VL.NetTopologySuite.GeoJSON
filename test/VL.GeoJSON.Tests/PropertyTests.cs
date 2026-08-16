using System.Collections.Immutable;
using System.Linq;
using NetTopologySuite.Features;
using Xunit;

namespace VL.GeoJSON.Tests;

/// <summary>
/// Property values, and the normalisation that makes them usable in a patch.
/// </summary>
/// <remarks>
/// This is the suite that would go red if <c>Defaults.NormaliseValue</c> were removed, which is
/// what makes it a check rather than decoration. Without it every number is a
/// <c>System.Decimal</c> — a type VL has no nodes for — and every nested object is an immutable
/// <c>JsonElementAttributesTable</c> that throws when written to.
/// </remarks>
public class PropertyTests
{
    static ImmutableDictionary<string, object> PropertiesOf(string fixture, string name)
    {
        var collection = new ReadGeoJsonNode().Update(out _, out _, out _, Fixtures.Read(fixture));
        var feature = collection.Single(f => Equals(Fixtures.Get(f, "name"), name));

        FeatureNodes.SplitFeature(feature, out _, out var properties);
        return properties;
    }

    [Fact]
    public void A_json_string_is_a_string()
        => Assert.Equal("DE", Assert.IsType<string>(PropertiesOf(Fixtures.Points, "Berlin")["country"]));

    [Fact]
    public void A_whole_number_is_an_int64_not_a_decimal()
    {
        // The serializer hands back System.Decimal for every JSON number, integers included.
        // VL has no Decimal, so a patch would meet a value it cannot add or compare.
        var value = PropertiesOf(Fixtures.Points, "Berlin")["population"];

        Assert.IsType<long>(value);
        Assert.Equal(3677472L, value);
    }

    [Fact]
    public void A_fractional_number_is_a_double()
    {
        var value = PropertiesOf(Fixtures.Points, "Berlin")["area_km2"];

        Assert.IsType<double>(value);
        Assert.Equal(891.7, (double)value, 6);
    }

    [Fact]
    public void A_boolean_is_a_boolean()
        => Assert.True(Assert.IsType<bool>(PropertiesOf(Fixtures.Points, "Berlin")["capital"]));

    [Fact]
    public void A_json_null_is_absent_rather_than_a_null_entry()
    {
        // Berlin's "nickname" is null in the fixture. A dictionary entry whose value is null is
        // worse than no entry: TryGetValue succeeds and hands the patch nothing.
        var properties = PropertiesOf(Fixtures.Points, "Berlin");

        Assert.False(properties.ContainsKey("nickname"));
    }

    [Fact]
    public void A_big_integer_keeps_its_exact_value()
    {
        // An OSM way id or a census GEOID exceeds 2^53 and would come back wrong as a double.
        // decimal holds it exactly, so long is a lossless destination and double would not be.
        const string big = """
            {"type":"FeatureCollection","features":[{"type":"Feature",
              "geometry":{"type":"Point","coordinates":[0,0]},
              "properties":{"osm_id":9007199254740993}}]}
            """;

        var collection = new ReadGeoJsonNode().Update(out _, out _, out _, big);
        FeatureNodes.SplitFeature(collection.Single(), out _, out var properties);

        Assert.Equal(9007199254740993L, Assert.IsType<long>(properties["osm_id"]));
    }

    [Fact]
    public void A_nested_object_becomes_a_mutable_attributes_table()
    {
        var collection = new ReadGeoJsonNode().Update(out _, out _, out _, Fixtures.Read(Fixtures.MultiPolygon));
        FeatureNodes.SplitFeature(collection.Single(), out _, out var properties);

        var meta = Assert.IsType<AttributesTable>(properties["meta"]);

        Assert.Equal("hand-written", meta["source"]);
        Assert.Equal(2L, meta["revision"]);

        // The un-normalised JsonElementAttributesTable throws NotSupportedException here. That is
        // the negative test: this line is what proves the conversion happened.
        meta["revision"] = 3L;
        Assert.Equal(3L, meta["revision"]);
    }

    [Fact]
    public void A_json_array_keeps_its_elements()
    {
        var collection = new ReadGeoJsonNode().Update(out _, out _, out _, Fixtures.Read(Fixtures.MultiPolygon));
        FeatureNodes.SplitFeature(collection.Single(), out _, out var properties);

        var tags = Assert.IsType<object[]>(properties["tags"]);

        Assert.Equal(["island", "test", "multipolygon"], tags);
    }

    [Fact]
    public void The_geojson_id_member_becomes_an_ordinary_property()
    {
        // It arrives on IUnique.Id, and the concrete Feature does not implement IUnique - so
        // copying only the attributes would silently lose it. This is also what the Newtonsoft
        // reader does, so the two serializers agree on the same document.
        var properties = PropertiesOf(Fixtures.Points, "Berlin");

        Assert.Equal("berlin", properties["id"]);
    }

    [Fact]
    public void A_feature_without_an_id_gains_no_id_property()
    {
        var properties = PropertiesOf(Fixtures.Points, "Paris");

        Assert.False(properties.ContainsKey("id"));
    }

    [Fact]
    public void Splitting_a_null_feature_gives_nothing_rather_than_throwing()
    {
        FeatureNodes.SplitFeature(null, out var geometry, out var properties);

        Assert.Null(geometry);
        Assert.Empty(properties);
    }

    // ------------------------------------------------------------ GetProperty
    //
    // The node exists because IAttributesTable's indexer THROWS on a name it does not have, and a
    // node that throws sixty times a second while somebody types is not a diagnostic. Every test
    // below is really one test: does it report instead of raising.

    static NetTopologySuite.Features.Feature Berlin()
        => new ReadGeoJsonNode()
            .Update(out _, out _, out _, Fixtures.Read(Fixtures.Points))
            .Single(f => Equals(Fixtures.Get(f, "name"), "Berlin"));

    [Fact]
    public void GetProperty_finds_a_property_and_reports_success()
    {
        var value = FeatureNodes.GetProperty(Berlin(), "population", out var success);

        Assert.True(success);
        Assert.Equal(3677472L, value);
    }

    [Fact]
    public void GetProperty_reports_a_missing_name_instead_of_throwing()
    {
        // The whole reason the node exists. Under the raw NTS indexer this line is an
        // ArgumentException; here it is a false on a pin.
        var value = FeatureNodes.GetProperty(Berlin(), "no_such_property", out var success);

        Assert.False(success);
        Assert.Null(value);
    }

    [Fact]
    public void GetProperty_treats_a_json_null_as_not_found()
    {
        // Berlin's "nickname" is null in the fixture. A patch cannot act on a null differently
        // from a missing value, so reporting them the same way means one check instead of two.
        var value = FeatureNodes.GetProperty(Berlin(), "nickname", out var success);

        Assert.False(success);
        Assert.Null(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void GetProperty_survives_an_unconnected_or_empty_name(string? name)
    {
        // An IOBox somebody has not typed into yet is the normal starting state.
        var value = FeatureNodes.GetProperty(Berlin(), name, out var success);

        Assert.False(success);
        Assert.Null(value);
    }

    [Fact]
    public void GetProperty_survives_a_null_feature()
    {
        var value = FeatureNodes.GetProperty(null, "name", out var success);

        Assert.False(success);
        Assert.Null(value);
    }

    [Fact]
    public void GetProperty_finds_the_geojson_id_member()
    {
        Assert.Equal("berlin", FeatureNodes.GetProperty(Berlin(), "id", out var success));
        Assert.True(success);
    }

    [Fact]
    public void GetProperty_agrees_with_Split_on_every_name()
    {
        // The two nodes answer different questions - take the lot, versus take one by name - but
        // they must never disagree about what is there. vvvv ships the same pair for XML
        // (XElement (Split) -> Attributes, and GetAttributeValue -> Value, Success).
        var berlin = Berlin();
        FeatureNodes.SplitFeature(berlin, out _, out var properties);

        foreach (var (key, expected) in properties)
        {
            var value = FeatureNodes.GetProperty(berlin, key, out var success);
            Assert.True(success, $"Split offered '{key}' but GetProperty did not find it");
            Assert.Equal(expected, value);
        }
    }
}
