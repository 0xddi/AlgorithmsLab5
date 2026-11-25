using System;
using System.Linq;
using GraphLab.Models;

namespace GraphLab.Models;

public static class Samples
{
    public static GraphModel TraversalGraph()
    {
        var g = new GraphModel { Directed = false };
        var a = g.AddNode("A", 170, 160);
        var b = g.AddNode("B", 390, 110);
        var c = g.AddNode("C", 615, 170);
        var d = g.AddNode("D", 840, 120);
        var e = g.AddNode("E", 280, 355);
        var f = g.AddNode("F", 520, 355);
        var gNode = g.AddNode("G", 765, 345);
        var h = g.AddNode("H", 520, 555);

        g.AddEdge(a.Id, b.Id, 4);
        g.AddEdge(a.Id, e.Id, 2);
        g.AddEdge(b.Id, c.Id, 5);
        g.AddEdge(b.Id, e.Id, 7);
        g.AddEdge(c.Id, d.Id, 3);
        g.AddEdge(c.Id, f.Id, 8);
        g.AddEdge(c.Id, gNode.Id, 6);
        g.AddEdge(d.Id, gNode.Id, 4);
        g.AddEdge(e.Id, f.Id, 4);
        g.AddEdge(e.Id, h.Id, 9);
        g.AddEdge(f.Id, gNode.Id, 2);
        g.AddEdge(f.Id, h.Id, 5);
        g.AddEdge(gNode.Id, h.Id, 3);
        return g;
    }

    public static GraphModel TransportNetwork()
    {
        var g = new GraphModel { Directed = true };
        var s = g.AddNode("S", 130, 330);
        var v1 = g.AddNode("V1", 355, 150);
        var v2 = g.AddNode("V2", 355, 510);
        var v3 = g.AddNode("V3", 640, 150);
        var v4 = g.AddNode("V4", 640, 510);
        var t = g.AddNode("T", 865, 330);

        g.AddEdge(s.Id, v1.Id, 16, GraphValueKind.Capacity);
        g.AddEdge(s.Id, v2.Id, 13, GraphValueKind.Capacity);
        g.AddEdge(v1.Id, v3.Id, 12, GraphValueKind.Capacity);
        g.AddEdge(v2.Id, v1.Id, 4, GraphValueKind.Capacity);
        g.AddEdge(v2.Id, v4.Id, 14, GraphValueKind.Capacity);
        g.AddEdge(v3.Id, v2.Id, 9, GraphValueKind.Capacity);
        g.AddEdge(v3.Id, t.Id, 20, GraphValueKind.Capacity);
        g.AddEdge(v4.Id, v3.Id, 7, GraphValueKind.Capacity);
        g.AddEdge(v4.Id, t.Id, 4, GraphValueKind.Capacity);
        return g;
    }

    public static GraphModel MstGraph()
    {
        var g = new GraphModel { Directed = false };
        var a = g.AddNode("A", 150, 190);
        var b = g.AddNode("B", 365, 110);
        var c = g.AddNode("C", 620, 145);
        var d = g.AddNode("D", 300, 370);
        var e = g.AddNode("E", 535, 335);
        var f = g.AddNode("F", 790, 345);
        var gn = g.AddNode("G", 585, 555);

        g.AddEdge(a.Id, b.Id, 7);
        g.AddEdge(a.Id, d.Id, 5);
        g.AddEdge(b.Id, c.Id, 8);
        g.AddEdge(b.Id, d.Id, 9);
        g.AddEdge(b.Id, e.Id, 7);
        g.AddEdge(c.Id, e.Id, 5);
        g.AddEdge(d.Id, e.Id, 15);
        g.AddEdge(d.Id, f.Id, 6);
        g.AddEdge(e.Id, f.Id, 8);
        g.AddEdge(e.Id, gn.Id, 9);
        g.AddEdge(f.Id, gn.Id, 11);
        return g;
    }

    public static GraphModel ShortestPathGraph()
    {
        var g = new GraphModel { Directed = false };
        var a = g.AddNode("A", 130, 170);
        var b = g.AddNode("B", 360, 105);
        var c = g.AddNode("C", 305, 335);
        var d = g.AddNode("D", 545, 255);
        var e = g.AddNode("E", 500, 500);
        var f = g.AddNode("F", 735, 360);
        var gn = g.AddNode("G", 845, 155);
        var h = g.AddNode("H", 865, 525);

        g.AddEdge(a.Id, b.Id, 4);
        g.AddEdge(a.Id, c.Id, 2);
        g.AddEdge(b.Id, c.Id, 1);
        g.AddEdge(b.Id, d.Id, 5);
        g.AddEdge(b.Id, gn.Id, 9);
        g.AddEdge(c.Id, d.Id, 8);
        g.AddEdge(c.Id, e.Id, 10);
        g.AddEdge(d.Id, e.Id, 2);
        g.AddEdge(d.Id, f.Id, 6);
        g.AddEdge(d.Id, gn.Id, 3);
        g.AddEdge(e.Id, f.Id, 3);
        g.AddEdge(e.Id, h.Id, 12);
        g.AddEdge(f.Id, gn.Id, 1);
        g.AddEdge(f.Id, h.Id, 4);
        g.AddEdge(gn.Id, h.Id, 8);
        return g;
    }

    public static GraphModel Empty(bool directed)
    {
        return new GraphModel { Directed = directed };
    }
}
