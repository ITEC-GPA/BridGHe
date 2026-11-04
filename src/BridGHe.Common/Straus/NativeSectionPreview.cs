using System;
using System.Linq;
using Rhino.Geometry;

namespace BridGHe.Common.Straus;

/// <summary>Display-only boundary of the vendor's section mesh (GPC Converter, <see cref="GPC.Converter.Straus7.Straus7SectionLibrary.Mesh"/>).
/// Never used for stiffness.</summary>
internal static class NativeSectionPreview
{
    /// <param name="scale">Unit conversion applied to the mesh coordinates (mm to the section unit).</param>
    internal static double[][] Contours(double[][] vertices,int[][] faces,double scale)
    {
        using var mesh=new Mesh();
        foreach(var v in vertices)mesh.Vertices.Add(v[0]*scale,v[1]*scale,v[2]*scale);
        foreach(var f in faces)
        {
            if(f.Length==3)mesh.Faces.AddFace(f[0],f[1],f[2]);
            else if(f.Length==4)mesh.Faces.AddFace(f[0],f[1],f[2],f[3]);
            else throw new NotSupportedException("Unsupported native section preview face: "+f.Length);
        }
        // Mesh geometry is display data; centre it on its own area centroid. Native section
        // properties and the native principal-axis rotation remain the sole analysis inputs.
        using var area=AreaMassProperties.Compute(mesh);
        if(area==null||area.Area<=0)throw new InvalidOperationException("Empty native section preview mesh.");
        var center=area.Centroid;
        return mesh.GetNakedEdges().Select(loop=>loop.SelectMany(p=>new[]{p.X-center.X,p.Y-center.Y}).ToArray()).ToArray();
    }
}
