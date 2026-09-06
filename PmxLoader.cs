using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Media3D;

namespace HimeMikotoDesktopNative;

internal static class PmxLoader
{
    public static PmxModel Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        return new PmxReader(reader, path).Read();
    }
}

internal sealed class PmxModel
{
    public PmxModel(string sourcePath)
    {
        SourcePath = sourcePath;
    }

    public string SourcePath { get; }
    public string Name { get; set; } = string.Empty;
    public List<PmxVertex> Vertices { get; } = [];
    public List<int> Indices { get; } = [];
    public List<PmxMaterial> Materials { get; } = [];
    public List<PmxBone> Bones { get; } = [];
    public List<PmxMorph> Morphs { get; } = [];
    public Point3D MinBounds { get; set; }
    public Point3D MaxBounds { get; set; }

    public int FindBlinkMorph()
    {
        var preferred = new[] { "まばたき", "ウィンク", "blink", "wink", "眨眼", "闭眼" };
        foreach (var keyword in preferred)
        {
            var exact = Morphs.FindIndex(m => string.Equals(m.Name, keyword, StringComparison.OrdinalIgnoreCase));
            if (exact >= 0)
            {
                return exact;
            }

            var partial = Morphs.FindIndex(m => m.Type is 0 or 1 && m.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            if (partial >= 0)
            {
                return partial;
            }
        }

        return -1;
    }

    public int FindVertexMorphIndex(params string[] names)
    {
        return FindMorphIndex(names, morph => morph.Type is 0 or 1);
    }

    public int FindMaterialMorphIndex(params string[] names)
    {
        return FindMorphIndex(names, morph => morph.Type == 8);
    }

    private int FindMorphIndex(IEnumerable<string> names, Func<PmxMorph, bool> predicate)
    {
        var candidates = names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
        foreach (var name in candidates)
        {
            var exact = Morphs.FindIndex(morph => predicate(morph)
                && string.Equals(morph.Name, name, StringComparison.OrdinalIgnoreCase));
            if (exact >= 0)
            {
                return exact;
            }
        }

        foreach (var name in candidates)
        {
            var partial = Morphs.FindIndex(morph => predicate(morph)
                && morph.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (partial >= 0)
            {
                return partial;
            }
        }

        return -1;
    }

    public Dictionary<int, Vector3D> BuildVertexMorph(int morphIndex, double factor = 1.0)
    {
        var result = new Dictionary<int, Vector3D>();
        if (morphIndex < 0 || morphIndex >= Morphs.Count || Math.Abs(factor) < 0.000001)
        {
            return result;
        }

        AccumulateMorph(morphIndex, factor, result, new HashSet<int>());
        return result;
    }

    public List<PmxWeightedMaterialMorphOffset> BuildMaterialMorph(int morphIndex, double factor = 1.0)
    {
        var result = new List<PmxWeightedMaterialMorphOffset>();
        if (morphIndex < 0 || morphIndex >= Morphs.Count || Math.Abs(factor) < 0.000001)
        {
            return result;
        }

        AccumulateMaterialMorph(morphIndex, factor, result, new HashSet<int>());
        return result;
    }

    private void AccumulateMorph(
        int morphIndex,
        double factor,
        Dictionary<int, Vector3D> result,
        HashSet<int> visiting)
    {
        if (morphIndex < 0 || morphIndex >= Morphs.Count || Math.Abs(factor) < 0.000001 || !visiting.Add(morphIndex))
        {
            return;
        }

        var morph = Morphs[morphIndex];
        if (morph.Type == 1)
        {
            foreach (var offset in morph.VertexOffsets)
            {
                var scaled = new Vector3D(
                    offset.Offset.X * factor,
                    offset.Offset.Y * factor,
                    offset.Offset.Z * factor);
                if (result.TryGetValue(offset.VertexIndex, out var previous))
                {
                    result[offset.VertexIndex] = previous + scaled;
                }
                else
                {
                    result[offset.VertexIndex] = scaled;
                }
            }
        }
        else if (morph.Type == 0)
        {
            foreach (var offset in morph.GroupOffsets)
            {
                AccumulateMorph(offset.MorphIndex, factor * offset.Weight, result, visiting);
            }
        }

        visiting.Remove(morphIndex);
    }

    private void AccumulateMaterialMorph(
        int morphIndex,
        double factor,
        List<PmxWeightedMaterialMorphOffset> result,
        HashSet<int> visiting)
    {
        if (morphIndex < 0 || morphIndex >= Morphs.Count || Math.Abs(factor) < 0.000001 || !visiting.Add(morphIndex))
        {
            return;
        }

        var morph = Morphs[morphIndex];
        if (morph.Type == 8)
        {
            foreach (var offset in morph.MaterialOffsets)
            {
                result.Add(new PmxWeightedMaterialMorphOffset(offset, factor));
            }
        }
        else if (morph.Type == 0)
        {
            foreach (var offset in morph.GroupOffsets)
            {
                AccumulateMaterialMorph(offset.MorphIndex, factor * offset.Weight, result, visiting);
            }
        }

        visiting.Remove(morphIndex);
    }
}

internal sealed class PmxVertex
{
    public Point3D Position { get; init; }
    public Vector3D Normal { get; init; }
    public Point UV { get; init; }
    public int[] BoneIndices { get; set; } = [];
    public float[] BoneWeights { get; set; } = [];
}

internal sealed class PmxMaterial
{
    public string Name { get; init; } = string.Empty;
    public PmxColor Diffuse { get; init; }
    public byte Flags { get; init; }
    public string TexturePath { get; init; } = string.Empty;
    public int IndexCount { get; init; }
}

internal sealed class PmxBone
{
    public string Name { get; init; } = string.Empty;
    public Point3D Position { get; init; }
    public int ParentIndex { get; init; }
}

internal sealed class PmxMorph
{
    public PmxMorph(string name, byte type)
    {
        Name = name;
        Type = type;
    }

    public string Name { get; }
    public byte Type { get; }
    public List<PmxVertexMorphOffset> VertexOffsets { get; } = [];
    public List<PmxGroupMorphOffset> GroupOffsets { get; } = [];
    public List<PmxMaterialMorphOffset> MaterialOffsets { get; } = [];
}

internal readonly record struct PmxVertexMorphOffset(int VertexIndex, Vector3D Offset);
internal readonly record struct PmxGroupMorphOffset(int MorphIndex, float Weight);
internal readonly record struct PmxMaterialMorphOffset(int MaterialIndex, byte Operation, PmxColor Diffuse);
internal readonly record struct PmxWeightedMaterialMorphOffset(PmxMaterialMorphOffset Offset, double Weight);
internal readonly record struct PmxColor(float R, float G, float B, float A);

internal sealed class PmxReader
{
    private readonly BinaryReader _reader;
    private readonly string _sourcePath;
    private Encoding _encoding = Encoding.UTF8;
    private int _vertexIndexSize;
    private int _textureIndexSize;
    private int _materialIndexSize;
    private int _boneIndexSize;
    private int _morphIndexSize;
    private int _rigidBodyIndexSize;

    public PmxReader(BinaryReader reader, string sourcePath)
    {
        _reader = reader;
        _sourcePath = sourcePath;
    }

    public PmxModel Read()
    {
        var signature = Encoding.ASCII.GetString(ReadBytes(4));
        if (!string.Equals(signature, "PMX ", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Not a PMX file: {_sourcePath}");
        }

        var version = _reader.ReadSingle();
        if (version < 2.0f || version > 3.0f)
        {
            throw new InvalidDataException($"Unsupported PMX version {version}.");
        }

        var headerSize = _reader.ReadByte();
        var settings = ReadBytes(headerSize);
        if (settings.Length < 8)
        {
            throw new InvalidDataException("The PMX header is incomplete.");
        }

        _encoding = settings[0] == 0 ? Encoding.Unicode : Encoding.UTF8;
        _additionalUvCount = settings[1];
        _vertexIndexSize = settings[2];
        _textureIndexSize = settings[3];
        _materialIndexSize = settings[4];
        _boneIndexSize = settings[5];
        _morphIndexSize = settings[6];
        _rigidBodyIndexSize = settings[7];

        var model = new PmxModel(_sourcePath)
        {
            Name = ReadString(),
        };
        _ = ReadString();
        _ = ReadString();
        _ = ReadString();

        ReadVertices(model);
        ReadIndices(model);
        var textures = ReadTextures();
        ReadMaterials(model, textures);
        ReadBones(model);
        ReadMorphs(model);

        if (model.Vertices.Count > 0)
        {
            var first = model.Vertices[0].Position;
            var minX = first.X;
            var minY = first.Y;
            var minZ = first.Z;
            var maxX = first.X;
            var maxY = first.Y;
            var maxZ = first.Z;
            foreach (var vertex in model.Vertices)
            {
                minX = Math.Min(minX, vertex.Position.X);
                minY = Math.Min(minY, vertex.Position.Y);
                minZ = Math.Min(minZ, vertex.Position.Z);
                maxX = Math.Max(maxX, vertex.Position.X);
                maxY = Math.Max(maxY, vertex.Position.Y);
                maxZ = Math.Max(maxZ, vertex.Position.Z);
            }

            model.MinBounds = new Point3D(minX, minY, minZ);
            model.MaxBounds = new Point3D(maxX, maxY, maxZ);
        }

        return model;
    }

    private void ReadVertices(PmxModel model)
    {
        var count = ReadCount("vertex", 5_000_000);
        for (var index = 0; index < count; index++)
        {
            var vertex = new PmxVertex
            {
                Position = ReadPoint3D(),
                Normal = ReadVector3D(),
                UV = ReadPoint(),
            };

            // The count is stored in the header and kept in _additionalUvCount by ReadHeader.
            for (var uvIndex = 0; uvIndex < _additionalUvCount; uvIndex++)
            {
                SkipFloats(4);
            }

            var weightType = _reader.ReadByte();
            switch (weightType)
            {
                case 0:
                    vertex.BoneIndices = [_reader.ReadIndex(_boneIndexSize)];
                    vertex.BoneWeights = [1.0f];
                    break;
                case 1:
                    var boneA = _reader.ReadIndex(_boneIndexSize);
                    var boneB = _reader.ReadIndex(_boneIndexSize);
                    var weightA = _reader.ReadSingle();
                    vertex.BoneIndices = [boneA, boneB];
                    vertex.BoneWeights = [weightA, 1.0f - weightA];
                    break;
                case 2:
                case 4:
                    vertex.BoneIndices =
                    [
                        _reader.ReadIndex(_boneIndexSize),
                        _reader.ReadIndex(_boneIndexSize),
                        _reader.ReadIndex(_boneIndexSize),
                        _reader.ReadIndex(_boneIndexSize),
                    ];
                    vertex.BoneWeights =
                    [
                        _reader.ReadSingle(),
                        _reader.ReadSingle(),
                        _reader.ReadSingle(),
                        _reader.ReadSingle(),
                    ];
                    break;
                case 3:
                    var sdefBoneA = _reader.ReadIndex(_boneIndexSize);
                    var sdefBoneB = _reader.ReadIndex(_boneIndexSize);
                    var sdefWeight = _reader.ReadSingle();
                    SkipFloats(9);
                    vertex.BoneIndices = [sdefBoneA, sdefBoneB];
                    vertex.BoneWeights = [sdefWeight, 1.0f - sdefWeight];
                    break;
                default:
                    throw new InvalidDataException($"Unsupported PMX vertex weight type {weightType}.");
            }

            _ = _reader.ReadSingle();
            model.Vertices.Add(vertex);
        }
    }

    private void ReadIndices(PmxModel model)
    {
        var count = ReadCount("index", 20_000_000);
        for (var index = 0; index < count; index++)
        {
            model.Indices.Add(_reader.ReadIndex(_vertexIndexSize));
        }
    }

    private List<string> ReadTextures()
    {
        var count = ReadCount("texture", 100_000);
        var textures = new List<string>(count);
        for (var index = 0; index < count; index++)
        {
            textures.Add(ReadString());
        }

        return textures;
    }

    private void ReadMaterials(PmxModel model, IReadOnlyList<string> textures)
    {
        var count = ReadCount("material", 100_000);
        for (var index = 0; index < count; index++)
        {
            var name = ReadString();
            _ = ReadString();
            var diffuse = ReadColor4();
            SkipFloats(3);
            _ = _reader.ReadSingle();
            SkipFloats(3);
            var flags = _reader.ReadByte();
            _ = ReadColor4();
            _ = _reader.ReadSingle();

            var textureIndex = _reader.ReadIndex(_textureIndexSize);
            _ = _reader.ReadIndex(_textureIndexSize);
            _ = _reader.ReadByte();
            var toonShared = _reader.ReadByte();
            if (toonShared == 0)
            {
                _ = _reader.ReadIndex(_textureIndexSize);
            }
            else
            {
                _ = _reader.ReadByte();
            }

            _ = ReadString();
            var faceCount = _reader.ReadInt32();
            var texturePath = textureIndex >= 0 && textureIndex < textures.Count
                ? textures[textureIndex]
                : string.Empty;
            model.Materials.Add(new PmxMaterial
            {
                Name = name,
                Diffuse = diffuse,
                Flags = flags,
                TexturePath = texturePath,
                IndexCount = faceCount,
            });
        }
    }

    private void ReadBones(PmxModel model)
    {
        var count = ReadCount("bone", 100_000);
        for (var index = 0; index < count; index++)
        {
            var name = ReadString();
            _ = ReadString();
            var position = ReadPoint3D();
            var parentIndex = _reader.ReadIndex(_boneIndexSize);
            _ = _reader.ReadInt32();
            var flags = _reader.ReadUInt16();

            if ((flags & 0x0001) != 0)
            {
                _ = _reader.ReadIndex(_boneIndexSize);
            }
            else
            {
                SkipFloats(3);
            }

            if ((flags & 0x0100) != 0 || (flags & 0x0200) != 0)
            {
                _ = _reader.ReadIndex(_boneIndexSize);
                _ = _reader.ReadSingle();
            }

            if ((flags & 0x0400) != 0)
            {
                SkipFloats(3);
            }

            if ((flags & 0x0800) != 0)
            {
                SkipFloats(6);
            }

            if ((flags & 0x2000) != 0)
            {
                _ = _reader.ReadInt32();
            }

            if ((flags & 0x0020) != 0)
            {
                _ = _reader.ReadIndex(_boneIndexSize);
                _ = _reader.ReadInt32();
                _ = _reader.ReadSingle();
                var linkCount = ReadCount("IK link", 10_000);
                for (var link = 0; link < linkCount; link++)
                {
                    _ = _reader.ReadIndex(_boneIndexSize);
                    var hasLimit = _reader.ReadByte();
                    if (hasLimit != 0)
                    {
                        SkipFloats(6);
                    }
                }
            }

            model.Bones.Add(new PmxBone
            {
                Name = name,
                Position = position,
                ParentIndex = parentIndex,
            });
        }
    }

    private void ReadMorphs(PmxModel model)
    {
        var count = ReadCount("morph", 100_000);
        for (var index = 0; index < count; index++)
        {
            var morph = new PmxMorph(ReadString(), 0);
            _ = ReadString();
            _ = _reader.ReadByte();
            var type = _reader.ReadByte();
            var offsetCount = ReadCount("morph offset", 20_000_000);
            morph = new PmxMorph(morph.Name, type);

            switch (type)
            {
                case 0:
                    for (var offset = 0; offset < offsetCount; offset++)
                    {
                        morph.GroupOffsets.Add(new PmxGroupMorphOffset(
                            _reader.ReadIndex(_morphIndexSize),
                            _reader.ReadSingle()));
                    }

                    break;
                case 1:
                    for (var offset = 0; offset < offsetCount; offset++)
                    {
                        morph.VertexOffsets.Add(new PmxVertexMorphOffset(
                            _reader.ReadIndex(_vertexIndexSize),
                            ReadVector3D()));
                    }

                    break;
                case 2:
                    for (var offset = 0; offset < offsetCount; offset++)
                    {
                        _ = _reader.ReadIndex(_boneIndexSize);
                        SkipFloats(3);
                        SkipFloats(4);
                    }

                    break;
                case 3:
                case 4:
                case 5:
                case 6:
                case 7:
                    for (var offset = 0; offset < offsetCount; offset++)
                    {
                        _ = _reader.ReadIndex(_vertexIndexSize);
                        SkipFloats(4);
                    }

                    break;
                case 8:
                    for (var offset = 0; offset < offsetCount; offset++)
                    {
                        // The material index and operation together occupy two bytes in these
                        // files. Keep the operation because clothing presets use the diffuse
                        // alpha channel to hide or reveal a material.
                        var materialIndex = _reader.ReadIndex(_materialIndexSize);
                        var operation = _reader.ReadByte();
                        var diffuse = ReadColor4();
                        SkipFloats(24);
                        morph.MaterialOffsets.Add(new PmxMaterialMorphOffset(materialIndex, operation, diffuse));
                    }

                    break;
                case 9:
                    for (var offset = 0; offset < offsetCount; offset++)
                    {
                        _ = _reader.ReadIndex(_morphIndexSize);
                        _ = _reader.ReadSingle();
                    }

                    break;
                case 10:
                    for (var offset = 0; offset < offsetCount; offset++)
                    {
                        _ = _reader.ReadIndex(_rigidBodyIndexSize);
                        _ = _reader.ReadByte();
                        SkipFloats(6);
                    }

                    break;
                default:
                    throw new InvalidDataException($"Unsupported PMX morph type {type}.");
            }

            model.Morphs.Add(morph);
        }
    }

    private int _additionalUvCount;
    private Point3D ReadPoint3D()
    {
        return new Point3D(_reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle());
    }

    private Vector3D ReadVector3D()
    {
        return new Vector3D(_reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle());
    }

    private Point ReadPoint()
    {
        return new Point(_reader.ReadSingle(), _reader.ReadSingle());
    }

    private PmxColor ReadColor4()
    {
        var color = new PmxColor(
            _reader.ReadSingle(),
            _reader.ReadSingle(),
            _reader.ReadSingle(),
            _reader.ReadSingle());
        return color;
    }

    private void SkipFloats(int count)
    {
        _reader.BaseStream.Seek(sizeof(float) * count, SeekOrigin.Current);
    }

    private string ReadString()
    {
        var byteCount = _reader.ReadInt32();
        if (byteCount <= 0)
        {
            return string.Empty;
        }

        if (byteCount > 100_000_000)
        {
            throw new InvalidDataException($"Invalid PMX string length {byteCount}.");
        }

        var value = _encoding.GetString(ReadBytes(byteCount));
        return value.TrimEnd('\0');
    }

    private byte[] ReadBytes(int count)
    {
        var bytes = _reader.ReadBytes(count);
        if (bytes.Length != count)
        {
            throw new EndOfStreamException($"Unexpected end of PMX while reading {count} bytes.");
        }

        return bytes;
    }

    private int ReadCount(string label, int maximum)
    {
        var count = _reader.ReadInt32();
        if (count < 0 || count > maximum)
        {
            throw new InvalidDataException($"Invalid PMX {label} count {count}.");
        }

        return count;
    }

}

internal static class BinaryReaderExtensions
{
    public static int ReadIndex(this BinaryReader reader, int size)
    {
        return size switch
        {
            1 => reader.ReadSByte(),
            2 => reader.ReadInt16(),
            4 => reader.ReadInt32(),
            _ => throw new InvalidDataException($"Unsupported PMX index size {size}."),
        };
    }
}
