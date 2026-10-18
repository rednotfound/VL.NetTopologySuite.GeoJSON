using System.Linq;
using NetTopologySuite.Features;
using Xunit;

namespace VL.NetTopologySuite.GeoJSON.Tests;

/// <summary>
/// Property values, and the normalisation that makes them usable in a patch.
/// </summary>
/// <remarks>
/// <para>This is the suite that would go red if <c>Defaults.NormaliseValue</c> were removed, which is
/// what makes it a check rather than decoration. Without it every number is a
/// <c>System.Decimal</c> — a type VL has no nodes for — and every nested object is an immutable
/// <c>JsonElementAttributesTable</c> that throws when written to.</para>
/// <para>It reads the attributes table directly. Opening a feature in a patch is VL.NetTopologySuite's
/// <c>Split</c> and <c>GetAttribute</c>, tested in that repository; what is this package's to test
/// is what those nodes are handed.</para>
/// </remarks>
public class PropertyTests
{
    static IAttributesTable PropertiesOf(string fixture, string name)
    {
        var collection = new ReadGeoJsonNode().Update(out _, out _, out _, Fixtures.Read(fixture));
        return collection.Single(f => Equals(Fixtures.Get(f, "name"), name)).Attributes;
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
    public void A_json_null_stays_a_null_entry_for_the_feature_nodes_to_drop()
    {
        // Berlin's "nickname" is null in the fixture. The table keeps it as written, and
        // VL.NetTopologySuite's Split leaves it out of the Dictionary while GetAttribute reports
        // Success false - one rule for "absent", applied where the feature is opened, whoever
        // produced it.
        var properties = PropertiesOf(Fixtures.Points, "Berlin");

        Assert.True(properties.Exists("nickname"));
        Assert.Null(properties["nickname"]);
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

        Assert.Equal(9007199254740993L, Assert.IsType<long>(collection.Single().Attributes["osm_id"]));
    }

    [Fact]
    public void A_nested_object_becomes_a_mutable_attributes_table()
    {
        var collection = new ReadGeoJsonNode().Update(out _, out _, out _, Fixtures.Read(Fixtures.MultiPolygon));

        var meta = Assert.IsType<AttributesTable>(collection.Single().Attributes["meta"]);

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

        var tags = Assert.IsType<object[]>(collection.Single().Attributes["tags"]);

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

        Assert.False(properties.Exists("id"));
    }

    [Fact]
    public void Properties_listed_in_a_different_order_keep_their_own_values()
    {
        // Normalisation shares one string per property name across a document, guessing that the
        // name at the same position as in the last feature is the same one. This is the case the
        // guess gets wrong: same names, other order, a name that is new, and one that is missing.
        const string shuffled = """
            {"type":"FeatureCollection","features":[
              {"type":"Feature","geometry":{"type":"Point","coordinates":[0,0]},
               "properties":{"name":"first","kind":"a","rank":1}},
              {"type":"Feature","geometry":{"type":"Point","coordinates":[1,1]},
               "properties":{"rank":2,"name":"second","extra":true}},
              {"type":"Feature","geometry":{"type":"Point","coordinates":[2,2]},
               "properties":{"kind":"c","name":"third","rank":3}}]}
            """;

        var features = new ReadGeoJsonNode().Update(out _, out _, out _, shuffled);

        Assert.Equal(["name", "kind", "rank"], features[0].Attributes.GetNames());
        Assert.Equal(["rank", "name", "extra"], features[1].Attributes.GetNames());
        Assert.Equal(["kind", "name", "rank"], features[2].Attributes.GetNames());
        Assert.Equal("second", features[1].Attributes["name"]);
        Assert.Equal(true, features[1].Attributes["extra"]);
        Assert.Equal("c", features[2].Attributes["kind"]);
        Assert.Equal(3L, features[2].Attributes["rank"]);
        Assert.False(features[1].Attributes.Exists("kind"));
    }
}
