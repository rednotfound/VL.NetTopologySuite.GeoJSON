using System;
using System.Collections.Generic;
using System.Text.Json;
using NetTopologySuite.Features;
using VL.Core.Import;

namespace VL.NetTopologySuite.GeoJSON;

// Reading is the reason this package exists; writing is here because a round trip is how you find
// out whether the reading was honest. SkipCategory so that Write GeoJSON sits directly in
// NetTopologySuite.IO beside Write WKT, rather than one level down in a category named after this
// class.

/// <summary>
/// Writes features as GeoJSON text.
/// </summary>
[SkipCategory]
public static class WriteNodes
{
    // Takes any sequence of features - the output of Read GeoJSON, or a spread built in the patch -
    // and always writes a complete FeatureCollection, which is what other tools expect.
    //
    // Exact Round Trip, on by default, is not cosmetic. RFC 7946 wants exterior rings
    // counter-clockwise and holes clockwise, and the upstream library rewinds rings on the way out
    // to match. Measured: a MultiPolygon read and written back that way has its interior ring
    // reversed - EqualsTopologically, but not EqualsExact - the kind of difference that fails a
    // test for a reason nobody can see. So the default writes the geometry the patch holds.
    //
    // No SRID goes out and nothing is reprojected: GeoJSON has nowhere to put an SRID (RFC 7946
    // removed crs and mandates WGS84), so SRID 3857 is written as though its numbers were degrees.
    // Z is written when present; a feature with no geometry is written with "geometry": null.
    // Failure is a pin, as on Read GeoJSON.

    /// <summary>
    /// Writes features as a GeoJSON FeatureCollection.
    /// </summary>
    /// <remarks>
    /// Coordinates are written as they are: GeoJSON expects longitude and latitude, and nothing is reprojected.
    /// </remarks>
    /// <param name="features">Any spread of features, for example the output of Read GeoJSON.</param>
    /// <param name="success">False when the features could not be written.</param>
    /// <param name="exactRoundTrip">On: rings are written as they are. Off: rings are rewound to the GeoJSON standard's winding order.</param>
    /// <param name="indent">Indents the text so it is easier to read.</param>
    /// <returns>The GeoJSON text.</returns>
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
