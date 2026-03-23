using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace AutoSLCut.Timeline;

internal static class SLCutTimelineTracker
{
    private static readonly object Sync = new();
    private static readonly List<TimelineEvent> Events = new();
    private static readonly List<TimelineSegment> InvalidSegments = new();
    private static readonly List<TimelineSegment> RecordingSegments = new();

    private static DateTime? _lastEventUtc;
    private static DateTime? _lastSaveUtc;
    private static DateTime? _lastAcceptedLoadUtc;
    private static DateTime? _lastPrimaryLoadUtc;
    private static bool _dirtyOpen;
    private static DateTime? _dirtyStartUtc;
    private static DateTime? _dirtyLastLoadUtc;
    private static DateTime? _activeRecordingStartUtc;

    public static void RecordSave(string source)
    {
        lock (Sync)
        {
            DateTime timestampUtc = NormalizeTimestamp(DateTime.UtcNow);
            AppendEvent(timestampUtc, TimelineEventType.Save, source);

            if (_dirtyOpen && _dirtyStartUtc.HasValue && _dirtyLastLoadUtc.HasValue)
            {
                TimelineSegment invalid = new TimelineSegment(_dirtyStartUtc.Value, _dirtyLastLoadUtc.Value);
                if (invalid.IsValid)
                {
                    InvalidSegments.Add(invalid);
                    Log.Info($"[AutoSLCut] Closed invalid segment {FormatUtc(invalid.StartUtc)} -> {FormatUtc(invalid.EndUtc)}");
                }
            }

            _dirtyOpen = false;
            _dirtyStartUtc = null;
            _dirtyLastLoadUtc = null;
            _lastSaveUtc = timestampUtc;

            ExportClipDataLocked("save");
        }
    }

    public static void RecordLoad(string source, bool isFallback = false)
    {
        lock (Sync)
        {
            DateTime adjusted = DateTime.UtcNow.AddMilliseconds(AutoSLCutSettings.LoadMarkerOffsetMilliseconds);
            DateTime timestampUtc = NormalizeTimestamp(adjusted);

            if (isFallback && _lastPrimaryLoadUtc.HasValue && (timestampUtc - _lastPrimaryLoadUtc.Value).TotalSeconds <= 10)
            {
                Verbose($"Ignored fallback load marker from {source} because a primary load marker was already captured.");
                return;
            }

            if (_lastAcceptedLoadUtc.HasValue && (timestampUtc - _lastAcceptedLoadUtc.Value).TotalMilliseconds <= AutoSLCutSettings.LoadDedupWindowMilliseconds)
            {
                Verbose($"Ignored duplicate load marker from {source}.");
                return;
            }

            _lastAcceptedLoadUtc = timestampUtc;
            if (!isFallback)
            {
                _lastPrimaryLoadUtc = timestampUtc;
            }

            AppendEvent(timestampUtc, TimelineEventType.Load, source);

            if (!_dirtyOpen)
            {
                _dirtyOpen = true;
                _dirtyStartUtc = _lastSaveUtc ?? timestampUtc;
                _dirtyLastLoadUtc = timestampUtc;
                Log.Info($"[AutoSLCut] Opened dirty interval at save point {FormatUtc(_dirtyStartUtc.Value)}");
            }
            else
            {
                _dirtyLastLoadUtc = timestampUtc;
                Verbose($"Extended dirty interval to {FormatUtc(timestampUtc)}");
            }

            ExportClipDataLocked("load");
        }
    }

    public static void RecordRecordingStart(string source)
    {
        lock (Sync)
        {
            DateTime timestampUtc = NormalizeTimestamp(DateTime.UtcNow);
            AppendEvent(timestampUtc, TimelineEventType.RecordingStart, source);

            if (_activeRecordingStartUtc.HasValue)
            {
                Verbose("RecordingStart received while recording is already active; keeping the earliest start.");
                return;
            }

            _activeRecordingStartUtc = timestampUtc;
            Log.Info($"[AutoSLCut] Recording started at {FormatUtc(timestampUtc)}");
        }
    }

    public static void RecordRecordingEnd(string source, string? recordingOutputPath = null)
    {
        lock (Sync)
        {
            DateTime timestampUtc = NormalizeTimestamp(DateTime.UtcNow);
            AppendEvent(timestampUtc, TimelineEventType.RecordingEnd, source);

            if (!_activeRecordingStartUtc.HasValue)
            {
                Verbose("RecordingEnd received while no recording is active; ignoring.");
                return;
            }

            TimelineSegment recordingSegment = new TimelineSegment(_activeRecordingStartUtc.Value, timestampUtc);
            if (recordingSegment.IsValid)
            {
                RecordingSegments.Add(recordingSegment);
                Log.Info($"[AutoSLCut] Recording stopped at {FormatUtc(timestampUtc)}");
            }

            _activeRecordingStartUtc = null;
            ExportClipDataLocked("recording_end", recordingOutputPath);
        }
    }

    public static void ExportClipData(string reason)
    {
        lock (Sync)
        {
            ExportClipDataLocked(reason);
        }
    }

    private static void ExportClipDataLocked(string reason, string? recordingOutputPath = null)
    {
        List<TimelineSegment> mergedInvalid = MergeSegments(InvalidSegments);
        List<TimelineSegment> mergedRecording = MergeSegments(RecordingSegments);
        List<TimelineSegment> validSegments = BuildValidSegments(mergedRecording, mergedInvalid);

        ClipTimelineFile payload = new ClipTimelineFile
        {
            GeneratedAtUtc = FormatUtc(DateTime.UtcNow),
            Reason = reason,
            ValidSegments = validSegments.Select(ToSegmentPayload).ToList(),
            InvalidSegments = mergedInvalid.Select(ToSegmentPayload).ToList(),
            Events = Events.Select(ToEventPayload).ToList()
        };

        WriteJson(payload, recordingOutputPath);
    }

    private static SegmentPayload ToSegmentPayload(TimelineSegment segment)
    {
        return new SegmentPayload
        {
            Start = ToUnixMs(segment.StartUtc),
            End = ToUnixMs(segment.EndUtc),
            StartUtc = FormatUtc(segment.StartUtc),
            EndUtc = FormatUtc(segment.EndUtc)
        };
    }

    private static EventPayload ToEventPayload(TimelineEvent timelineEvent)
    {
        return new EventPayload
        {
            Type = timelineEvent.EventType.ToString(),
            Timestamp = ToUnixMs(timelineEvent.TimestampUtc),
            TimestampUtc = FormatUtc(timelineEvent.TimestampUtc),
            Source = timelineEvent.Source
        };
    }

    private static List<TimelineSegment> BuildValidSegments(IReadOnlyList<TimelineSegment> recordingSegments, IReadOnlyList<TimelineSegment> invalidSegments)
    {
        List<TimelineSegment> valid = new List<TimelineSegment>();
        if (recordingSegments.Count == 0)
        {
            return valid;
        }

        foreach (TimelineSegment recording in recordingSegments)
        {
            DateTime cursor = recording.StartUtc;
            foreach (TimelineSegment invalid in invalidSegments)
            {
                if (invalid.EndUtc <= recording.StartUtc)
                {
                    continue;
                }

                if (invalid.StartUtc >= recording.EndUtc)
                {
                    break;
                }

                DateTime overlapStart = Max(recording.StartUtc, invalid.StartUtc);
                DateTime overlapEnd = Min(recording.EndUtc, invalid.EndUtc);

                if (overlapStart > cursor)
                {
                    TimelineSegment candidate = new TimelineSegment(cursor, overlapStart);
                    if (candidate.IsValid)
                    {
                        valid.Add(candidate);
                    }
                }

                if (overlapEnd > cursor)
                {
                    cursor = overlapEnd;
                }
            }

            if (recording.EndUtc > cursor)
            {
                TimelineSegment tail = new TimelineSegment(cursor, recording.EndUtc);
                if (tail.IsValid)
                {
                    valid.Add(tail);
                }
            }
        }

        return valid;
    }

    private static List<TimelineSegment> MergeSegments(IEnumerable<TimelineSegment> source)
    {
        List<TimelineSegment> sorted = source
            .Where((TimelineSegment s) => s.IsValid)
            .OrderBy((TimelineSegment s) => s.StartUtc)
            .ThenBy((TimelineSegment s) => s.EndUtc)
            .ToList();

        if (sorted.Count <= 1)
        {
            return sorted;
        }

        List<TimelineSegment> merged = new List<TimelineSegment> { sorted[0] };
        for (int i = 1; i < sorted.Count; i++)
        {
            TimelineSegment next = sorted[i];
            TimelineSegment current = merged[merged.Count - 1];
            if (next.StartUtc <= current.EndUtc)
            {
                DateTime mergedEnd = Max(current.EndUtc, next.EndUtc);
                merged[merged.Count - 1] = new TimelineSegment(current.StartUtc, mergedEnd);
            }
            else
            {
                merged.Add(next);
            }
        }

        return merged;
    }

    private static void WriteJson(ClipTimelineFile payload, string? recordingOutputPath)
    {
        try
        {
            string outputPath = ResolveClipDataPath(recordingOutputPath);
            string? directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(payload, JsonOptions);
            File.WriteAllText(outputPath, json);
            Verbose($"Wrote clip timeline file to {outputPath}");
        }
        catch (Exception ex)
        {
            Log.Warn($"[AutoSLCut] Failed to write clip timeline json: {ex.Message}");
        }
    }

    private static string ResolveClipDataPath(string? recordingOutputPath)
    {
        string fallbackPath = ProjectSettings.GlobalizePath(AutoSLCutSettings.ClipDataOutputPath);
        if (string.IsNullOrWhiteSpace(recordingOutputPath))
        {
            return fallbackPath;
        }

        try
        {
            string? directory = Path.GetDirectoryName(recordingOutputPath);
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(recordingOutputPath);
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileNameWithoutExtension))
            {
                Verbose($"OBS output path '{recordingOutputPath}' is invalid, using fallback path.");
                return fallbackPath;
            }

            string extension = Path.GetExtension(AutoSLCutSettings.ClipDataOutputPath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".json";
            }

            string clipPath = Path.Combine(directory, fileNameWithoutExtension + extension);
            Verbose($"Resolved clip timeline path from OBS output: {clipPath}");
            return clipPath;
        }
        catch (Exception ex)
        {
            Log.Warn($"[AutoSLCut] Failed to resolve clip timeline path from OBS output path '{recordingOutputPath}': {ex.Message}");
            return fallbackPath;
        }
    }

    private static DateTime NormalizeTimestamp(DateTime timestampUtc)
    {
        if (timestampUtc.Kind != DateTimeKind.Utc)
        {
            timestampUtc = DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc);
        }

        if (_lastEventUtc.HasValue && timestampUtc <= _lastEventUtc.Value)
        {
            timestampUtc = _lastEventUtc.Value.AddTicks(1);
        }

        _lastEventUtc = timestampUtc;
        return timestampUtc;
    }

    private static void AppendEvent(DateTime timestampUtc, TimelineEventType eventType, string source)
    {
        Events.Add(new TimelineEvent(timestampUtc, eventType, source));
        Verbose($"Event {eventType} at {FormatUtc(timestampUtc)} from {source}");
    }

    private static string FormatUtc(DateTime timestampUtc)
    {
        return timestampUtc.ToUniversalTime().ToString("O");
    }

    private static long ToUnixMs(DateTime timestampUtc)
    {
        return new DateTimeOffset(timestampUtc.ToUniversalTime()).ToUnixTimeMilliseconds();
    }

    private static DateTime Min(DateTime a, DateTime b)
    {
        return a <= b ? a : b;
    }

    private static DateTime Max(DateTime a, DateTime b)
    {
        return a >= b ? a : b;
    }

    private static void Verbose(string message)
    {
        if (AutoSLCutSettings.EnableVerboseLogs)
        {
            Log.Info($"[AutoSLCut] {message}");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true
    };

    private sealed class ClipTimelineFile
    {
        [JsonPropertyName("generated_at_utc")]
        public string GeneratedAtUtc { get; init; } = string.Empty;

        [JsonPropertyName("reason")]
        public string Reason { get; init; } = string.Empty;

        [JsonPropertyName("valid_segments")]
        public List<SegmentPayload> ValidSegments { get; init; } = new List<SegmentPayload>();

        [JsonPropertyName("invalid_segments")]
        public List<SegmentPayload> InvalidSegments { get; init; } = new List<SegmentPayload>();

        [JsonPropertyName("events")]
        public List<EventPayload> Events { get; init; } = new List<EventPayload>();
    }

    private sealed class SegmentPayload
    {
        [JsonPropertyName("start")]
        public long Start { get; init; }

        [JsonPropertyName("end")]
        public long End { get; init; }

        [JsonPropertyName("start_utc")]
        public string StartUtc { get; init; } = string.Empty;

        [JsonPropertyName("end_utc")]
        public string EndUtc { get; init; } = string.Empty;
    }

    private sealed class EventPayload
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        [JsonPropertyName("timestamp")]
        public long Timestamp { get; init; }

        [JsonPropertyName("timestamp_utc")]
        public string TimestampUtc { get; init; } = string.Empty;

        [JsonPropertyName("source")]
        public string Source { get; init; } = string.Empty;
    }
}
