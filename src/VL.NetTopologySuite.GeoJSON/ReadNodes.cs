using System;
using System.Text.Json;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using VL.Core.Import;
using VL.Lib.Collections;

using NtsFeature = NetTopologySuite.Features.Feature;

namespace VL.NetTopologySuite.GeoJSON;

/// <summary>
/// Parse GeoJSON text into a spread of features. Reports failure instead of throwing.
/// </summary>
/// <remarks>
/// <para><b>This is a process node, and that is the whole point of it.</b> A <c>public static</c>
/// method in VL is a node evaluated sixty times a second from the moment the document opens.
/// Parsing 10,000 polygons takes <b>122 ms</b> (measured, 5.4 MB of GeoJSON) against a frame budget
/// of 16.7 ms, so a static <c>Read GeoJSON</c> would not be slow — it would be a patch that never
/// draws a frame again. 50,000 features take 629 ms.</para>
/// <para>So the text is parsed when it <i>changes</i>, and the same <c>Spread</c> instance is
/// returned on every frame in between. Returning the same instance matters as much as skipping the
/// parse: object identity is the only "has this changed" signal VL has, so an unchanged spread lets
/// everything downstream — VL.NetTopologySuite's <c>Split</c>, and VL.Mapsui's <c>FeatureLayer</c> — skip its
/// own work too.</para>
/// <para><b>The features come out directly; there is no <c>FeatureCollection</c> to open first.</b>
/// That follows vvvv's own document readers, which never hand a patch a document type — see
/// <see cref="Defaults.Normalise(FeatureCollection)"/>. A <c>Spread</c> is what VL patches with, so
/// <c>GetSlice</c>, <c>Count</c> and a <c>ForEach</c> region all work on this output with no node
/// of ours in between, and it satisfies VL.Mapsui's <c>IEnumerable&lt;Feature&gt;</c> pin unchanged.</para>
/// <para><b>Watch the <c>Parses</c> pin.</b> It should reach 1 and stay there. "This only runs once"
/// is not a claim anyone should take on trust, and a rate is invisible unless something counts it —
/// the same reasoning behind VL.Mapsui's <c>Layers Built</c>.</para>
/// <para><b>For a small file, compose.</b> VL.CoreLib ships <c>FileReader (String) [IO]</c>, with
/// an explicit <c>Read</c> trigger pin, and <c>FileReader → Read GeoJSON</c> works with anything
/// else that produces text too. <b>For a big file, use <see cref="GeoJsonReaderReactiveNode"/>.</b>
/// This node parses on the main loop, and while it parses vvvv stops: measured in vvvv 7.4, the
/// 7,342 cities shipped with this package freeze it for 0.56 s.</para>
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

    /// <summary>
    /// Parse GeoJSON text. Uses NetTopologySuite.IO.GeoJSON4STJ.
    /// </summary>
    /// <remarks>
    /// <para>Accepts a whole GeoJSON <c>FeatureCollection</c> document and hands back the features
    /// inside it. Every GeoJSON geometry type
    /// survives intact and native — <c>Point</c>, <c>MultiPoint</c>, <c>LineString</c>,
    /// <c>MultiLineString</c>, <c>Polygon</c>, <c>MultiPolygon</c> and <c>GeometryCollection</c> —
    /// as is a third ordinate, so <c>[1,2,300]</c> reads back as <c>POINT Z(1 2 300)</c>.</para>
    /// <para><b>SRID is 4326 unless the <c>Factory</c> pin says otherwise</b>, because RFC 7946 §4
    /// mandates WGS84 longitude and latitude and the upstream library stamps it unaided. Note that
    /// this differs from VL.NetTopologySuite's <c>Read WKT</c>, which gives 0 — WKT carries no CRS
    /// semantics and GeoJSON does. <b>Mixing SRIDs never throws in NTS</b>, so if a patch combines
    /// geometry from both, make them agree through this pin. Setting an SRID is a label, not a
    /// reprojection, and nothing in this package reprojects anything.</para>
    /// <para><b>A legacy <c>crs</c> member is accepted and ignored</b> (measured) — RFC 7946
    /// removed it, and the SRID still comes from the <c>Factory</c> pin. A <c>bbox</c> member is
    /// read.</para>
    /// <para><b>Failure is a pin, not an exception.</b> Half-typed text is the normal state of an
    /// IOBox somebody is editing, and a node that throws sixty times a second while they type is
    /// not a diagnostic. <c>Success</c> goes false, <c>Status</c> says what happened, and the
    /// result is an empty collection.</para>
    /// <para><b>One failure is not a failure and is worth knowing about:</b> valid JSON that is
    /// not GeoJSON — <c>{"hello":"world"}</c> — parses to <b>zero features</b> with no error
    /// (measured). Nothing upstream objects, so <c>Status</c> says "0 features" and a patch author
    /// who expected data has the count to look at.</para>
    /// </remarks>
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
