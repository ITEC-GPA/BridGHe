using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.Forms;
using BridGHe.Grasshopper.Helpers;
using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Types;

namespace BridGHe.Grasshopper.Components.Parameters
{
    public class VersionParam : GH_PersistentParam<GH_String>
    {
        public override Guid ComponentGuid => new Guid("98efbea2-2189-43fa-8b2e-a9fb6736e306");

        public VersionParam()
            : base("Version controller", "VC", "Version controller", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_PARAMETERS)
        {
        }

        protected override GH_GetterResult Prompt_Plural(ref List<GH_String> values)
        {
            return GH_GetterResult.cancel;
        }

        protected override GH_GetterResult Prompt_Singular(ref GH_String value)
        {
            return GH_GetterResult.cancel;
        }

        public string GetDescription()
        {
            if (PersistentDataCount > 0)
                return "Saved with BridGHe " + PersistentData.Last().Value;
            else
                return "";
        }

        public override void CreateAttributes()
        {
            m_attributes = new VersionParamAttributes(this);
        }

        public override bool Write(GH_IWriter writer)
        {
            PersistentData.ClearData();
            SetPersistentData(new GH_String(typeof(BridGHeInfo).Assembly.GetName().Version.ToString()));
            if (base.Write(writer))
            {
                ClearRuntimeMessages();
                ExpireSolution(true);
                return true;
            }
            return false;
        }

        public override bool Read(GH_IReader reader)
        {
            if (base.Read(reader))
            {
                string serialized_ver = PersistentData.Last().Value;
                if (!serialized_ver.Equals(typeof(BridGHeInfo).Assembly.GetName().Version.ToString()))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Attention, serialized version " + serialized_ver + " different from current version");
                }
                return true;
            }
            return false;
        }

        public override GH_Exposure Exposure => GH_Exposure.hidden;

        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());
    }

    internal class VersionParamAttributes : GH_ResizableAttributes<VersionParam>
    {
        private Rectangle _internalRect;

        protected override Size MinimumSize => new Size(150, 25);

        protected override Size MaximumSize => new Size(150, 25);

        protected override Padding SizingBorders { get { return new Padding(10); } }

        public VersionParamAttributes(VersionParam owner)
            : base(owner)
        {
            Bounds = new RectangleF(Pivot, MinimumSize);
        }

        protected override void Layout()
        {
            base.Layout();
            _internalRect = GH_Convert.ToRectangle(Bounds);
            _internalRect.Inflate(-5, -5);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            if (channel == GH_CanvasChannel.Objects)
            {
                GH_Palette palette = GH_Palette.Normal;

                // Adjust palette based on the Owner's worst case messaging level.
                switch (Owner.RuntimeMessageLevel)
                {
                    case GH_RuntimeMessageLevel.Warning:
                        palette = GH_Palette.Warning;
                        break;

                    case GH_RuntimeMessageLevel.Error:
                        palette = GH_Palette.Error;
                        break;
                }

                RectangleF capsuleBox = new RectangleF(Bounds.Location, Bounds.Size);

                GH_Capsule capsule = GH_Capsule.CreateCapsule(capsuleBox, palette);
                capsule.Render(graphics, Selected, Owner.Locked, false);
                capsule.Dispose();

                using (Pen pen = new Pen(Color.Black))
                    graphics.DrawRectangle(pen, _internalRect);

                StringFormat format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter
                };
                RectangleF textRectangle = Bounds;
                textRectangle.Y += 2;
                textRectangle.Height = 20;
                textRectangle.Inflate(-4, 0);
                graphics.DrawString(Owner.GetDescription(), GH_FontServer.Standard, Brushes.Black, textRectangle, format);
            }
            else
            {
                base.Render(canvas, graphics, channel);
            }
        }
    }
}
