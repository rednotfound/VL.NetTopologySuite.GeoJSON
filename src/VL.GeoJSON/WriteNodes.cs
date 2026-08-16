using System;
using System.Collections.Generic;
using System.Text.Json;
using NetTopologySuite.Features;
using VL.Core.Import;

namespace VL.GeoJSON;

/// <summary>
/// Writing features back out as GeoJSON text.
/// </summary>
/// <remarks>
/// <para>Reading is the reason this package exists; writing is here because <b>a round trip is how
/// you find out whether the reading was honest</b>. Without it, "the properties survived" is a
/// claim with nothing behind it.</para>
/// <para><c>SkipCategory</c> for the same reason as <see cref="FeatureNodes"/>: one flat
/// <c>GeoJSON</c> category.</para>
/// </remarks>
[SkipCategory]
public static class WriteNodes
{
    /// <summary>
    /// Write features as a GeoJSON FeatureCollection document.
    /// Uses NetTopologySuite.IO.GeoJSON4STJ.
    /// </summary>
    /// <remarks>
    /// <para>Takes anything that is a sequence of features, so both a <c>FeatureCollection</c>
    /// straight out of <c>Read GeoJSON</c> and a spread of features built in the patch connect
    /// here. The output is always a complete <c>FeatureCollection</c> document, which is what
    /// other tools expect to be handed.</para>
    /// <para><b><c>Exact Round Trip</c>, on by default, and it is not cosmetic.</b> RFC 7946 says a
    /// polygon's exterior ring should wind counter-clockwise and its holes clockwise, and the
    /// upstream library rewinds rings on the way out to match. Measured: a MultiPolygon read and
    /// written back with that on has its interior ring <b>reversed</b> — the result is
    /// <c>EqualsTopologically</c> to the original but <b>not</b> <c>EqualsExact</c>. That is the
    /// kind of difference that makes a test fail for a reason nobody can see, so the default here
    /// is to write the geometry the patch actually holds. Switch it off to emit strictly
    /// RFC-wound rings for a consumer that insists.</para>
    /// <para><b>No SRID goes out, and no reprojection happens.</b> GeoJSON has nowhere to put an
    /// SRID — RFC 7946 removed the <c>crs</c> member and mandates WGS84 longitude and latitude —
    /// so a geometry carrying SRID 3857 is written as though its numbers were degrees, because
    /// that is all the format can say. This node does not reproject and will not pretend to; if
    /// the coordinates are not lon/lat, transform them first with a package whose job that is.</para>
    /// <para>Z is written when present, as GeoJSON's optional third ordinate.</para>
    /// <para>A feature with no geometry is written with <c>"geometry": null</c>, which is legal
    /// GeoJSON and what was most likely read in.</para>
    /// <para>Failure is a pin, matching <c>Read GeoJSON</c>: <c>Success</c> goes false and the
    /// text goes empty rather than an exception reaching the patch.</para>
    /// </remarks>
    [Name("Write GeoJSON")]
    public static string WriteGeoJson(
        IEnumerable<IFeature>? features,
        out bool success,
        [Pin(Name = "Exact Round Trip")] bool exactRoundTrip = true,
        bool indent = false)
    {
        if (features is null)
        {
            success = false;
            return "";
        }

        // Always a FeatureCollection on the way out, even though one never arrives on the way in:
        // a lone Feature is legal GeoJSON, but a collection is what tools expect a file to
        // contain, and it is what Read GeoJSON accepts, so the round trip closes.
        //
        // Built here rather than accepted as a pin, because FeatureCollection is deliberately not
        // part of this package's surface - see Defaults.Normalise. It is a detail of the wire
        // format, which is exactly where it belongs.
        var collection = new FeatureCollection();
        foreach (var feature in features)
            if (feature is not null)
                collection.Add(feature);

        try
        {
            success = true;
            return JsonSerializer.Serialize(collection, Defaults.WriterOptionsFor(exactRoundTrip, indent));
        }
        catch (Exception)
        {
            // Serializing is far less likely to fail than parsing, but "far less likely" is not
            // "cannot": a property value of a type System.Text.Json has no converter for would do
            // it. The patch gets a false pin rather than an exception on a frame.
            success = false;
            return "";
        }
    }
}
