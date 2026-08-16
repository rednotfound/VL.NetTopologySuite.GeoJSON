using System.Collections.Immutable;
using VL.Core.Import;

using NtsFeature = NetTopologySuite.Features.Feature;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace VL.GeoJSON;

/// <summary>
/// Opening up one feature: its geometry, its properties, and one property by name.
/// </summary>
/// <remarks>
/// <para><b>Every opaque value needs a reader node.</b> A <c>Feature</c> sitting on a pin is
/// otherwise a name with no way to see inside, and this is where the whole package would stop being
/// usable. <c>Split</c> is the name the Gray Book gives this and the one a user types — measured
/// across the 45 packs shipped with vvvv 7.4, <b>247 distinct categories</b> define one.</para>
/// <para><b>There is deliberately no <c>Split</c> for a collection.</b> <c>Read GeoJSON</c> hands
/// back a <c>Spread</c> directly, so there is nothing to unwrap. That is not a shortcut, it is the
/// shipped convention twice over: measured across 815 shipped <c>.vl</c> files, <c>Split</c> is
/// <b>always</b> field projection on a record — its output pins are the type's own member names —
/// and it is <b>never</b> used to turn a collection into a <c>Spread</c>. When vvvv does that it
/// says <c>FromSequence</c> or <c>ToSpread</c>. An earlier version of this package had a second
/// <c>Split</c> taking a <c>FeatureCollection</c> and producing <c>Result</c> and <c>Count</c>;
/// it broke that rule three ways at once and is gone.</para>
/// <para><b>The types crossing this boundary are NetTopologySuite's own.</b> No <c>VLFeature</c>,
/// no <c>GeoFeature</c>, no wrapper. <c>Feature</c> and <c>Geometry</c> go straight into
/// VL.NetTopologySuite's operations and into VL.Mapsui's <c>FeatureLayer</c>, and neither of those
/// packages has to know this one exists.</para>
/// <para><b><c>SkipCategory</c>, not <c>[Name(...)]</c>.</b> A named static class becomes a
/// category level of its own, which would put these in <c>GeoJSON.FeatureNodes</c> beside where
/// they belong. This package is small enough that one flat <c>GeoJSON</c> category is right.</para>
/// <para>Static operations: neither holds a resource, and neither is compared downstream by
/// identity the way <c>Read GeoJSON</c>'s output is.</para>
/// </remarks>
[SkipCategory]
public static class FeatureNodes
{
    /// <summary>
    /// A feature taken apart: its geometry, and its properties as a Dictionary.
    /// Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// <para><b>The properties come out as VL's own Dictionary</b>, because NTS keeps them in an
    /// <c>IAttributesTable</c>, which VL has no nodes for — a feature would arrive carrying the
    /// very thing that makes it interesting and no way to read it. As a Dictionary, VL's shipped
    /// <c>TryGetValue</c> works on it, and so does anything else in
    /// <c>Collections.Dictionary</c>.</para>
    /// <para>Use this when you want <i>all</i> the properties. To pull out <i>one</i> by name, use
    /// <see cref="GetProperty"/>. Both exist on purpose and neither replaces the other: vvvv's own
    /// XML library ships exactly the same pair — <c>XElement (Split)</c> hands over
    /// <c>Attributes</c> as a spread, and <c>GetAttributeValue</c> takes one by name.</para>
    /// <para><b>What a property value actually is.</b> <c>Read GeoJSON</c> normalises them to types
    /// VL has nodes for: JSON strings arrive as <c>String</c>, whole numbers as <c>Int64</c>,
    /// fractional numbers as <c>Float64</c>, <c>true</c>/<c>false</c> as <c>Boolean</c>, and
    /// <c>null</c> as nothing at all. A nested JSON object arrives as another
    /// <c>AttributesTable</c> and a JSON array as an object array — both are rarer, and both are
    /// reachable through VL's raw .NET nodes rather than being given nodes here.</para>
    /// <para>A GeoJSON <c>id</c> member appears as an ordinary <c>"id"</c> property.</para>
    /// <para>An empty dictionary rather than nothing when a feature has no properties: absence
    /// would have to be checked before every lookup, and there is nothing to distinguish — a
    /// feature with no properties and a feature with an empty table say the same thing.</para>
    /// </remarks>
    [Name("Split")]
    public static void SplitFeature(
        NtsFeature? feature,
        out NtsGeometry? geometry,
        out ImmutableDictionary<string, object> properties)
    {
        geometry = feature?.Geometry;

        var builder = ImmutableDictionary.CreateBuilder<string, object>();

        if (feature?.Attributes is { } table)
            foreach (var name in table.GetNames())
                if (table[name] is { } value)
                    builder[name] = value;

        properties = builder.ToImmutable();
    }

    /// <summary>
    /// One property of a feature, by name. Reports whether it was there instead of throwing.
    /// Uses NetTopologySuite.
    /// </summary>
    /// <remarks>
    /// <para><b><c>Success</c> is the whole point of this node.</b> NTS's
    /// <c>IAttributesTable</c> indexer <b>throws <c>ArgumentException</c></b> for a name it does not
    /// have (measured) — and a node that throws sixty times a second while somebody types a name
    /// into an IOBox is not a diagnostic, it is a broken patch. So a missing name is reported, not
    /// raised, and <c>Value</c> is nothing.</para>
    /// <para>This is vvvv's own convention rather than an invention here. Measured against
    /// <c>VL.CoreLib\VL.Xml.vl</c>: <c>GetAttributeValue</c>, <c>GetValue</c>, <c>GetValues</c>,
    /// <c>GetAttributeValues</c>, <c>XPathGetValue</c> and <c>XPathGetAttributeValue</c> all take
    /// <c>Input</c> plus a name and all return <c>Value</c> plus <c>Success</c>. The house rule is
    /// legible: <b>a <c>Get…</c> node carries a <c>Success</c> pin and never throws</b>, while a
    /// non-<c>Get</c> accessor hands back a raw, possibly-null object and carries none.</para>
    /// <para><c>Value</c> is an <c>Object</c> because a GeoJSON property genuinely can be any of
    /// String, Int64, Float64 or Boolean, and which one is a fact about the document rather than
    /// about the node. Cast it with VL's raw .NET nodes, or read the whole table with
    /// <see cref="SplitFeature"/> where <c>TryGetValue</c> is waiting.</para>
    /// <para><b>A property whose JSON value is <c>null</c> reports <c>Success</c> false</b>, the
    /// same as one that is absent. A patch cannot act on a null differently from a missing value,
    /// and pretending the two differ would only mean two checks instead of one.</para>
    /// </remarks>
    [Name("GetProperty")]
    public static object? GetProperty(NtsFeature? feature, string? name, out bool success)
    {
        // Exists() first, deliberately: the indexer is what throws, and asking before reading is
        // the only way to answer "is it there?" without catching an exception per frame.
        if (feature?.Attributes is { } table &&
            !string.IsNullOrEmpty(name) &&
            table.Exists(name) &&
            table[name] is { } value)
        {
            success = true;
            return value;
        }

        success = false;
        return null;
    }
}
