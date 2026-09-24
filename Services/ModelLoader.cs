using MeshCaddy.Models;

namespace MeshCaddy.Services;

public static class ModelLoader
{
    public static readonly IReadOnlySet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".3d", ".3ds", ".3mf", ".ac", ".ac3d", ".acc", ".amf", ".ase", ".ask", ".assbin",
        ".b3d", ".blend", ".bvh", ".cob", ".csm", ".dae", ".dxf", ".enff", ".fbx", ".glb",
        ".gltf", ".hmp", ".ifc", ".ifczip", ".iqm", ".irr", ".irrmesh", ".lwo", ".lws", ".lxo",
        ".md2", ".md3", ".md5mesh", ".mdc", ".mdl", ".mesh", ".mot", ".ms3d", ".ndo", ".nff",
        ".obj", ".off", ".ogex", ".pk3", ".ply", ".pmx", ".q3o", ".q3s", ".raw", ".scn",
        ".sib", ".smd", ".stl", ".ter", ".uc", ".vta", ".x", ".x3d", ".xgl", ".zgl"
    };

    public static Task<ModelMesh> LoadAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".stl" => StlParser.Parse(path, cancellationToken),
            ".3mf" => ThreeMfParser.Parse(path, cancellationToken),
            _ when SupportedExtensions.Contains(Path.GetExtension(path)) => AssimpModelLoader.Parse(path, cancellationToken),
            _ => throw new NotSupportedException("This model format is not supported by the installed importer.")
        }, cancellationToken);
}
