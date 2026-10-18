using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using NetTopologySuite;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;
using VL.Lib.Collections;

namespace VL.NetTopologySuite.GeoJSON;

/// <summary>
/// The package's default <see cref="GeometryFactory"/>, its cached serializer options, and the
/// normalisation every read node depends on. Internal: none of this is a node.
/// </summary>
/// <remarks>
/// <para><b>Why normalisation exists at all.</b> <c>NetTopologySuite.IO.GeoJSON4STJ</c> does not
/// hand back the mature public NTS types. Measured against 4.0.0:</para>
/// <list type="bullet">
/// <item><description>A decoded feature is <c>NetTopologySuite.IO.Converters.StjFeature</c>, which
/// is <b>internal</b>, implements <c>IFeature</c>, and is <b>not</b> a
/// <c>NetTopologySuite.Features.Feature</c>.</description></item>
/// <item><description>Its attributes are a <c>JsonElementAttributesTable</c>, which is
/// <b>immutable</b> — assigning through the indexer throws
/// <c>NotSupportedException</c> — and cannot be cast to <c>AttributesTable</c>.</description></item>
/// <item><description>Every JSON number arrives as <c>System.Decimal</c>, including integers.</description></item>
/// <item><description>A nested object arrives as another <c>JsonElementAttributesTable</c>; an
/// array as <c>object[]</c>.</description></item>
/// <item><description>The GeoJSON <c>id</c> member lands on <c>IUnique.Id</c> and <b>not</b> in the
/// properties.</description></item>
/// <item><description><c>IFeature.Attributes</c> is <b>null</b> when the JSON feature carries no
/// <c>properties</c> member at all.</description></item>
/// </list>
/// <para>That matters because of where these values are going. <b>VL.Mapsui's <c>FeatureLayer</c>
/// takes <c>IEnumerable&lt;NetTopologySuite.Features.Feature&gt;</c> — the concrete class</b> — so
/// a raw <c>FeatureCollection</c> from this serializer does not satisfy it, and the pipeline this
/// package exists to prove would not compile. Normalising here rather than teaching VL.Mapsui about
/// GeoJSON is what keeps the dependency pointing one way.</para>
/// <para>It is also the cheap direction to be wrong in: <c>Feature</c> implements <c>IFeature</c>,
/// so if VL.Mapsui later widens its signature this normalisation stays valid and merely becomes
/// optional. The reverse — emitting <c>IFeature</c> now — would require editing a sibling
/// repository before anything renders.</para>
/// <para><b>Measured cost: 9 ms per 10,000 features</b>, against 122 ms to parse them. 7%, for
/// types a patch can actually use.</para>
/// </remarks>
internal static class Defaults
{
    /// <summary>
    /// The factory used when a node's <c>Factory</c> pin is left unconnected. <b>SRID 4326.</b>
    /// </summary>
    /// <remarks>
    /// <para><b>This deliberately differs from VL.NetTopologySuite, whose default is 0.</b> The
    /// difference is not an oversight and not a divergence from the ecosystem — it is the two
    /// formats being genuinely different.</para>
    /// <para>WKT carries no CRS semantics, so a package reading it cannot know what its numbers
    /// mean and 0 ("unset") is the only honest answer. <b>RFC 7946 §4 mandates</b> that GeoJSON
    /// coordinates are WGS84 longitude and latitude. So 4326 here is a fact about the format
    /// rather than a guess about the data — and it is also what the upstream library already
    /// stamps unaided (measured: <c>new GeoJsonConverterFactory()</c> yields SRID 4326).
    /// Overriding it to 0 would be silently patching the library to make a number match, which
    /// is the thing not to do.</para>
    /// <para><b>The hazard, stated plainly because nothing else will state it:</b> NTS never
    /// throws on mixed SRIDs (measured — 4326 intersected with 0
    /// returns 4326 silently). A patch mixing <c>Read WKT</c> (SRID 0) with <c>Read GeoJSON</c>
    /// (SRID 4326) therefore gets a confident wrong answer that looks exactly like a right one.
    /// The <c>Factory</c> pin is how a patch makes the two agree, in whichever direction it
    /// wants.</para>
    /// <para><b>An SRID is a label, not a transformation.</b> Nothing here reprojects anything,
    /// and no node in this package ever will — that is <c>VL.ProjNet</c>'s job.</para>
    /// <para>Shared and static, which is safe: a factory holds no connection, handle or thread.</para>
    /// </remarks>
    internal const int GeoJsonSrid = 4326;

    /// <inheritdoc cref="GeoJsonSrid"/>
    internal static readonly GeometryFactory Factory =
        new GeometryFactory(new PrecisionModel(), GeoJsonSrid);

    /// <summary>Resolve a possibly unconnected <c>Factory</c> pin.</summary>
    /// <remarks>
    /// Unconnected is the only thing that can mean "the default" — rule 8 of
    /// <c>docs/RULES.md</c>, learned in a sibling at the cost of 444 stray files. A
    /// <see cref="GeometryFactory"/> pin has no "empty" state to confuse with <c>null</c>, so
    /// this is unambiguous.
    /// </remarks>
    internal static GeometryFactory Or(GeometryFactory? factory) => factory ?? Factory;

    /// <summary>
    /// One <see cref="JsonSerializerOptions"/> per factory, reused.
    /// </summary>
    /// <remarks>
    /// <para>A <c>public static</c> method is a node that runs <b>every frame</b>. Building a
    /// <see cref="GeoJsonConverterFactory"/> and a fresh <see cref="JsonSerializerOptions"/> per
    /// call would allocate forever — and worse, System.Text.Json caches its converter lookups
    /// <i>per options instance</i>, so a new instance per call throws that cache away too.</para>
    /// <para>A <see cref="ConditionalWeakTable{TKey, TValue}"/> rather than a dictionary so a
    /// factory a patch has stopped using can be collected: the <c>Factory</c> pin can be fed by
    /// anything, including a factory built through VL's raw .NET nodes, and a strong dictionary
    /// would pin every one of them for the life of the process.</para>
    /// <para><c>RingOrientationOption.DoNotModify</c> on the READ side is not a choice — reading
    /// never reorients rings anyway (measured: a clockwise exterior ring reads back clockwise).
    /// It matters only when writing, which is why <see cref="WriterOptions"/> is separate.</para>
    /// </remarks>
    private static readonly ConditionalWeakTable<GeometryFactory, JsonSerializerOptions> ReaderOptions = new();

    /// <summary>Get the shared reader options for a factory, creating them once.</summary>
    internal static JsonSerializerOptions ReaderOptionsFor(GeometryFactory factory)
        => ReaderOptions.GetValue(factory, static f => new JsonSerializerOptions
        {
            Converters = { new GeoJsonConverterFactory(f, writeGeometryBBox: false) },
        });

    /// <summary>
    /// Writer options, one per (ring orientation, indenting) combination.
    /// </summary>
    /// <remarks>
    /// <para><b>Ring orientation is the one thing that stops a GeoJSON round trip being exact.</b>
    /// Measured: with the library's default (<c>EnforceRfc9746</c> — the spelling is upstream's,
    /// and RFC 7946 is the document meant), a MultiPolygon read and written back has its interior
    /// ring reversed. The result is <c>EqualsTopologically</c> but <b>not</b> <c>EqualsExact</c>,
    /// which is exactly the kind of difference that makes a round-trip test fail for a reason
    /// nobody can see.</para>
    /// <para>So <c>Exact Round Trip</c> is a pin. On (<c>DoNotModify</c>) the bytes describe the
    /// geometry the patch actually holds; off (<c>EnforceRfc9746</c>) the output follows RFC 7946's
    /// winding rule, which is what a strict consumer may want. Neither is right for everyone,
    /// which is why it is visible rather than decided here.</para>
    /// <para>The factory is irrelevant when writing — an SRID has nowhere to go in GeoJSON — so
    /// these are plain statics rather than keyed on one.</para>
    /// </remarks>
    private static readonly JsonSerializerOptions[] WriterOptions =
    [
        Writer(RingOrientationOption.EnforceRfc9746, indent: false),
        Writer(RingOrientationOption.EnforceRfc9746, indent: true),
        Writer(RingOrientationOption.DoNotModify, indent: false),
        Writer(RingOrientationOption.DoNotModify, indent: true),
    ];

    private static JsonSerializerOptions Writer(RingOrientationOption ring, bool indent)
        => new()
        {
            WriteIndented = indent,
            Converters = { new GeoJsonConverterFactory(Factory, false, "id", ring) },
        };

    /// <summary>Get the shared writer options for the given pin settings.</summary>
    internal static JsonSerializerOptions WriterOptionsFor(bool exactRoundTrip, bool indent)
        => WriterOptions[(exactRoundTrip ? 2 : 0) + (indent ? 1 : 0)];

    /// <summary>
    /// Copy a decoded collection into a <see cref="Spread{T}"/> of concrete
    /// <see cref="NetTopologySuite.Features.Feature"/> objects with mutable
    /// <see cref="AttributesTable"/>s and VL-natural property values.
    /// </summary>
    /// <remarks>
    /// <para>The geometry itself is <b>not</b> copied or altered: it is already a native NTS
    /// geometry with the factory's SRID on it, and re-creating it would cost the one thing this
    /// package promises to hand over untouched.</para>
    /// <para><b>A <c>Spread</c> comes out, not a <c>FeatureCollection</c>, and the container type
    /// is deliberately dropped here.</b> vvvv's own document readers do the same thing: measured
    /// against <c>VL.CoreLib\VL.Xml.vl</c>, <c>XMLReader</c>, <c>Parse [XML]</c>, <c>JSONReader</c>
    /// and <c>ParseJSON</c> all end with <c>XDocument (Split) → Root</c> internally and hand a
    /// patch an <c>XElement</c>. A patch never holds a "document" type. <c>FeatureCollection</c> is
    /// our <c>XDocument</c>, it crosses no package boundary — and its only role was to be taken apart again
    /// immediately.</para>
    /// <para><b>What this costs:</b> the collection's <c>bbox</c> member. It was never surfaced by
    /// any node; when something needs it, it becomes an output pin on <c>Read GeoJSON</c>
    /// rather than a resurrected type.</para>
    /// </remarks>
    internal static Spread<global::NetTopologySuite.Features.Feature> Normalise(FeatureCollection source)
    {
        var builder = new SpreadBuilder<global::NetTopologySuite.Features.Feature>(source.Count);
        foreach (var feature in source)
            builder.Add(Normalise(feature));
        return builder.ToSpread();
    }

    /// <summary>Copy one decoded feature into a concrete <see cref="NetTopologySuite.Features.Feature"/>.</summary>
    /// <remarks>
    /// <para>Two things are carried across that a naive copy would drop.</para>
    /// <para><b>The GeoJSON <c>id</c> member.</b> It arrives on <c>IUnique.Id</c>, and the concrete
    /// <c>Feature</c> does not implement <c>IUnique</c> (measured), so copying only the attributes
    /// would silently lose it. It becomes an ordinary <c>"id"</c> property — which is exactly what
    /// the Newtonsoft reader does with it, so the two serializers agree. An <c>id</c> already
    /// present in <c>properties</c> is not overwritten; the document said something specific.</para>
    /// <para><b>A null attributes table becomes an empty one.</b> A feature with no
    /// <c>properties</c> member yields <c>Attributes == null</c> (measured), and a patch cannot
    /// tell that apart from a feature with no properties — there is nothing to distinguish. An
    /// empty table means no downstream node has to null-check.</para>
    /// </remarks>
    internal static global::NetTopologySuite.Features.Feature Normalise(IFeature feature)
    {
        var table = new AttributesTable();

        if (feature.Attributes is { } source)
            foreach (var name in source.GetNames())
                table.Add(name, NormaliseValue(source[name]));

        if (feature is IUnique { Id: { } id } && !table.Exists("id"))
            table.Add("id", NormaliseValue(id));

        return new global::NetTopologySuite.Features.Feature(feature.Geometry!, table);
    }

    /// <summary>
    /// Turn one System.Text.Json-shaped property value into something a patch can use.
    /// </summary>
    /// <remarks>
    /// <para><b>Numbers.</b> System.Text.Json's GeoJSON reader boxes every JSON number as
    /// <c>System.Decimal</c>, integers included (measured). VL has no Decimal type, so a patch
    /// would meet a value it cannot compare, add, or feed to anything. Whole numbers become
    /// <c>long</c> and the rest <c>double</c> — which is both what VL has nodes for and what the
    /// Newtonsoft reader produces, so the two serializers agree on the same JSON.</para>
    /// <para>Whole numbers go to <c>long</c> rather than <c>double</c> on purpose: an OSM way id
    /// or a census GEOID exceeds 2^53 and would come back wrong as a double. <c>decimal</c> holds
    /// them exactly, so the conversion is lossless — and where a value genuinely does not fit a
    /// <c>long</c> it stays a <c>decimal</c> rather than being silently mangled.</para>
    /// <para><b>Nested objects</b> become nested <see cref="AttributesTable"/>s, recursively, so
    /// the immutable <c>JsonElementAttributesTable</c> never reaches a patch. <b>Arrays</b> keep
    /// their <c>object[]</c> shape with every element normalised.</para>
    /// <para><b>Depth is not guarded</b>, and does not need to be: System.Text.Json refuses
    /// documents deeper than its own limit (64 by default) while parsing, so nothing this deep
    /// ever reaches here.</para>
    /// </remarks>
    internal static object? NormaliseValue(object? value) => value switch
    {
        null => null,

        // Whole and it fits: long. Whole and it does not fit: left as decimal, because turning a
        // 30-digit integer into a double would silently mangle it. Fractional: double.
        //
        // No range check is needed on the double branch - decimal tops out around 7.9e28 and
        // double at 1.7e308, so the conversion cannot overflow. Writing the symmetric-looking
        // guard `d <= (decimal)double.MaxValue` would itself throw OverflowException, since that
        // cast is the one that does not fit.
        //
        // The (object) cast on the long branch is load bearing, not noise. Without it the
        // conditional unifies its two branches to their common type - and long converts
        // implicitly to decimal - so `Convert.ToInt64(d) : d` has type DECIMAL and the long is
        // turned straight back into the thing this line exists to get rid of. It compiles, it
        // runs, and every whole number silently stays a decimal.
        decimal d when d == decimal.Truncate(d)
            => d >= long.MinValue && d <= long.MaxValue ? (object)Convert.ToInt64(d) : d,
        decimal d => Convert.ToDouble(d),

        // Nested objects arrive as another attributes table - the immutable JsonElement-backed
        // one when it came from JSON. Recursing turns the whole tree into mutable tables.
        IAttributesTable nested => NormaliseTable(nested),

        object[] array => NormaliseArray(array),

        // string, bool, and anything already ordinary. JsonElement can still appear if a caller
        // supplied their own options; ToString is a better answer than an opaque struct.
        JsonElement element => NormaliseElement(element),

        _ => value,
    };

    private static AttributesTable NormaliseTable(IAttributesTable source)
    {
        var table = new AttributesTable();
        foreach (var name in source.GetNames())
            table.Add(name, NormaliseValue(source[name]));
        return table;
    }

    private static object[] NormaliseArray(object[] source)
    {
        var copy = new object[source.Length];
        for (var i = 0; i < source.Length; i++)
            copy[i] = NormaliseValue(source[i])!;
        return copy;
    }

    private static object? NormaliseElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => element.GetString(),
        // (object) for the same reason as above: long and double unify to double, which would
        // make every integer here a double and lose exactness past 2^53.
        JsonValueKind.Number => element.TryGetInt64(out var l) ? (object)l : element.GetDouble(),
        _ => element.ToString(),
    };

    /// <summary>
    /// A one-line description of a failure, for a <c>Status</c> pin.
    /// </summary>
    /// <remarks>
    /// <para>The type name is included on purpose. <c>JsonException</c> means the bytes are not
    /// JSON; <c>ArgumentException</c> means they are valid JSON describing something that is not
    /// valid geometry — an unclosed ring, or a geometry type GeoJSON does not have (both
    /// measured). Those are different problems with different fixes, and the message alone does
    /// not always say which one happened.</para>
    /// <para>Newlines are flattened because a Status pin is one line in a patch.</para>
    /// </remarks>
    internal static string Describe(Exception exception)
        => string.Create(CultureInfo.InvariantCulture, $"{exception.GetType().Name}: {exception.Message}")
            .Replace('\r', ' ').Replace('\n', ' ');
}
