using NetTopologySuite.Geometries;
using Xunit;

namespace VL.GeoJSON.Tests;

/// <summary>
/// When the parsing happens — the thing that actually matters in VL.
/// </summary>
/// <remarks>
/// <para>Every test here is shaped like a frame loop: call <c>Update</c> many times and assert on
/// how much work got done. That shape is deliberate and carried from VL.Mapsui, where the expensive
/// bug was a lifetime bug rather than an arithmetic one.</para>
/// <para><b>Negative-tested.</b> Removing the cache check in <c>ReadGeoJsonNode.Update</c> turns
/// <see cref="A_hundred_frames_with_unchanged_text_parse_once"/> and
/// <see cref="An_unchanged_input_returns_the_same_instance"/> red — the first reports 100 parses
/// instead of 1. A check that has never failed on known-bad input is not a check.</para>
/// </remarks>
public class LifetimeTests
{
    [Fact]
    public void A_hundred_frames_with_unchanged_text_parse_once()
    {
        var node = new ReadGeoJsonNode();
        var text = Fixtures.Read(Fixtures.Points);

        for (var frame = 0; frame < 100; frame++)
            node.Update(out _, out _, out _, text);

        Assert.Equal(1, node.Parses);
    }

    [Fact]
    public void An_unchanged_input_returns_the_same_instance()
    {
        // Identity is the only "has this changed" signal VL has. Handing back a new collection
        // every frame would make every downstream node redo its work even though the parse was
        // skipped - which is most of the benefit thrown away.
        var node = new ReadGeoJsonNode();
        var text = Fixtures.Read(Fixtures.Points);

        var first = node.Update(out _, out _, out _, text);
        var second = node.Update(out _, out _, out _, text);

        Assert.Same(first, second);
    }

    [Fact]
    public void Equal_text_in_a_different_string_instance_still_does_not_reparse()
    {
        // A patch that builds its GeoJSON by concatenation produces a new string every frame.
        // Comparing 27 MB costs a few milliseconds against 629 ms to re-parse it.
        var node = new ReadGeoJsonNode();
        var text = Fixtures.Read(Fixtures.Points);

        node.Update(out _, out _, out _, text);
        node.Update(out _, out _, out _, new string(text.ToCharArray()));

        Assert.Equal(1, node.Parses);
    }

    [Fact]
    public void Changed_text_parses_again()
    {
        var node = new ReadGeoJsonNode();

        node.Update(out _, out _, out _, Fixtures.Read(Fixtures.Points));
        node.Update(out _, out _, out _, Fixtures.Read(Fixtures.Polygons));

        Assert.Equal(2, node.Parses);
    }

    [Fact]
    public void A_changed_factory_parses_again_because_it_decides_the_srid()
    {
        var node = new ReadGeoJsonNode();
        var text = Fixtures.Read(Fixtures.Points);

        var wgs84 = node.Update(out _, out _, out _, text);
        var planar = node.Update(out _, out _, out _, text, new GeometryFactory(new PrecisionModel(), 0));

        Assert.Equal(2, node.Parses);
        Assert.Equal(4326, wgs84[0].Geometry!.SRID);
        Assert.Equal(0, planar[0].Geometry!.SRID);
    }

    [Fact]
    public void A_failing_parse_is_not_retried_every_frame()
    {
        // Half-typed text is the normal state of an IOBox being edited. Retrying the parse sixty
        // times a second while somebody types is the same waste as succeeding sixty times a
        // second, and on a large document it would freeze the editor rather than the patch.
        var node = new ReadGeoJsonNode();
        var broken = Fixtures.Read(Fixtures.Malformed);

        for (var frame = 0; frame < 50; frame++)
            node.Update(out _, out _, out _, broken);

        Assert.Equal(1, node.Parses);
    }

    [Fact]
    public void Nothing_connected_never_parses_at_all()
    {
        var node = new ReadGeoJsonNode();

        for (var frame = 0; frame < 50; frame++)
            node.Update(out var success, out var status, out _, null);

        Assert.Equal(0, node.Parses);
    }

    [Fact]
    public void Recovering_from_a_failure_works()
    {
        var node = new ReadGeoJsonNode();

        node.Update(out var brokenSuccess, out _, out _, Fixtures.Read(Fixtures.Malformed));
        var recovered = node.Update(out var goodSuccess, out _, out _, Fixtures.Read(Fixtures.Points));

        Assert.False(brokenSuccess);
        Assert.True(goodSuccess);
        Assert.Equal(4, recovered.Count);
    }
}
