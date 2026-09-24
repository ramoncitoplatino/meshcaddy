using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using System.Xml.Linq;
using MeshCaddy.Models;

namespace MeshCaddy.Services;

internal static class ThreeMfParser
{
    private sealed record MeshTriangle(int A, int B, int C, uint? Color);
    private sealed record MeshObject(Vector3[] Vertices, MeshTriangle[] Triangles);
    private sealed record Component(int ObjectId, Matrix4x4 Transform);
    private sealed record ModelObject(MeshObject? Mesh, Component[] Components);

    public static ModelMesh Parse(string path, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(path);
        var modelEntries = archive.Entries
            .Where(e => e.FullName.EndsWith(".model", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.FullName.Equals("3D/3dmodel.model", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var entry = modelEntries.FirstOrDefault()
                    ?? throw new InvalidDataException("This 3MF file does not contain a model.");
        var slicerColors = ReadSlicerColors(archive);
        using var xml = entry.Open();
        var document = XDocument.Load(xml, LoadOptions.None);
        var root = document.Root ?? throw new InvalidDataException("The 3MF model XML is empty.");
        var ns = root.Name.Namespace;
        var scale = UnitScale((string?)root.Attribute("unit"));
        var colors = ReadColors(root, ns);
        var objects = new Dictionary<int, ModelObject>();

        foreach (var objectElement in root.Descendants(ns + "object"))
        {
            token.ThrowIfCancellationRequested();
            if (!int.TryParse((string?)objectElement.Attribute("id"), out var id)) continue;
            var meshElement = objectElement.Element(ns + "mesh");
            MeshObject? mesh = null;
            if (meshElement is not null)
            {
                var vertices = meshElement.Element(ns + "vertices")?.Elements(ns + "vertex")
                    .Select(v => new Vector3(F(v, "x") * scale, F(v, "y") * scale, F(v, "z") * scale)).ToArray() ?? [];
                var objectPid = NullableInt(objectElement, "pid");
                var objectIndex = NullableInt(objectElement, "pindex");
                var triangles = meshElement.Element(ns + "triangles")?.Elements(ns + "triangle")
                    .Select(t => new MeshTriangle(I(t, "v1"), I(t, "v2"), I(t, "v3"), ResolveColor(t, colors, objectPid, objectIndex))).ToArray() ?? [];
                mesh = new MeshObject(vertices, triangles);
            }
            var components = objectElement.Element(ns + "components")?.Elements(ns + "component")
                .Select(c => new Component(I(c, "objectid"), ParseTransform((string?)c.Attribute("transform"), scale))).ToArray() ?? [];
            objects[id] = new ModelObject(mesh, components);
        }

        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<int>();
        var triangleColors = new List<uint?>();
        var items = root.Element(ns + "build")?.Elements(ns + "item").ToArray() ?? [];

        if (items.Length == 0)
        {
            foreach (var id in objects.Keys) AppendObject(id, Matrix4x4.Identity, objects, positions, normals, indices, triangleColors, token, 0);
        }
        else
        {
            foreach (var item in items)
            {
                token.ThrowIfCancellationRequested();
                if (!int.TryParse((string?)item.Attribute("objectid"), out var id) || !objects.ContainsKey(id)) continue;
                AppendObject(id, ParseTransform((string?)item.Attribute("transform"), scale), objects, positions, normals, indices, triangleColors, token, 0);
            }
        }

        // Multi-part 3MF packages can keep their actual meshes in secondary model
        // parts and reference them through package relationships. If the primary
        // build produced no geometry, collect those mesh parts for preview.
        if (positions.Count == 0)
            AppendPackageMeshes(modelEntries, positions, normals, indices, triangleColors, slicerColors, token);

        return StlParser.Complete(positions, indices, normals, triangleColors);
    }

    private static void AppendPackageMeshes(IEnumerable<ZipArchiveEntry> entries, List<Vector3> positions, List<Vector3> normals, List<int> indices, List<uint?> triangleColors, Dictionary<int, uint> slicerColors, CancellationToken token)
    {
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            using var stream = entry.Open();
            var document = XDocument.Load(stream, LoadOptions.None);
            var root = document.Root;
            if (root is null) continue;
            var ns = root.Name.Namespace;
            var scale = UnitScale((string?)root.Attribute("unit"));
            var colors = ReadColors(root, ns);
            foreach (var meshElement in root.Descendants(ns + "mesh"))
            {
                var vertices = meshElement.Element(ns + "vertices")?.Elements(ns + "vertex")
                    .Select(v => new Vector3(F(v, "x") * scale, F(v, "y") * scale, F(v, "z") * scale)).ToArray() ?? [];
                var owner = meshElement.Parent;
                var objectPid = owner is null ? null : NullableInt(owner, "pid");
                var objectIndex = owner is null ? null : NullableInt(owner, "pindex");
                uint? slicerColor = owner is not null && int.TryParse((string?)owner.Attribute("id"), out var objectId) && slicerColors.TryGetValue(objectId, out var savedColor)
                    ? savedColor
                    : null;
                var triangles = meshElement.Element(ns + "triangles")?.Elements(ns + "triangle")
                    .Select(t => new MeshTriangle(I(t, "v1"), I(t, "v2"), I(t, "v3"), ResolveColor(t, colors, objectPid, objectIndex) ?? slicerColor)).ToArray() ?? [];
                Append(new MeshObject(vertices, triangles), Matrix4x4.Identity, positions, normals, indices, triangleColors, token);
            }
        }
    }

    private static void AppendObject(int id, Matrix4x4 transform, Dictionary<int, ModelObject> objects, List<Vector3> positions, List<Vector3> normals, List<int> indices, List<uint?> triangleColors, CancellationToken token, int depth)
    {
        if (depth > 64) throw new InvalidDataException("The 3MF component hierarchy is too deep.");
        if (!objects.TryGetValue(id, out var model)) return;
        if (model.Mesh is not null) Append(model.Mesh, transform, positions, normals, indices, triangleColors, token);
        foreach (var component in model.Components)
            AppendObject(component.ObjectId, component.Transform * transform, objects, positions, normals, indices, triangleColors, token, depth + 1);
    }

    private static void Append(MeshObject model, Matrix4x4 transform, List<Vector3> positions, List<Vector3> normals, List<int> indices, List<uint?> triangleColors, CancellationToken token)
    {
        for (var index = 0; index < model.Triangles.Length; index++)
        {
            if ((index & 4095) == 0) token.ThrowIfCancellationRequested();
            var triangle = model.Triangles[index];
            if ((uint)triangle.A >= model.Vertices.Length || (uint)triangle.B >= model.Vertices.Length || (uint)triangle.C >= model.Vertices.Length) continue;
            var a = Vector3.Transform(model.Vertices[triangle.A], transform);
            var b = Vector3.Transform(model.Vertices[triangle.B], transform);
            var c = Vector3.Transform(model.Vertices[triangle.C], transform);
            var normal = Vector3.Cross(b - a, c - a);
            normal = normal.LengthSquared() < 0.000001f ? Vector3.UnitZ : Vector3.Normalize(normal);
            StlParser.AddTriangle(a, b, c, normal, positions, normals, indices);
            triangleColors.Add(triangle.Color);
        }
    }

    private static Dictionary<(int ResourceId, int Index), uint> ReadColors(XElement root, XNamespace ns)
    {
        var result = new Dictionary<(int, int), uint>();
        var resources = root.Element(ns + "resources");
        if (resources is null) return result;
        foreach (var group in resources.Elements())
        {
            if (group.Name.LocalName is not ("basematerials" or "colorgroup") || !int.TryParse((string?)group.Attribute("id"), out var id)) continue;
            var entries = group.Elements().ToArray();
            for (var index = 0; index < entries.Length; index++)
            {
                var text = (string?)entries[index].Attribute(group.Name.LocalName == "basematerials" ? "displaycolor" : "color");
                if (TryParseColor(text, out var color)) result[(id, index)] = color;
            }
        }
        return result;
    }

    private static Dictionary<int, uint> ReadSlicerColors(ZipArchive archive)
    {
        var result = new Dictionary<int, uint>();
        var projectEntry = archive.Entries.FirstOrDefault(e => e.FullName.Equals("Metadata/project_settings.config", StringComparison.OrdinalIgnoreCase));
        var modelEntry = archive.Entries.FirstOrDefault(e => e.FullName.Equals("Metadata/model_settings.config", StringComparison.OrdinalIgnoreCase));
        if (projectEntry is null || modelEntry is null) return result;

        try
        {
            using var projectStream = projectEntry.Open();
            using var json = JsonDocument.Parse(projectStream);
            if (!json.RootElement.TryGetProperty("filament_colour", out var paletteElement) || paletteElement.ValueKind != JsonValueKind.Array) return result;
            var palette = paletteElement.EnumerateArray()
                .Select(value => TryParseColor(value.GetString(), out var color) ? (uint?)color : null)
                .ToArray();

            using var settingsStream = modelEntry.Open();
            var settings = XDocument.Load(settingsStream, LoadOptions.None);
            foreach (var objectElement in settings.Descendants().Where(e => e.Name.LocalName == "object"))
            {
                var inheritedExtruder = ReadExtruder(objectElement);
                if (int.TryParse((string?)objectElement.Attribute("id"), out var objectId))
                    AssignColor(objectId, inheritedExtruder, palette, result);

                foreach (var partElement in objectElement.Elements().Where(e => e.Name.LocalName == "part"))
                {
                    if (!int.TryParse((string?)partElement.Attribute("id"), out var partId)) continue;
                    AssignColor(partId, ReadExtruder(partElement) ?? inheritedExtruder, palette, result);
                }
            }
        }
        catch (Exception)
        {
            // Slicer metadata is optional and must never prevent geometry preview.
        }
        return result;
    }

    private static int? ReadExtruder(XElement element)
    {
        var metadata = element.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata" && (string?)e.Attribute("key") == "extruder");
        return int.TryParse((string?)metadata?.Attribute("value"), out var value) ? value : null;
    }

    private static void AssignColor(int objectId, int? extruderNumber, uint?[] palette, Dictionary<int, uint> result)
    {
        if (!extruderNumber.HasValue) return;
        var paletteIndex = extruderNumber.Value - 1;
        if ((uint)paletteIndex < palette.Length && palette[paletteIndex].HasValue)
            result[objectId] = palette[paletteIndex]!.Value;
    }

    private static uint? ResolveColor(XElement triangle, Dictionary<(int ResourceId, int Index), uint> colors, int? inheritedPid, int? inheritedIndex)
    {
        var pid = NullableInt(triangle, "pid") ?? inheritedPid;
        var index = NullableInt(triangle, "p1") ?? inheritedIndex;
        return pid.HasValue && index.HasValue && colors.TryGetValue((pid.Value, index.Value), out var color) ? color : null;
    }

    private static bool TryParseColor(string? value, out uint color)
    {
        color = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var hex = value.Trim().TrimStart('#');
        if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed)) return false;
        color = hex.Length == 6 ? 0xFF000000u | parsed : ((parsed & 0xFFu) << 24) | (parsed >> 8);
        return true;
    }

    private static int? NullableInt(XElement element, string name) => int.TryParse((string?)element.Attribute(name), out var value) ? value : null;

    private static Matrix4x4 ParseTransform(string? value, float unitScale)
    {
        if (string.IsNullOrWhiteSpace(value)) return Matrix4x4.Identity;
        var v = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        if (v.Length != 12) return Matrix4x4.Identity;
        return new Matrix4x4(v[0], v[1], v[2], 0, v[3], v[4], v[5], 0, v[6], v[7], v[8], 0, v[9] * unitScale, v[10] * unitScale, v[11] * unitScale, 1);
    }

    private static float UnitScale(string? unit) => unit?.ToLowerInvariant() switch
    {
        "micron" or "micrometer" => 0.001f,
        "centimeter" => 10f,
        "meter" => 1000f,
        "inch" => 25.4f,
        "foot" => 304.8f,
        _ => 1f
    };

    private static float F(XElement element, string name) => float.Parse((string?)element.Attribute(name) ?? "0", CultureInfo.InvariantCulture);
    private static int I(XElement element, string name) => int.Parse((string?)element.Attribute(name) ?? "-1", CultureInfo.InvariantCulture);
}
