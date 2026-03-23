using System;

namespace AutoSLCut.Timeline;

internal readonly record struct TimelineSegment(DateTime StartUtc, DateTime EndUtc)
{
    public bool IsValid => EndUtc > StartUtc;
}
