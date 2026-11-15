using System;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using GPC.Geometry;
using GPC.Model.ElementProperties;
using GPC.Model.Sections;

internal static partial class OpenSectionChecks190
{
    private static void Managed198()
    {
        OracleChecks198();ReadbackNegative198();
        foreach(var item in Cases198())foreach(var units in new[]{DefinitionUnits.Nmm,DefinitionUnits.Knm})
        foreach(double rotation in new[]{0d,37d})foreach(int direction in new[]{0,1,2})
        {
            string label=$"R2 {item.Label}/{units}/{rotation}/axis{direction}";
            try
            {
                var model=Model198(item,units,rotation,direction);var beam=model.Model.BeamElements.Values.Single();
                var s=((ElasticBeamProperty)beam.BeamProperty).Section;var r=RectangleReference198(item);
                Near(r.A,s.Area,label+" independent area");Near(r.Ixx,s.Jxx,label+" independent Ixx");Near(r.Iyy,s.Jyy,label+" independent Iyy");
                Near(r.Angle,s.AngleX1,label+" signed principal angle, including gap crossing");
                double turn=item.Type=="Angle"&&item.Mirror?90:0;
                var principal=Frame198(Ends198[direction],(rotation+turn)*Math.PI/180);
                var geometry=Frame198(Ends198[direction],(rotation+turn)*Math.PI/180-r.Angle);
                Vector198(geometry.V1,beam.Assignments.SectionAxes.V1,label+" geometric axis1");
                Vector198(geometry.V2,beam.Assignments.SectionAxes.V2,label+" geometric axis2");
                Vector198(principal.V1,beam.Assignments.OffsetAxes.V1,label+" offset axis1");
                Vector198(principal.V2,beam.Assignments.OffsetAxes.V2,label+" offset axis2");
                var load=beam.Assignments.Loads.Single();
                Vector198(principal.V1*3+principal.V2*7,load.Eccentricity,label+" global load eccentricity");
                double forceScale=units==DefinitionUnits.Nmm?1:1000;
                var expectedForce=(principal.V1*2+principal.V2*3+principal.V3*5)*forceScale;
                var global=load.Concentrated.GetGlobalForces();
                Vector198(expectedForce,new Vector3d(global[0],global[1],global[2]),label+" local point force in global axes",1e-8);
                // Component local X,Y,Z are the principal axes, independent of the section eigenvalue ordering.
                var recorded=Record(model);
                var axes=(double[])recorded.Calls.Single(c=>c.Name=="SetBeamAxes").Args[1];
                Vector198(principal.V1,new Vector3d(axes[0],axes[1],axes[2]),label+" writer signed principal1");
                Vector198(principal.V2,new Vector3d(axes[3],axes[4],axes[5]),label+" writer signed principal2");
                var expected=Expected198((ElasticBeamProperty)beam.BeamProperty,item);
                var call=recorded.Calls.Single(c=>c.Name==(item.Mirror?"SetNativeSection":"SetBeamSectionGeometry"));
                var values=(double[])call.Args[item.Mirror?2:3];
                for(int i=0;i<11;i++)Near(expected[i],values[i],label+" planned "+PropertyNames198[i]);
                var sentForce=(double[])recorded.Calls.Single(c=>c.Name=="SetBeamPointLoad"&&!(bool)c.Args[3]).Args[4];
                Vector198(expectedForce,new Vector3d(sentForce[0],sentForce[1],sentForce[2]),label+" exported local point force",1e-8);
                Near(.37,sentForce[3],label+" point force station");
                var outlines=s is SectionBuiltUp pair?pair.GetOutlines():new[]{s.Shape};
                var actualPoints=outlines.SelectMany(o=>o.Fill).Select(p=>beam.Assignments.SectionAxes.V1*(p.X-s.Centroid.X)+beam.Assignments.SectionAxes.V2*(p.Y-s.Centroid.Y)+load.Eccentricity).ToArray();
                foreach(var point in MarkedContour198(item))
                {
                    var expectedPoint=geometry.V1*(point.X-r.X)+geometry.V2*(point.Y-r.Y)+principal.V1*3+principal.V2*7;
                    Check(actualPoints.Any(p=>(p-expectedPoint).Length<1e-7),label+$" oriented contour marker ({point.X},{point.Y})");
                }
                if(units==DefinitionUnits.Nmm)Preview198(item,rotation,direction);
                var expectedShearGlobal=geometry.V1*(s.ShearCenter.X-r.X)+geometry.V2*(s.ShearCenter.Y-r.Y);
                var writtenShearGlobal=principal.V1*values[4]+principal.V2*values[5];
                Vector198(expectedShearGlobal,writtenShearGlobal,label+" signed shear-centre vector",1e-7);
                // A negative sign cannot hide behind equal/sorted moments of inertia.
                Check((geometry.V1-(beam.Assignments.SectionAxes.V1*-1)).Length>1,label+" rejects 180deg axis flip");
                if(units==DefinitionUnits.Nmm&&direction==0&&rotation==37&&item.Mirror&&(item.Gap==40||item.Gap==120))
                {
                    string target=item.Type=="Angle"?"C":"Angle";
                    var targetRecipe=GpcSectionBuilders.Standard(target=="C"?BridgeSectionKind.C:BridgeSectionKind.Angle,150,100,t1:12,t2:10,mirror:target=="C"?0:2,gapA:target=="C"?0:40);
                    var replacement=GpcPropertyBuilders.Beam("replacement",5,targetRecipe,GpcFixtures.Steel());
                    var changed=GpcModelTransforms.PhaseModel(model,"198 replacement",new[]{beam.Name},replacement,false,Guid.NewGuid());
                    var next=changed.Model.BeamElements.Values.Single();var frame=Frame198(Ends198[0],(37+(target=="Angle"?90:0))*Math.PI/180);
                    Vector198(frame.V1,next.Assignments.OffsetAxes.V1,label+" replacement "+target+" offset frame");
                    Vector198(frame.V1*3+frame.V2*7,next.Assignments.Loads.Single().Eccentricity,label+" replacement "+target+" load eccentricity");
                    var changedForce=next.Assignments.Loads.Single().Concentrated.GetGlobalForces();
                    Vector198(frame.V1*2+frame.V2*3+frame.V3*5,new Vector3d(changedForce[0],changedForce[1],changedForce[2]),label+" replacement "+target+" signed load");
                    Near(beam.Assignments.Loads.Count,next.Assignments.Loads.Count,label+" replacement keeps loads");
                }
            }
            catch(Exception ex){Check(false,label+": "+ex);}
        }
    }
    private static (double X,double Y)[] MarkedContour198(OpenCase198 c)
    {
        (double X,double Y)[] p=c.Type=="T"?new[]{((c.B-c.Tw)/2,0d),((c.B+c.Tw)/2,0d),((c.B+c.Tw)/2,c.H-c.Tf),(c.B,c.H-c.Tf),(c.B,c.H),(0d,c.H),(0d,c.H-c.Tf),((c.B-c.Tw)/2,c.H-c.Tf)}
            :c.Type=="C"?new[]{(0d,0d),(c.B,0d),(c.B,c.Tf),(c.Tw,c.Tf),(c.Tw,c.H-c.Tf),(c.B,c.H-c.Tf),(c.B,c.H),(0d,c.H)}
            :new[]{(0d,0d),(c.B,0d),(c.B,c.Tf),(c.Tw,c.Tf),(c.Tw,c.H),(0d,c.H)};
        return c.Mirror?p.Concat(p.Select(v=>(-c.Gap-v.X,v.Y))).ToArray():p;
    }
}
