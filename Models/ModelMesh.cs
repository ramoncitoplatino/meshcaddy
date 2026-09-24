using System.Numerics;

namespace MeshCaddy.Models;

public sealed class ModelMesh
{
    public IReadOnlyList<ModelPlate> Plates { get; init; } = [];
    public required IReadOnlyList<Vector3> Positions { get; init; }
    public required IReadOnlyList<int> Indices { get; init; }
    public required IReadOnlyList<Vector3> Normals { get; init; }
    public IReadOnlyList<uint?> TriangleColors { get; init; } = [];
    public string UnitLabel { get; init; } = "mm";
    public int TriangleCount => Indices.Count / 3;

    public Vector3 Min => Positions.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
    public Vector3 Max => Positions.Aggregate(new Vector3(float.MinValue), Vector3.Max);
}

public sealed record ModelPlate(string Name, int FirstTriangle, int TriangleCount);
