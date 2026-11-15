using System;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using GPC.Geometry;

internal static partial class OpenSectionChecks190
{
    private static void Preview198(OpenCase198 item,double rotation,int direction)
    {
        // Frozen legacy reference: 5ebd9e9, BeamModel.CreateShapes/GetLocalAxes(false).
        // The display convention keeps the user angle, and mirrors the already-offset single contour.
        // It is deliberately checked separately from the analysis principal frame (+90 for double L).
        var recipe=Solve(item.Type,item.B,item.Tf,item.H,item.Tw,item.Mirror,item.Gap);
        var property=GpcPropertyBuilders.Beam("preview198",5,recipe,GpcFixtures.Steel());
        var draft=GpcBeamBuilders.New(Point3d.Origin,Ends198[direction],property,null,rotation,3,7,Guid.NewGuid(),"preview198");
        var solids=GpcDraftShapes.Beam(draft);
        try
        {
            var vertices=solids.SelectMany(b=>b.Vertices.Select(v=>v.Location)).ToArray();
            var single=item with{Mirror=false,Gap=0};var r=RectangleReference198(single);
            var frame=Frame198(Ends198[direction],rotation*Math.PI/180);
            foreach(var p in MarkedContour198(single))foreach(int side in item.Mirror?new[]{1,-1}:new[]{1})
            {
                double x=item.Mirror?side*(p.X+item.Gap/2+3):p.X-r.X+3,y=p.Y-r.Y+7;
                var expected=frame.V1*x+frame.V2*y;
                Check(vertices.Any(v=>Math.Sqrt(Math.Pow(v.X-expected.X,2)+Math.Pow(v.Y-expected.Y,2)+Math.Pow(v.Z-expected.Z,2))<1e-7),
                    $"R2 frozen legacy preview {item.Label}/{rotation}/axis{direction}/side{side}/({p.X},{p.Y})");
            }
        }
        finally{foreach(var solid in solids)solid.Dispose();}
    }
}
