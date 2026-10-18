using System;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using NetTopologySuite.Geometries;
using VL.Core.Import;
using VL.Lib.Collections;

using NtsFeature = NetTopologySuite.Features.Feature;
using VLPath = VL.Lib.IO.Path;

namespace VL.NetTopologySuite.GeoJSON;

/// <summary>
/// Read a GeoJSON file in the background. The features arrive on an observable; take them into
/// the main loop with <c>HoldLatest</c>.
/// </summary>
/// <remarks>
/// <para><b>Why this exists beside <c>FileReader → Read GeoJSON</c>.</b> <c>Read GeoJSON</c> parses
/// on the main loop, and while it parses nothing else in vvvv runs. Measured in vvvv 7.4: the
/// 7,342 cities this package ships freeze it for 0.56 s, 1.2 million building footprints for
/// 12.9 s. This node reads and parses on another thread, so vvvv keeps drawing.</para>
/// <para><b>Shaped like vvvv's own <c>JSONReader (Reactive)</c> and
/// <c>FileReader (String Reactive)</c></b> — the same <c>File Path</c> and <c>Read</c> in, the same
/// <c>Output</c> observable, <c>Progress</c>, <c>In Progress</c> and <c>On Completed</c> out — and
/// The Gray Book's rule for node authors: anything asynchronous is handed to the user as an
/// observable. <c>Success</c> and <c>Status</c> are added because a file can be wrong, and they
/// say what <c>Read GeoJSON</c>'s pins of the same names say.</para>
/// <para><b>What the background buys besides the frame rate</b> (measured 2026-10-18): the file is
/// never turned into a string — the bytes go straight to the parser — so a 340 MB file does not
/// also sit in memory as a 680 MB text; a new <c>Read</c> cancels the read in progress between two
/// features, within a millisecond; and progress is real, bytes parsed over file length.</para>
/// <para><b>The result arrives some frames after <c>Read</c></b>, on the thread that parsed it.
/// <c>HoldLatest</c> brings it into the main loop and keeps the previous result until then. A read
/// that fails or is superseded publishes nothing, so the last good result stays.</para>
/// <para><b>Encoding is not a pin.</b> RFC 7946 requires UTF-8, and the bytes are parsed as UTF-8;
/// a byte order mark is skipped.</para>
/// </remarks>
[ProcessNode(Name = "GeoJSONReader (Reactive)", Category = "NetTopologySuite.IO")]
public sealed class GeoJsonReaderReactiveNode : IDisposable
{
    readonly Subject<Spread<NtsFeature>> _output = new();

    // State the background read writes and Update reads, under _gate.
    readonly object _gate = new();
    CancellationTokenSource? _running;
    int _generation;
    float _progress;
    bool _inProgress;
    bool _completed;
    bool _success;
    string _status = "nothing read yet";

    // Publishing has its own lock: whatever a patch hangs off the observable runs inside OnNext,
    // and the main loop must not wait for it in Update.
    readonly object _publish = new();
    bool _disposed;

    /// <summary>
    /// Read a GeoJSON file on another thread when <c>Read</c> is true.
    /// </summary>
    /// <remarks>
    /// <para><b><c>Read</c> is a bang.</b> Every frame it is true starts a new read and cancels the
    /// one before, so held true it never finishes. Connect <c>OnOpen</c> to read once when the
    /// document opens, or a Bang IOBox.</para>
    /// <para><b>SRID is 4326 unless the <c>Factory</c> pin says otherwise</b> — the same rule, and
    /// the same features, as <c>Read GeoJSON</c>.</para>
    /// <para><b><c>On Completed</c></b> is true for one frame when a read has finished — with
    /// features or with a failure; <c>Success</c> says which.</para>
    /// </remarks>
    [return: Pin(Name = "Output")]
    public IObservable<Spread<NtsFeature>> Update(
        out float progress,
        out bool inProgress,
        out bool onCompleted,
        out bool success,
        out string status,
        VLPath? filePath = null,
        bool read = false,
        GeometryFactory? factory = null)
    {
        if (read)
            Start(filePath?.Value, Defaults.Or(factory));

        lock (_gate)
        {
            progress = _progress;
            inProgress = _inProgress;
            onCompleted = _completed;
            _completed = false;
            success = _success;
            status = _status;
        }
        return _output;
    }

    void Start(string? path, GeometryFactory factory)
    {
        CancellationToken token;
        int generation;
        lock (_gate)
        {
            if (_disposed)
                return;
            _running?.Cancel();
            _running?.Dispose();
            _running = null;
            generation = ++_generation;

            if (string.IsNullOrWhiteSpace(path))
            {
                // An empty path is never "the document's folder" here - rule 8 of docs/RULES.md.
                Finish(generation, success: false, "no file path");
                return;
            }

            _running = new CancellationTokenSource();
            token = _running.Token;
            _progress = 0;
            _inProgress = true;
        }

        Task.Run(() => Work(path!, factory, generation, token), token);
    }

    void Work(string path, GeometryFactory factory, int generation, CancellationToken token)
    {
        try
        {
            var features = GeoJsonFile.Read(path, factory, p => Report(generation, p), token);

            lock (_publish)
            {
                // Superseded or disposed while parsing: the newer read, or nobody, owns the output.
                if (generation != Volatile.Read(ref _generation) || _disposed)
                    return;
                _output.OnNext(features);
            }
            lock (_gate)
                Finish(generation, success: true, $"{features.Count} features");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Cancelled by a newer Read or by Dispose. Whoever cancelled it reports.
        }
        catch (Exception exception)
        {
            lock (_gate)
                Finish(generation, success: false, Defaults.Describe(exception));
        }
    }

    void Report(int generation, float progress)
    {
        lock (_gate)
            if (generation == _generation)
                _progress = progress;
    }

    // Call under _gate.
    void Finish(int generation, bool success, string status)
    {
        if (generation != _generation)
            return;
        _inProgress = false;
        _completed = true;
        _success = success;
        _status = status;
        if (success)
            _progress = 1;
    }

    /// <summary>Cancel a read in progress; nothing is published afterwards.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _generation++;
            _running?.Cancel();
            _running?.Dispose();
            _running = null;
        }
        lock (_publish)
            _output.OnCompleted();
    }
}
