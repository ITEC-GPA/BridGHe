using System;
using System.Drawing;
using System.Windows.Forms;

namespace BridGHe.Grasshopper.Display;

internal sealed class PlotPreviewWindow : Form
{
    private readonly PlotSvgPreview preview = new();
    private readonly Panel scroll = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White };
    private readonly PictureBox picture = new() { BackColor = Color.White, SizeMode = PictureBoxSizeMode.Zoom };
    private readonly ComboBox zoom = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };

    internal PlotPreviewWindow()
    {
        Text = "BridGHe — Grafico"; Width = 1250; Height = 800; MinimumSize = new Size(500, 350);
        StartPosition = FormStartPosition.CenterParent;
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(5) };
        zoom.Items.AddRange(new object[] { "Adatta", "100%", "200%" }); zoom.SelectedIndex = 0;
        zoom.SelectedIndexChanged += (_, _) => LayoutImage();
        toolbar.Controls.Add(zoom);
        toolbar.Controls.Add(new Label { Text = "Il grafico segue l'aggiornamento del componente.", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
        scroll.Controls.Add(picture); Controls.Add(scroll); Controls.Add(toolbar);
        scroll.SizeChanged += (_, _) => LayoutImage();
    }
    internal void UpdatePlot(string svg)
    {
        if (svg == preview.Source) return;
        picture.Image = null;
        try
        {
            if (svg == null) preview.Clear(); else preview.Set(svg, 1500);
            picture.Image = preview.Image; LayoutImage();
        }
        catch { preview.Clear(); throw; }
    }
    private void LayoutImage()
    {
        if (zoom.SelectedIndex == 0)
        { picture.Dock = DockStyle.Fill; picture.SizeMode = PictureBoxSizeMode.Zoom; }
        else
        {
            picture.Dock = DockStyle.None; picture.Location = Point.Empty;
            int factor = zoom.SelectedIndex;
            picture.Size = new Size(Math.Max(1, preview.CanvasSize.Width * factor), Math.Max(1, preview.CanvasSize.Height * factor));
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { picture.Image = null; preview.Dispose(); }
        base.Dispose(disposing);
    }
}
