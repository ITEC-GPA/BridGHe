using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BridGHe.Common.Geometry;
using GPC.Geometry.Meshes;
using GPC.Geometry.Meshes.DelaunayMesh;
using St7API;
using static BridGHe.Common.Straus.StrausRuntime;

namespace BridGHe.Common.Straus
{
    internal static class StrausBxsGenerator
    {
        // Called under the process-wide Straus gate; no R2 proxy or child executable.
        public static void Assign(int targetId, int propertyId, Shapes shapes, int[] units)
        {
            if (shapes == null || shapes.Count == 0) throw new ArgumentException("Geometria BXS vuota.");
            var options = new DelaunayMesh.DelaunayGenerateOptions { InitialMeshOnly = true };
            if (!DelaunayMesh.Generate(shapes.ToShapes2d(), options, out List<Mesh> meshes, out var status))
                throw new InvalidOperationException("Mesh BXS non generata: " + status);
            using var model = NewTemporaryModel();
            Check(St7.St7SetUnits(model.Id, units), "St7SetUnits BXS");
            Check(St7.St7NewPlateProperty(model.Id, 1, St7.ptPlateShell, St7.mtIsotropic, "BXS"), "St7NewPlateProperty BXS");
            int node = 0, element = 0;
            foreach (var mesh in meshes)
            {
                var map = new Dictionary<int, int>();
                foreach (var vertex in mesh.Vertices)
                {
                    map.Add(vertex.Id, ++node);
                    Check(St7.St7SetNodeXYZ(model.Id, node, new[] { vertex.Point.X, vertex.Point.Y, vertex.Point.Z }), "St7SetNodeXYZ BXS");
                }
                foreach (var face in mesh.Faces)
                {
                    var vertices = mesh.GetFaceVertices(face);
                    var connection = new int[vertices.Length + 1]; connection[0] = vertices.Length;
                    for (int i = 0; i < vertices.Length; i++) connection[i + 1] = map[vertices[i].Id];
                    Check(St7.St7SetElementConnection(model.Id, St7.tyPLATE, ++element, 1, connection), "St7SetElementConnection BXS");
                }
            }
            if (element == 0) throw new InvalidOperationException("Mesh BXS senza elementi.");
            string file = Path.Combine(Path.GetTempPath(), "BridGHe-" + Guid.NewGuid().ToString("N") + ".bxs");
            try
            {
                Check(St7.St7GenerateBXS(model.Id, file, new double[39]), "St7GenerateBXS");
                Check(St7.St7AssignBXS(targetId, propertyId, file), "St7AssignBXS");
            }
            finally { if (File.Exists(file)) File.Delete(file); }
        }
    }
}
