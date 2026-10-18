using System;
using System.Text.Json;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using VL.Core.Import;
using VL.Lib.Collections;

using NtsFeature = NetTopologySuite.Features.Feature;

namespace VL.NetTopologySuite.GeoJSON;

// The /// text below is what vvvv shows on hover and in the NodeBrowser - user-facing, short, one
// sentence per line (a line break in the source is a line break in the tooltip). The reasoning
// lives here.
//
// THIS IS A PROCESS NODE, AND THAT IS THE WHOLE POINT OF IT. A public static method in VL is a node
// evaluated sixty times a second from the moment the document opens. Parsing 10,000 polygons takes
// 122 ms (measured, 5.4 MB) against a 16.7 ms frame, so a static Read GeoJSON would not be slow - it
// would be a patch that never draws a frame again. So the text is parsed when it CHANGES, and the
// same Spread instance is returned on every frame in between. Returning the same instance matters
// as much as skipping the parse: object identity is the only "has this changed" signal VL has, so
// an unchanged spread lets VL.NetTopologySuite's Split and VL.Mapsui's FeatureLayer skip their own
// work too.
//
// The features come out directly, with no FeatureCollection to open first: vvvv's own document
// readers never hand a patch a document type (see Defaults.Normalise). A Spread is what VL patches
// with, so GetSlice, Count and ForEach work on it with no node of ours in between, and it satisfies
// VL.Mapsui's IEnumerable<Feature> pin unchanged.
//
// Parses is a pin because "this only runs once" is not a claim anyone should take on trust - the
// reasoning behind VL.Mapsui's Layers Built.
//
// For a small file compose FileReader (String) -> Read GeoJSON. For a big file use
// GeoJsonReaderReactiveNode: this node parses on the main loop, and the 7,342 cities shipped with
// this package freeze vvvv for 0.56 s (measured in vvvv 7.4).

/// <summary>
/// Parses GeoJSON text into a spread of features.
/// </summary>
/// <remarks>
/// Parses only when the text changes; Parses counts how often that happened.
/// For a big file use GeoJSONReader (Reactive), which reads in the background.
/// </remarks>
[ProcessNode(Name = "Read GeoJSON", Category = "NetTopologySuite.IO")]
public class ReadGeoJsonNode
{
    string? _lastText;
    GeometryFactory? _lastFactory;
    Spread<NtsFeature> _result = Spread<NtsFeature>.Empty;
    bool _success;
    string _status = "nothing connected";

    /// <summary>
    /// Times the text was actually parsed. It should settle at 1 and stay there.
    /// </summary>
    internal int Parses { get; private set; }

    // Every GeoJSON geometry type survives intact and native, Z included ([1,2,300] reads back as
    // POINT Z(1 2 300)).
    //
    // SRID is 4326 unless Factory says otherwise: RFC 7946 §4 mandates WGS84 lon/lat and the
    // upstream library stamps it unaided. VL.NetTopologySuite's Read WKT gives 0, and mixing SRIDs
    // never throws in NTS, so Factory is how a patch makes them agree. An SRID is a label, not a
    // reprojection. A legacy crs member is accepted and ignored (measured); bbox is read.
    //
    // Failure is a pin, not an exception: half-typed text is the normal state of an IOBox somebody
    // is editing. And valid JSON that is not GeoJSON - {"hello":"world"} - parses to zero features
    // with no error (measured), which is why Status says "0 features".

    /// <summary>
    /// Parses the text when it changes.
    /// </summary>
    /// <param name="success">False when the text is not GeoJSON.</param>
    /// <param name="status">The number of features, or what went wrong.</param>
    /// <param name="parses">How many times the text was actually parsed. It should stay at 1.</param>
    /// <param name="geoJson">A GeoJSON FeatureCollection, as text.</param>
    /// <param name="factory">Sets the SRID. Unconnected gives 4326, which GeoJSON mandates.</param>
    [return: Pin(Name = "Features")]
    public Spread<NtsFeature> Update(
        out bool success,
        out string status,
        out int parses,
        [Pin(Name = "GeoJSON")] string? geoJson = null,
        GeometryFactory? factory = null)
    {
        var resolved = Defaults.Or(factory);

        if (string.IsNullOrWhiteSpace(geoJson))
        {
            // Nothing typed yet is the starting state, not a failure worth flagging.
            _lastText = geoJson;
            _lastFactory = resolved;
            _result = Spread<NtsFeature>.Empty;
            _success = false;
            _status = "nothing connected";
            return Report(out success, out status, out parses);
        }

        // string.Equals checks reference equality first, which is the case that actually happens:
        // a string held in a pad, or handed over by FileReader, is the same instance every frame
        // and this costs nothing. Value equality is the fallback for text rebuilt per frame -
        // comparing 27 MB costs a few milliseconds against the 629 ms of re-parsing it.
        //
        // The factory is part of the key because it decides the SRID: changing it must re-parse,
        // and comparing by reference is right because Defaults.Or hands back a shared instance.
        if (string.Equals(geoJson, _lastText, StringComparison.Ordinal) && ReferenceEquals(resolved, _lastFactory))
            return Report(out success, out status, out parses);

        _lastText = geoJson;
        _lastFactory = resolved;
        Parses++;

        try
        {
            var parsed = JsonSerializer.Deserialize<FeatureCollection>(geoJson, Defaults.ReaderOptionsFor(resolved));

            // Normalising here rather than downstream is what makes the output usable: the
            // serializer's own feature type is internal, is not NetTopologySuite.Features.Feature,
            // and carries an immutable attributes table. See Defaults.Normalise.
            _result = parsed is null ? Spread<NtsFeature>.Empty : Defaults.Normalise(parsed);
            _success = true;
            _status = $"{_result.Count} features";
        }
        catch (Exception exception)
        {
            // Deliberately broad. JsonException means the bytes are not JSON; ArgumentException
            // means they are valid JSON describing something that is not valid geometry - an
            // unclosed ring, or a geometry type GeoJSON does not have. Both measured, and from a
            // patch author's side they are one problem: the text is wrong. Status carries the
            // distinction for whoever needs it.
            _result = Spread<NtsFeature>.Empty;
            _success = false;
            _status = Defaults.Describe(exception);
        }

        return Report(out success, out status, out parses);
    }

    Spread<NtsFeature> Report(out bool success, out string status, out int parses)
    {
        success = _success;
        status = _status;
        parses = Parses;
        return _result;
    }
}
