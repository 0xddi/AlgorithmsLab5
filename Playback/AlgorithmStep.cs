using System;
using System.Collections.Generic;
using System.Linq;

namespace GraphLab.Playback;

public sealed class TableRow
{
    public string A { get; init; } = "";
    public string B { get; init; } = "";
    public string C { get; init; } = "";
    public string D { get; init; } = "";
}

public sealed class StepVisual
{
    public HashSet<Guid> ActiveNodes { get; set; } = new();
    public HashSet<Guid> VisitedNodes { get; set; } = new();
    public HashSet<Guid> FrontierNodes { get; set; } = new();
    public HashSet<Guid> PathNodes { get; set; } = new();
    public HashSet<Guid> ActiveEdges { get; set; } = new();
    public HashSet<Guid> AcceptedEdges { get; set; } = new();
    public HashSet<Guid> RejectedEdges { get; set; } = new();
    public HashSet<Guid> PathEdges { get; set; } = new();
    public HashSet<Guid> BackEdges { get; set; } = new();
    public Dictionary<Guid, string> NodeBadges { get; set; } = new();
    public Dictionary<Guid, string> NodeTags { get; set; } = new();
    public Dictionary<Guid, string> EdgeLabels { get; set; } = new();

    public StepVisual Copy()
    {
        return new StepVisual
        {
            ActiveNodes = new HashSet<Guid>(ActiveNodes),
            VisitedNodes = new HashSet<Guid>(VisitedNodes),
            FrontierNodes = new HashSet<Guid>(FrontierNodes),
            PathNodes = new HashSet<Guid>(PathNodes),
            ActiveEdges = new HashSet<Guid>(ActiveEdges),
            AcceptedEdges = new HashSet<Guid>(AcceptedEdges),
            RejectedEdges = new HashSet<Guid>(RejectedEdges),
            PathEdges = new HashSet<Guid>(PathEdges),
            BackEdges = new HashSet<Guid>(BackEdges),
            NodeBadges = new Dictionary<Guid, string>(NodeBadges),
            NodeTags = new Dictionary<Guid, string>(NodeTags),
            EdgeLabels = new Dictionary<Guid, string>(EdgeLabels)
        };
    }
}

public sealed class AlgorithmStep
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Hint { get; set; } = "";
    public string Metrics { get; set; } = "";
    public StepVisual Visual { get; set; } = new();
    public List<TableRow> Table { get; set; } = new();
}
