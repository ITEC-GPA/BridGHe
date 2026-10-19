using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace BridGHe.Grasshopper.Display;

/// <summary>Renders self-contained vector plots without a browser, Rhino viewport or temporary files.</summary>
internal sealed class PlotSvgPreview : IDisposable
{
    private string source;
    private int width;
    internal Bitmap Image { get; private set; }
    internal string Source => source;
    internal Size CanvasSize { get; private set; }

    internal void Set(string svg, int canvasWidth)
    {
        if (svg == source && canvasWidth == width && Image != null) return;
        if (canvasWidth < 240 || canvasWidth > 1600)
            throw new ArgumentOutOfRangeException(nameof(canvasWidth), "Preview width must be between 240 and 1600 pixels.");
        double aspect = Validate(svg);
        double scale = Math.Min(canvasWidth, 900 * aspect);
        var size = new Size(Math.Max(1, (int)Math.Round(scale)), Math.Max(1, (int)Math.Round(scale / aspect)));
        using var text = new StringReader(svg);
        using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var drawing = Svg.SvgDocument.Open<Svg.SvgDocument>(reader);
        var next = drawing.Draw(size.Width * 2, size.Height * 2);
        if (next == null) throw new ArgumentException("Could not render this SVG.");
        Image?.Dispose();
        Image = next;
        source = svg;
        width = canvasWidth;
        CanvasSize = size;
    }

    internal static double Validate(string svg)
    {
        if (string.IsNullOrWhiteSpace(svg)) throw new ArgumentException("Connect the SVG output of a Bridge Plot component.");
        using var text = new StringReader(svg);
        using var reader = XmlReader.Create(text, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4_000_000
        });
        var document = XDocument.Load(reader);
        var root = document.Root;
        if (root == null || root.Name != XName.Get("svg", "http://www.w3.org/2000/svg"))
            throw new ArgumentException("Input must be SVG markup, not a file path.");
        string[] tags = { "svg", "g", "rect", "path", "text", "tspan", "title", "desc", "circle", "line", "polyline", "polygon", "ellipse" };
        foreach (var element in root.DescendantsAndSelf())
        {
            if (element.Name.Namespace != root.Name.Namespace || !tags.Contains(element.Name.LocalName))
                throw new ArgumentException("Preview supports self-contained vector plots; unsupported element: " + element.Name.LocalName);
            foreach (var attribute in element.Attributes())
                if (!attribute.IsNamespaceDeclaration && (attribute.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase) ||
                    attribute.Name.LocalName is "href" or "style" || attribute.Value.Contains("url(", StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException("External resources, scripts and CSS are not supported in plot previews.");
        }
        double w, h;
        if (root.Attribute("viewBox") is XAttribute box)
        {
            var values = box.Value.Split(new[] { ' ', ',', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            if (values.Length != 4 || values.Any(v => !double.IsFinite(v))) throw new ArgumentException("Invalid SVG viewBox.");
            w = values[2]; h = values[3];
        }
        else
        {
            double Length(string name) => double.Parse(((string)root.Attribute(name) ?? "").Replace("px", ""), CultureInfo.InvariantCulture);
            w = Length("width"); h = Length("height");
        }
        if (!double.IsFinite(w) || !double.IsFinite(h) || w <= 0 || h <= 0 || w / h < .01 || w / h > 100)
            throw new ArgumentException("SVG requires finite positive dimensions and an aspect ratio between 1:100 and 100:1.");
        return w / h;
    }

    internal void Clear()
    {
        Image?.Dispose(); Image = null; source = null; width = 0; CanvasSize = Size.Empty;
    }
    public void Dispose() => Clear();
}
