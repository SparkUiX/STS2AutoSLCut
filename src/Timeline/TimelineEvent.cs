using System;

namespace AutoSLCut.Timeline;

internal readonly record struct TimelineEvent(DateTime TimestampUtc, TimelineEventType EventType, string Source);
