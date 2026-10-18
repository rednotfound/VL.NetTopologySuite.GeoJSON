using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using VL.Lib.Collections;

namespace VL.NetTopologySuite.GeoJSON;

/// <summary>
/// Read a GeoJSON file one feature at a time. Internal: the parse behind
/// <see cref="GeoJsonReaderReactiveNode"/>.
/// </summary>
/// <remarks>
/// <para><b>Why not <c>JsonSerializer.Deserialize&lt;FeatureCollection&gt;</c>, as
/// <see cref="ReadGeoJsonNode"/> does?</b> Three things a background reader needs and that call
/// cannot give (all measured 2026-10-18, see <c>docs/MEASUREMENTS.md</c>):</para>
/// <list type="bullet">
/// <item><description><b>Cancellation.</b> <c>DeserializeAsync</c> honours its token only while it
/// buffers the stream; the parse itself runs to the end. Checking a token between features stops
/// a 56,600-feature read within 0.4 ms.</description></item>
/// <item><description><b>Progress.</b> Bytes consumed over file length, after every
/// feature.</description></item>
/// <item><description><b>Memory.</b> No text is ever made: UTF-8 bytes go straight to
/// <see cref="Utf8JsonReader"/>, where a string would be twice the file (UTF-16), and nothing is
/// kept once the read is done.</description></item>
/// </list>
/// <para>Each feature still goes through the upstream converter
/// (<c>JsonSerializer.Deserialize&lt;IFeature&gt;</c> with the same options) and through
/// <see cref="Defaults.Normalise(FeatureCollection)"/>'s per-feature step, so a file read here and
/// the same text through <c>Read GeoJSON</c> give identical features — the tests compare
/// them.</para>
/// </remarks>
internal static class GeoJsonFile
{
    /// <summary>The UTF-8 byte order mark, which <see cref="Utf8JsonReader"/> rejects.</summary>
    static ReadOnlySpan<byte> Bom => [0xEF, 0xBB, 0xBF];

    internal static Spread<global::NetTopologySuite.Features.Feature> Read(
        string path, GeometryFactory factory, Action<float> progress, CancellationToken cancellation)
    {
        try
        {
            return ReadCore(path, factory, progress, cancellation);
        }
        catch (JsonException exception) when (exception.GetType() != typeof(JsonException))
        {
            // A bare Utf8JsonReader throws the internal JsonReaderException where the serializer
            // wraps the same fault as JsonException. Status names the type, so say what
            // Read GeoJSON says about the same bytes.
            throw new JsonException(exception.Message, exception);
        }
    }

    static Spread<global::NetTopologySuite.Features.Feature> ReadCore(
        string path, GeometryFactory factory, Action<float> progress, CancellationToken cancellation)
    {
        var bytes = File.ReadAllBytes(path);
        cancellation.ThrowIfCancellationRequested();

        ReadOnlySpan<byte> json = bytes;
        if (json.StartsWith(Bom))
            json = json[Bom.Length..];

        var options = Defaults.ReaderOptionsFor(factory);
        var builder = new SpreadBuilder<global::NetTopologySuite.Features.Feature>();
        var names = new Defaults.NameCache();
        var reader = new Utf8JsonReader(json);
        var length = Math.Max(1, json.Length);

        // The document's own `null` reads as no features, as it does through Read GeoJSON.
        if (!reader.Read() || reader.TokenType == JsonTokenType.Null)
            return Spread<global::NetTopologySuite.Features.Feature>.Empty;
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"A GeoJSON file must hold a FeatureCollection object, found {reader.TokenType}.");

        string? type = null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("features"u8))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.Null)
                    continue;
                if (reader.TokenType != JsonTokenType.StartArray)
                    throw new JsonException($"\"features\" must be an array, found {reader.TokenType}.");

                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var feature = JsonSerializer.Deserialize<IFeature>(ref reader, options);
                    builder.Add(Defaults.Normalise(feature!, names));
                    progress((float)reader.BytesConsumed / length);
                }
            }
            else if (reader.ValueTextEquals("type"u8))
            {
                reader.Read();
                type = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            }
            else
            {
                // bbox, a legacy crs, foreign members: Read GeoJSON ignores them too.
                reader.Read();
                reader.Skip();
            }
        }

        // Read GeoJSON says the same about a bare geometry or a single Feature at the root. The
        // member may come after "features", so it can only be judged at the end.
        if (type is not null && type != "FeatureCollection")
            throw new JsonException($"A GeoJSON file must hold a FeatureCollection, this one is a {type}.");

        progress(1f);
        return builder.ToSpread();
    }
}
