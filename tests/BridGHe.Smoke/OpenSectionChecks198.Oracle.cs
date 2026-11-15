using System;

internal static partial class OpenSectionChecks190
{
    // R3: C = R diag(1/SA1,1/SA2) R^T. Forces in old axes are (c*x-s*y, s*x+c*y).
    private static (double C11,double C22,double C12) ShearCompliance198(double a1,double a2,double angle)
    {
        if(!double.IsFinite(a1)||!double.IsFinite(a2)||!double.IsFinite(angle)||a1<=0||a2<=0)throw new ArgumentOutOfRangeException(nameof(a1),"Positive finite shear areas and finite angle required");
        double c=Math.Cos(angle),s=Math.Sin(angle);
        return (c*c/a1+s*s/a2,s*s/a1+c*c/a2,c*s*(1/a2-1/a1));
    }
    internal static int Oracle198()
    {
        checks=failed=0;
        OracleChecks198();
        return Finish();
    }
    private static void OracleChecks198()
    {
        foreach(double angle in new[]{0d,37d,45d,90d,180d,-37d})
        {
            double radians=angle*Math.PI/180,c=Math.Cos(radians),s=Math.Sin(radians);
            var compliance=ShearCompliance198(100,400,radians);
            // Independent energy: force components projected to the original principal axes.
            foreach(var force in new[]{(1d,0d),(0d,1d),(2d,-3d)})
            {
                double x=force.Item1,y=force.Item2;
                double original=Math.Pow(c*x-s*y,2)/100+Math.Pow(s*x+c*y,2)/400;
                double rotated=compliance.C11*x*x+2*compliance.C12*x*y+compliance.C22*y*y;
                Near(original,rotated,$"R3 shear energy angle={angle}, force=({x},{y})");
            }
            Near(1d/(100*400),compliance.C11*compliance.C22-compliance.C12*compliance.C12,$"R3 determinant angle={angle}");
        }
        var diagonal=ShearCompliance198(100,400,Math.PI/4);
        Near(160,1/diagonal.C11,"R3 45deg effective area 160, not arithmetic 250");
        Near(-0.00375,diagonal.C12,"R3 45deg signed coupling");
    }
}
