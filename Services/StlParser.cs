using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using MeshCaddy.Models;

namespace MeshCaddy.Services;

internal static class StlParser
{
    public static ModelMesh Parse(string path, CancellationToken token)
    {
        using var stream = File.OpenRead(path);
        return LooksBinary(stream) ? ParseBinary(stream, token) : ParseAscii(stream, token);
    }

    private static bool LooksBinary(Stream stream)
    {
        if (stream.Length < 84) return false;
        Span<byte> header = stackalloc byte[84];
        stream.ReadExactly(header);
        var triangleCount = BinaryPrimitives.ReadUInt32LittleEndian(header[80..84]);
        stream.Position = 0;
        // Some exporters append metadata or padding after the triangle records.
        // Requiring an exact length misclassifies those valid binary files as ASCII.
        var expectedLength = 84L + triangleCount * 50L;
        return triangleCount > 0 && expectedLength <= stream.Length;
    }

    private static ModelMesh ParseBinary(Stream stream, CancellationToken token)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        reader.ReadBytes(80);
        var count = reader.ReadUInt32();
        if (count > 20_000_000) throw new InvalidDataException("The STL contains too many triangles.");

        var positions = new List<Vector3>(checked((int)Math.Min(count * 3L, int.MaxValue)));
        var normals = new List<Vector3>(positions.Capacity);
        var indices = new List<int>(positions.Capacity);

        for (var t = 0u; t < count; t++)
        {
            if ((t & 4095) == 0) token.ThrowIfCancellationRequested();
            var normal = ReadVector(reader);
            var a = ReadVector(reader);
            var b = ReadVector(reader);
            var c = ReadVector(reader);
            reader.ReadUInt16();
            if (normal.LengthSquared() < 0.000001f) normal = FaceNormal(a, b, c);
            else normal = Vector3.Normalize(normal);
            AddTriangle(a, b, c, normal, positions, normals, indices);
        }

        return Complete(positions, indices, normals);
    }

    private static ModelMesh ParseAscii(Stream stream, CancellationToken token)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<int>();
        var vertices = new List<Vector3>(3);
        var normal = Vector3.Zero;
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            if ((positions.Count & 12287) == 0) token.ThrowIfCancellationRequested();
            var bits = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (bits.Length >= 5 && bits[0].Equals("facet", StringComparison.OrdinalIgnoreCase) && bits[1].Equals("normal", StringComparison.OrdinalIgnoreCase))
                normal = ParseVector(bits, 2);
            else if (bits.Length >= 4 && bits[0].Equals("vertex", StringComparison.OrdinalIgnoreCase))
            {
                vertices.Add(ParseVector(bits, 1));
                if (vertices.Count == 3)
                {
                    if (normal.LengthSquared() < 0.000001f) normal = FaceNormal(vertices[0], vertices[1], vertices[2]);
                    else normal = Vector3.Normalize(normal);
                    AddTriangle(vertices[0], vertices[1], vertices[2], normal, positions, normals, indices);
                    vertices.Clear();
                }
            }
        }

        return Complete(positions, indices, normals);
    }

    private static Vector3 ParseVector(string[] values, int offset) => new(
        float.Parse(values[offset], CultureInfo.InvariantCulture),
        float.Parse(values[offset + 1], CultureInfo.InvariantCulture),
        float.Parse(values[offset + 2], CultureInfo.InvariantCulture));

    private static Vector3 ReadVector(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    private static Vector3 FaceNormal(Vector3 a, Vector3 b, Vector3 c)
    {
        var normal = Vector3.Cross(b - a, c - a);
        return normal.LengthSquared() < 0.000001f ? Vector3.UnitZ : Vector3.Normalize(normal);
    }

    internal static void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, List<Vector3> positions, List<Vector3> normals, List<int> indices)
    {
        var start = positions.Count;
        positions.Add(a); positions.Add(b); positions.Add(c);
        normals.Add(normal); normals.Add(normal); normals.Add(normal);
        indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
    }

    internal static ModelMesh Complete(List<Vector3> positions, List<int> indices, List<Vector3> normals, List<uint?>? triangleColors = null, IReadOnlyList<ModelPlate>? plates = null)
    {
        if (positions.Count == 0) throw new InvalidDataException("No triangles were found in this model.");
        return new ModelMesh
        {
            Positions = positions,
            Indices = indices,
            Normals = normals,
            Plates = plates ?? [],
            TriangleColors = triangleColors is not null
                ? triangleColors
                : Enumerable.Repeat<uint?>(null, indices.Count / 3).ToArray()
        };
    }
}
