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

// The /// text below is what vvvv shows on hover - user-facing, short, one sentence per line. The
// reasoning lives here.
//
// Why this exists beside FileReader -> Read GeoJSON: Read GeoJSON parses on the main loop, and
// while it parses nothing else in vvvv runs. Measured in vvvv 7.4: the 7,342 shipped cities freeze
// it for 0.56 s, 1.2 million building footprints for 12.9 s.
//
// Shaped like vvvv's own JSONReader (Reactive) and FileReader (String Reactive) - File Path and
// Read in; Output observable, Progress, In Progress, On Completed out - following The Gray Book's
// rule that anything asynchronous is handed out as an observable. Success and Status are added
// because a file can be wrong; they mean what Read GeoJSON's pins of the same names mean.
//
// What the background buys besides the frame rate (measured 2026-10-18): the file is never made a
// string, so a 340 MB file does not also sit in memory as a 680 MB text; a new Read cancels the
// read in progress between two features, within a millisecond; and progress is real.
//
// The result arrives frames later, on the parsing thread; HoldLatest brings it in. A failed or
// superseded read publishes nothing, so the last good result stays. Encoding is not a pin:
// RFC 7946 requires UTF-8, and a byte order mark is skipped.

/// <summary>
/// Reads a GeoJSON file in the background, so vvvv keeps running while it parses.
/// </summary>
/// <remarks>
/// The features arrive on Output some frames after Read. Connect HoldLatest to bring them into the main loop.
/// A new Read cancels the one in progress.
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

    // Read is a bang: every frame it is true starts a new read and cancels the one before, so held
    // true it never finishes. SRID follows the same rule, and gives the same features, as
    // Read GeoJSON. On Completed fires for a failure too; Success says which.

    /// <summary>
    /// Starts a read when Read is true.
    /// </summary>
    /// <param name="progress">How much of the file is parsed, from 0 to 1.</param>
    /// <param name="inProgress">True while a read is running.</param>
    /// <param name="onCompleted">True for one frame when a read has finished, with features or with a failure.</param>
    /// <param name="success">False when the last read failed.</param>
    /// <param name="status">The number of features, or what went wrong.</param>
    /// <param name="filePath">The GeoJSON file.</param>
    /// <param name="read">A bang. Held true, it restarts the read every frame and never finishes.</param>
    /// <param name="factory">Sets the SRID. Unconnected gives 4326, which GeoJSON mandates.</param>
    /// <returns>The features of each finished read. Connect HoldLatest.</returns>
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

    /// <summary>Cancels a read in progress; nothing is published afterwards.</summary>
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
