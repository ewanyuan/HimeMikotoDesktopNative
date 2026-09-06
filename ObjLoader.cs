using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace HimeMikotoDesktopNative;

internal static class ObjLoader
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static Model3DGroup Load(
        string objPath,
        Func<string, bool>? materialVisible = null,
        Func<string, double?>? materialOpacity = null)
    {
        var objDirectory = Path.GetDirectoryName(objPath) ?? Directory.GetCurrentDirectory();
        var positions = new List<Point3D>();
        var normals = new List<Vector3D>();
        var texcoords = new List<Point>();
        var materials = new Dictionary<string, ObjMaterial>(StringComparer.OrdinalIgnoreCase)
        {
            ["__default__"] = new ObjMaterial(),
        };
        var builders = new Dictionary<string, MeshBuilder>(StringComparer.OrdinalIgnoreCase);
        var currentMaterial = "__default__";

        foreach (var rawLine in File.ReadLines(objPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf(' ');
            if (separator < 0)
            {
                continue;
            }

            var command = line[..separator];
            var value = line[(separator + 1)..].Trim();
            switch (command)
            {
                case "mtllib":
                    ParseMaterialLibrary(Path.Combine(objDirectory, NormalizePath(value)), materials);
                    break;
                case "v":
                    positions.Add(ParsePosition(value));
                    break;
                case "vn":
                    normals.Add(ParseNormal(value));
                    break;
                case "vt":
                    texcoords.Add(ParseTexcoord(value));
                    break;
                case "usemtl":
                    currentMaterial = value;
                    if (!materials.ContainsKey(currentMaterial))
                    {
                        materials[currentMaterial] = new ObjMaterial();
                    }
                    break;
                case "f":
                    var face = ParseFace(value, positions.Count, texcoords.Count, normals.Count);
                    if (face.Count >= 3)
                    {
                        if (!builders.TryGetValue(currentMaterial, out var builder))
                        {
                            builder = new MeshBuilder();
                            builders[currentMaterial] = builder;
                        }

                        for (var index = 1; index < face.Count - 1; index++)
                        {
                            builder.AddTriangle(face[0], face[index], face[index + 1], positions, texcoords, normals);
                        }
                    }
                    break;
            }
        }

        var group = new Model3DGroup();
        foreach (var pair in builders)
        {
            var mesh = pair.Value.Build();
            if (mesh.Positions.Count == 0)
            {
                continue;
            }

            if (materialVisible != null && !materialVisible(pair.Key))
            {
                continue;
            }

            var material = materials.TryGetValue(pair.Key, out var sourceMaterial)
                ? sourceMaterial
                : materials["__default__"];
            var brush = CreateBrush(material, objDirectory, materialOpacity?.Invoke(pair.Key));
            var front = new DiffuseMaterial(brush);
            front.Freeze();
            var geometry = new GeometryModel3D(mesh, front)
            {
                BackMaterial = front,
            };
            group.Children.Add(geometry);
        }

        return group;
    }

    private static void ParseMaterialLibrary(string path, Dictionary<string, ObjMaterial> materials)
    {
        if (!File.Exists(path))
        {
            return;
        }

        ObjMaterial? current = null;
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf(' ');
            if (separator < 0)
            {
                continue;
            }

            var command = line[..separator];
            var value = line[(separator + 1)..].Trim();
            switch (command)
            {
                case "newmtl":
                    current = new ObjMaterial();
                    materials[value] = current;
                    break;
                case "Kd":
                    if (current != null)
                    {
                        var values = Numbers(value, 3);
                        current.Diffuse = Color.FromScRgb(1.0f, (float)values[0], (float)values[1], (float)values[2]);
                    }
                    break;
                case "d":
                    if (current != null)
                    {
                        current.Opacity = Clamp01(FirstNumber(value));
                    }
                    break;
                case "Tr":
                    if (current != null)
                    {
                        current.Opacity = 1.0 - Clamp01(FirstNumber(value));
                    }
                    break;
                case "map_Kd":
                    if (current != null)
                    {
                        current.Texture = NormalizePath(value.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty);
                    }
                    break;
            }
        }
    }

    private static Brush CreateBrush(ObjMaterial material, string directory, double? opacityOverride = null)
    {
        var opacity = Clamp01(opacityOverride ?? material.Opacity);
        if (!string.IsNullOrWhiteSpace(material.Texture))
        {
            var texturePath = Path.Combine(directory, material.Texture);
            if (File.Exists(texturePath))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new System.Uri(texturePath, System.UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();

                var imageBrush = new ImageBrush(bitmap)
                {
                    Stretch = Stretch.Fill,
                    Opacity = opacity,
                };
                imageBrush.Freeze();
                return imageBrush;
            }
        }

        var color = material.Diffuse;
        var solid = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(opacity * 255.0),
            color.R,
            color.G,
            color.B));
        solid.Freeze();
        return solid;
    }

    private static Point3D ParsePosition(string value)
    {
        var values = Numbers(value, 3);
        return new Point3D(values[0], values[1], values[2]);
    }

    private static Vector3D ParseNormal(string value)
    {
        var values = Numbers(value, 3);
        return new Vector3D(values[0], values[1], values[2]);
    }

    private static Point ParseTexcoord(string value)
    {
        var values = Numbers(value, 2);
        return new Point(values[0], values[1]);
    }

    private static List<FaceIndex> ParseFace(string value, int positionCount, int texcoordCount, int normalCount)
    {
        var result = new List<FaceIndex>();
        foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = token.Split('/');
            var position = ResolveIndex(fields[0], positionCount);
            if (position < 0 || position >= positionCount)
            {
                continue;
            }

            var texcoord = fields.Length > 1 && fields[1].Length > 0
                ? ResolveIndex(fields[1], texcoordCount)
                : -1;
            var normal = fields.Length > 2 && fields[2].Length > 0
                ? ResolveIndex(fields[2], normalCount)
                : -1;
            result.Add(new FaceIndex(position, texcoord, normal));
        }

        return result;
    }

    private static int ResolveIndex(string value, int count)
    {
        var parsed = int.Parse(value, Invariant);
        return parsed > 0 ? parsed - 1 : count + parsed;
    }

    private static double[] Numbers(string value, int count)
    {
        var result = new double[count];
        var values = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < count && index < values.Length; index++)
        {
            result[index] = double.Parse(values[index], Invariant);
        }

        return result;
    }

    private static double FirstNumber(string value)
    {
        return double.Parse(value.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0], Invariant);
    }

    private static string NormalizePath(string value)
    {
        return value.Trim().Replace('\\', Path.DirectorySeparatorChar).Replace("\\\\", Path.DirectorySeparatorChar.ToString());
    }

    private static double Clamp01(double value)
    {
        return Math.Clamp(value, 0.0, 1.0);
    }

    private sealed class ObjMaterial
    {
        public Color Diffuse { get; set; } = Colors.White;
        public double Opacity { get; set; } = 1.0;
        public string Texture { get; set; } = string.Empty;
    }

    private readonly record struct FaceIndex(int Position, int Texcoord, int Normal);

    private sealed class MeshBuilder
    {
        private readonly Dictionary<string, int> _vertexMap = new(StringComparer.Ordinal);
        private readonly List<Point3D> _positions = [];
        private readonly List<Vector3D> _normals = [];
        private readonly List<Point> _texcoords = [];
        private readonly List<int> _indices = [];
        private bool _hasNormals;

        public void AddTriangle(
            FaceIndex first,
            FaceIndex second,
            FaceIndex third,
            IReadOnlyList<Point3D> positions,
            IReadOnlyList<Point> texcoords,
            IReadOnlyList<Vector3D> normals)
        {
            _indices.Add(AddVertex(first, positions, texcoords, normals));
            _indices.Add(AddVertex(second, positions, texcoords, normals));
            _indices.Add(AddVertex(third, positions, texcoords, normals));
        }

        public MeshGeometry3D Build()
        {
            var mesh = new MeshGeometry3D
            {
                Positions = new Point3DCollection(_positions),
                TextureCoordinates = new PointCollection(_texcoords),
                TriangleIndices = new Int32Collection(_indices),
            };
            if (_hasNormals)
            {
                mesh.Normals = new Vector3DCollection(_normals);
            }

            return mesh;
        }

        private int AddVertex(
            FaceIndex face,
            IReadOnlyList<Point3D> positions,
            IReadOnlyList<Point> texcoords,
            IReadOnlyList<Vector3D> normals)
        {
            var key = $"{face.Position}/{face.Texcoord}/{face.Normal}";
            if (_vertexMap.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var index = _positions.Count;
            _vertexMap[key] = index;
            _positions.Add(positions[face.Position]);
            _texcoords.Add(face.Texcoord >= 0 && face.Texcoord < texcoords.Count
                ? new Point(texcoords[face.Texcoord].X, 1.0 - texcoords[face.Texcoord].Y)
                : new Point(0.0, 0.0));
            if (face.Normal >= 0 && face.Normal < normals.Count)
            {
                _normals.Add(normals[face.Normal]);
                _hasNormals = true;
            }
            else
            {
                _normals.Add(new Vector3D(0.0, 1.0, 0.0));
            }

            return index;
        }
    }
}
