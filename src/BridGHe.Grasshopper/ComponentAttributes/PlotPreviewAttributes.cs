using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using BridGHe.Grasshopper.Components.Preview;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;

namespace BridGHe.Grasshopper.ComponentAttributes;

internal sealed class PlotPreviewAttributes : GH_ComponentAttributes
{
    private readonly BridgePlotPreviewComponent component;
    private RectangleF body, frame;
    internal PlotPreviewAttributes(BridgePlotPreviewComponent owner) : base(owner) => component = owner;

    protected override void Layout()
    {
        base.Layout(); body = Bounds;
        var size = component.Preview.Image == null ? new Size(component.CanvasWidth, 130) : component.Preview.CanvasSize;
        frame = new RectangleF(body.X + body.Width / 2 - size.Width / 2f - 5, body.Bottom + 8, size.Width + 10, size.Height + 30);
        Bounds = RectangleF.Union(body, frame);
    }
    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        var all = Bounds;
        try { Bounds = body; base.Render(canvas, graphics, channel); }
        finally { Bounds = all; }
        if (channel != GH_CanvasChannel.Objects) return;
        var state = graphics.Save();
        try
        {
            graphics.FillRectangle(Brushes.White, frame);
            using var border = new Pen(Selected ? Color.Teal : Color.SlateGray, Selected ? 2 : 1);
            graphics.DrawRectangle(border, frame.X, frame.Y, frame.Width, frame.Height);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (component.Preview.Image != null)
                graphics.DrawImage(component.Preview.Image, frame.X + 5, frame.Y + 5, frame.Width - 10, frame.Height - 30);
            else graphics.DrawString("Collega SVG da Bridge Plot", GH_FontServer.Standard, Brushes.DimGray, frame.X + 12, frame.Y + 20);
            graphics.DrawString("Doppio clic: ingrandisci", GH_FontServer.Small, Brushes.DimGray, frame.X + 8, frame.Bottom - 20);
        }
        finally { graphics.Restore(state); }
    }
    public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (e.Button == MouseButtons.Left && frame.Contains(e.CanvasLocation))
        { component.OpenPreview(); return GH_ObjectResponse.Handled; }
        return base.RespondToMouseDoubleClick(sender, e);
    }
}
