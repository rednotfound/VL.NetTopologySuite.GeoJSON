using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using VL.Lib.Collections;
using Xunit;

namespace VL.NetTopologySuite.GeoJSON.Tests;

/// <summary>
/// The architectural hypothesis, as an assertion: native shared NTS types are enough, and no
/// <c>VL.GIS.Core</c> abstraction layer is needed for these packages to compose.
/// </summary>
/// <remarks>
/// <para><b>Nothing here references VL.Mapsui or VL.NetTopologySuite, and that is the point.</b>
/// The dependency must point one way — this package knows about NetTopologySuite, and neither
/// sibling knows this package exists. So the sibling's requirements are restated locally as the
/// exact signatures they declare, and the test proves our output satisfies them.</para>
/// <para>If VL.Mapsui ever changes those signatures, these tests keep passing while the real
/// integration breaks. That is a known limit of testing an interface by transcription, and it is
/// why the integration is also verified for real in a cross-package example patch that lives
/// outside both repositories. See <c>docs/ARCHITECTURE.md</c>.</para>
/// </remarks>
public class InteropTests
{
    /// <summary>
    /// VL.Mapsui's <c>FeatureLayer</c> pin, transcribed from
    /// <c>vl-mapsui\src\VL.Mapsui\FeatureLayerNodes.cs</c> as of 2026-08-16:
    /// <c>public ILayer? Update(out int layersBuilt, IEnumerable&lt;NtsFeature&gt;? features, ...)</c>
    /// where <c>NtsFeature = NetTopologySuite.Features.Feature</c> — the CONCRETE class.
    /// </summary>
    static int FeatureLayerAccepts(IEnumerable<Feature>? features)
        => features?.Count(f => f?.Geometry is not null) ?? 0;

    /// <summary>
    /// VL.Mapsui's <c>Feature</c> / <c>Split</c> pair, and its <c>ToMapsui</c> adapter, both of
    /// which read attributes through <c>IAttributesTable</c>.
    /// </summary>
    static IEnumerable<string> AttributeNamesAsMapsuiWouldReadThem(Feature feature)
        => feature.Attributes is null ? [] : feature.Attributes.GetNames();

    [Fact]
    public void Read_GeoJSON_feeds_VL_Mapsui_FeatureLayer_with_nothing_in_between()
    {
        // This is the whole pipeline from the brief, minus the rendering:
        //   GeoJSON -> VL.GeoJSON -> NTS Feature -> VL.Mapsui FeatureLayer
        //
        // Note what is NOT in that chain any more: a node of ours to open a collection. Read
        // GeoJSON hands back the features, and the features ARE what the pin takes.
        var features = new ReadGeoJsonNode().Update(out _, out _, out _, Fixtures.Read(Fixtures.Points));

        Assert.Equal(4, features.Count);
        Assert.Equal(4, FeatureLayerAccepts(features));
    }

    [Fact]
    public void The_features_are_the_concrete_NTS_Feature_which_is_what_the_pin_demands()
    {
        // The serializer's own type is an internal StjFeature implementing IFeature only, and it
        // would NOT satisfy IEnumerable<Feature>. Defaults.Normalise is what closes that gap.
        // Removing it turns this test red, which is what makes it a check.
        var features = new ReadGeoJsonNode().Update(out _, out _, out _, Fixtures.Read(Fixtures.Points));

        Assert.All(features, f => Assert.IsType<Feature>(f));
        Assert.IsAssignableFrom<IEnumerable<Feature>>(features);
    }

    [Fact]
    public void A_Spread_is_what_VL_patches_with_and_it_still_satisfies_the_pin()
    {
        // Spread<T> is an IReadOnlyList<T>, so it costs nothing at the package boundary while
        // giving a patch GetSlice, Count and ForEach for free. That is why Read GeoJSON returns
        // one rather than a bare sequence or a FeatureCollection.
        var features = new ReadGeoJsonNode().Update(out _, out _, out _, Fixtures.Read(Fixtures.Points));

        Assert.IsType<Spread<Feature>>(features);
        Assert.IsAssignableFrom<IReadOnlyList<Feature>>(features);
    }

    [Fact]
    public void Attributes_are_readable_the_way_Mapsui_reads_them()
    {
        // Mapsui's ToMapsui walks GetNames() and copies through the indexer. The immutable
        // JsonElementAttributesTable would survive that, but the FeatureLayer's own
        // SameAttributes comparison and the Split node both need real values.
        var features = new ReadGeoJsonNode().Update(out _, out _, out _, Fixtures.Read(Fixtures.Points));
        var berlin = features.Single(f => Equals(Fixtures.Get(f, "name"), "Berlin"));

        var names = AttributeNamesAsMapsuiWouldReadThem(berlin).ToArray();

        Assert.Contains("name", names);
        Assert.Contains("population", names);
        Assert.Equal("Berlin", berlin.Attributes["name"]);
        Assert.Equal(3677472L, berlin.Attributes["population"]);

        // A JSON null stays in the table as a null value rather than being dropped, because the
        // document did say something and the table is the faithful copy. The Split node is where
        // it disappears, since a Dictionary entry holding null is worse than no entry at all -
        // TryGetValue would succeed and hand the patch nothing.
        Assert.Contains("nickname", names);
        Assert.Null(berlin.Attributes["nickname"]);
    }

    [Fact]
    public void The_attributes_table_is_the_mutable_one_Mapsui_and_a_patch_can_work_with()
    {
        var features = new ReadGeoJsonNode().Update(out _, out _, out _, Fixtures.Read(Fixtures.Points));

        var table = Assert.IsType<AttributesTable>(features.First().Attributes);

        // Would throw NotSupportedException on the serializer's own table.
        table["name"] = "changed";
        Assert.Equal("changed", table["name"]);
    }

    [Fact]
    public void Geometry_goes_straight_into_a_NetTopologySuite_operation()
    {
        // Standing in for VL.NetTopologySuite's Buffer / Area / Intersection nodes, which take
        // and return exactly this type. No conversion step exists anywhere in this chain.
        var features = new ReadGeoJsonNode().Update(out _, out _, out _, Fixtures.Read(Fixtures.Polygons));

        var geometry = features.First().Geometry;

        Geometry buffered = geometry.Buffer(1.0);

        Assert.True(buffered.Area > geometry.Area);
        Assert.Equal(4.0, geometry.Area);
    }

    [Fact]
    public void A_feature_with_no_geometry_is_skipped_by_the_layer_rather_than_crashing_it()
    {
        var features = new ReadGeoJsonNode().Update(out _, out _, out _, Fixtures.Read(Fixtures.Mixed));

        Assert.Equal(9, features.Count);                  // every feature comes through
        Assert.Equal(8, FeatureLayerAccepts(features));   // the null-geometry one is not drawn
    }

    [Fact]
    public void Nothing_connected_gives_an_empty_spread_rather_than_null()
    {
        // A null would have to be checked by every downstream node, and VL.Mapsui's pin would
        // simply see "no features" either way. An empty spread says the same thing without the
        // check.
        var features = new ReadGeoJsonNode().Update(out _, out _, out _, null);

        Assert.Empty(features);
        Assert.Equal(0, FeatureLayerAccepts(features));
    }
}
