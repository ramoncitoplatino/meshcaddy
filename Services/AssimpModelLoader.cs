using System.Numerics;
using Assimp;
using MeshCaddy.Models;

namespace MeshCaddy.Services;

internal static class AssimpModelLoader
{
    public static ModelMesh Parse(string path, CancellationToken token)
    {
        using var importer = new AssimpContext();
        var steps = PostProcessSteps.Triangulate |
                    PostProcessSteps.GenerateSmoothNormals |
                    PostProcessSteps.PreTransformVertices |
                    PostProcessSteps.FindDegenerates |
                    PostProcessSteps.FindInvalidData;
        var scene = importer.ImportFile(path, steps)
                    ?? throw new InvalidDataException("The importer could not read this model.");

        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<int>();
        var colors = new List<uint?>();

        foreach (var mesh in scene.Meshes)
        {
            token.ThrowIfCancellationRequested();
            uint? color = null;
            if ((uint)mesh.MaterialIndex < scene.Materials.Count)
            {
                var material = scene.Materials[mesh.MaterialIndex];
                if (material.HasColorDiffuse)
                {
                    var diffuse = material.ColorDiffuse;
                    color = PackColor(diffuse.R, diffuse.G, diffuse.B, diffuse.A);
                }
            }

            foreach (var face in mesh.Faces)
            {
                if (face.IndexCount != 3) continue;
                var faceColor = color;
                if (mesh.HasVertexColors(0))
                {
                    var channel = mesh.VertexColorChannels[0];
                    var ca = channel[face.Indices[0]];
                    var cb = channel[face.Indices[1]];
                    var cc = channel[face.Indices[2]];
                    faceColor = PackColor(
                        (ca.R + cb.R + cc.R) / 3,
                        (ca.G + cb.G + cc.G) / 3,
                        (ca.B + cb.B + cc.B) / 3,
                        (ca.A + cb.A + cc.A) / 3);
                }
                var a = ToVector(mesh.Vertices[face.Indices[0]]);
                var b = ToVector(mesh.Vertices[face.Indices[1]]);
                var c = ToVector(mesh.Vertices[face.Indices[2]]);
                var faceNormal = Vector3.Cross(b - a, c - a);
                faceNormal = faceNormal.LengthSquared() < 0.000001f ? Vector3.UnitZ : Vector3.Normalize(faceNormal);
                var start = positions.Count;
                for (var corner = 0; corner < 3; corner++)
                {
                    var vertexIndex = face.Indices[corner];
                    positions.Add(ToVector(mesh.Vertices[vertexIndex]));
                    normals.Add(mesh.HasNormals && vertexIndex < mesh.Normals.Count ? ToVector(mesh.Normals[vertexIndex]) : faceNormal);
                    indices.Add(start + corner);
                }
                colors.Add(faceColor);
            }
        }

        var result = StlParser.Complete(positions, indices, normals, colors);
        return new ModelMesh
        {
            Positions = result.Positions,
            Indices = result.Indices,
            Normals = result.Normals,
            TriangleColors = result.TriangleColors,
            UnitLabel = "units"
        };
    }

    private static Vector3 ToVector(Vector3D value) => new(value.X, value.Y, value.Z);

    private static uint PackColor(float red, float green, float blue, float alpha)
    {
        static byte B(float value) => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);
        return (uint)(B(alpha) << 24 | B(red) << 16 | B(green) << 8 | B(blue));
    }
}
