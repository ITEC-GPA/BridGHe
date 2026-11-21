using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Geometry;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Rhino.Geometry;

/// <summary>BridGHe 0.7.2 (proposal 2): an offscreen image of the labels of Bridge Grillage Mesh with Labels = True (no screen, no mouse;
/// memory rule of 9/10). Rhino's DrawToBitmap crashes in the hidden host (access violation), so the image is drawn with GDI+ from the data
/// the preview draws: the member axes in their preview colours, the support and joint dots of before and the labels of 0.7.2 (PreviewLabels),
/// in plan, with the transverse scale exaggerated so that the girders can be told apart. The definition is read from a copy in a temporary
/// folder; only the objects upstream of Bridge Grillage Mesh are solved (the others are locked), no button is pressed.
/// Usage: BridGHe.Smoke --labels-capture &lt;definition.ghx&gt; &lt;image.png&gt;</summary>
internal static class LabelsCapture072
{
    internal static int Run(string definition, string image)
    {
        BridgeMergeTests.Initialize();
        string folder = Path.Combine(Path.GetTempPath(), "bridghe-labels-072-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string copy = Path.Combine(folder, Path.GetFileName(definition)); File.Copy(definition, copy);
        using var doc = GrasshopperBridgeExample.Read(copy); doc.FilePath = copy;
        var mesh = doc.Objects.OfType<GH_Component>().FirstOrDefault(c => c.GetType().Name == "BridgeGrillageMeshComponent")
            ?? throw new InvalidOperationException("No Bridge Grillage Mesh in " + definition);
        var upstream = new HashSet<IGH_DocumentObject>(); var stack = new Stack<IGH_DocumentObject>(); stack.Push(mesh);
        while (stack.Count > 0)
        {
            var o = stack.Pop(); if (!upstream.Add(o)) continue;
            var inputs = o is GH_Component c ? c.Params.Input.Cast<IGH_Param>() : o is IGH_Param p ? new[] { p } : Array.Empty<IGH_Param>();
            foreach (var input in inputs) foreach (var s in input.Sources) stack.Push(s.Attributes.GetTopLevel.DocObject);
        }
        foreach (var o in doc.Objects.OfType<GH_ActiveObject>()) if (!upstream.Contains(o)) o.Locked = true;
        var labels = mesh.Params.Input[2];
        foreach (var toggle in labels.Sources.OfType<GH_BooleanToggle>()) { toggle.Value = true; toggle.ExpireSolution(false); }
        if (labels.SourceCount == 0) { var b = (Param_Boolean)labels; b.PersistentData.Clear(); b.SetPersistentData(true); b.ExpireSolution(false); }
        GH_Document.EnableSolutions = true; doc.Enabled = true; doc.NewSolution(false, GH_SolutionMode.Silent);
        var errors = mesh.RuntimeMessages(GH_RuntimeMessageLevel.Error);
        if (errors.Count > 0) throw new InvalidOperationException("Bridge Grillage Mesh did not solve: " + string.Join(" / ", errors));
        // The preview data of the component: (geometry, labels, extra labels of 0.7.2) and its colours.
        Type type = mesh.GetType(); while (type != null && type.Name != "SimpleBridgeComponent") type = type.BaseType;
        var item = ((IList)type.GetField("preview", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(mesh)).Cast<object>().Single();
        var geometry = (SimpleBridgeGeometry)item.GetType().GetField("Item1").GetValue(item);
        var extra = item.GetType().GetField("Item3").GetValue(item) ?? throw new InvalidOperationException("Labels are not on.");
        var colors = (Color[])type.GetField("Colors", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        var dots = ((IEnumerable)extra.GetType().GetProperty("Dots", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(extra)).Cast<object>()
            .Select(d => (ValueTuple<Point3d, string, Color>)d).ToList();
        var arrow = (Line)extra.GetType().GetProperty("Arrow", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(extra);
        dots.InsertRange(0, geometry.Supports.Select(s => (s.Point, $"S{s.Support}/G{s.Girder}", Color.DarkRed)));
        dots.InsertRange(0, geometry.Joints.Select(j => (j.Point, $"G{j.Girder}/J{j.Joint}", Color.DarkSlateGray)));
        Draw(geometry, colors, dots, arrow, image, Path.GetFileName(definition));
        Console.WriteLine($"LABELS CAPTURE {Path.GetFullPath(image)} ({Path.GetFileName(definition)}: {geometry.Members.Count} members, {dots.Count} labels)");
        return 0;
    }

    private static void Draw(SimpleBridgeGeometry g, Color[] colors, List<(Point3d Point, string Text, Color Back)> dots, Line arrow, string image, string title)
    {
        const int W = 2200, H = 1000, M = 70;
        var points = g.Nodes.Concat(dots.Select(d => d.Point)).Concat(new[] { arrow.From, arrow.To }).ToArray();
        double x0 = points.Min(p => p.X), x1 = points.Max(p => p.X), y0 = points.Min(p => p.Y), y1 = points.Max(p => p.Y);
        double sx = (W - 2 * M) / Math.Max(1, x1 - x0), sy = Math.Min((H - 2 * M - 40) / Math.Max(1, y1 - y0), 8 * sx);
        PointF P(Point3d p) => new((float)(M + (p.X - x0) * sx), (float)(H - M - (p.Y - y0) * sy));
        using var bitmap = new Bitmap(W, H); using var gr = Graphics.FromImage(bitmap);
        gr.SmoothingMode = SmoothingMode.AntiAlias; gr.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit; gr.Clear(Color.White);
        using var font = new Font("Segoe UI", 10f, FontStyle.Bold); using var titleFont = new Font("Segoe UI", 12f);
        foreach (var edge in new[] { g.LeftEdge, g.RightEdge }) if (edge != null && edge.Count > 1) using (var pen = new Pen(Color.SeaGreen, 2)) gr.DrawLines(pen, edge.Select(P).ToArray());
        foreach (var m in g.Members)
            using (var pen = new Pen(m.Family == BridgeMemberFamily.Girder ? colors[(Math.Max(1, m.Segment) - 1) % 3] : colors[(int)m.Family % colors.Length], m.Family == BridgeMemberFamily.Girder ? 3 : 1.5f))
                gr.DrawLine(pen, P(m.Axis.From), P(m.Axis.To));
        if (arrow.IsValid) using (var pen = new Pen(Color.Black, 3) { CustomEndCap = new AdjustableArrowCap(6, 8) }) gr.DrawLine(pen, P(arrow.From), P(arrow.To));
        foreach (var (point, text, back) in dots)
        {
            var size = gr.MeasureString(text, font); var c = P(point);
            var box = new RectangleF(c.X - size.Width / 2 - 3, c.Y - size.Height / 2 - 1, size.Width + 6, size.Height + 2);
            using var brush = new SolidBrush(back); gr.FillRectangle(brush, box); gr.DrawString(text, font, Brushes.White, box.X + 3, box.Y + 1);
        }
        gr.DrawString($"BridGHe 0.7.2 - {title}, Bridge Grillage Mesh, Labels = True. Plan view; transverse scale x{sy / sx:0.#} for legibility (drawn offscreen from the preview data).",
            titleFont, Brushes.Black, M, 15);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(image))); bitmap.Save(image, ImageFormat.Png);
    }
}
