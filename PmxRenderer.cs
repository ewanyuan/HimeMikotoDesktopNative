using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace HimeMikotoDesktopNative;

internal sealed class PmxRenderer
{
    private const bool FlipTextureV = true;
    private static readonly bool RotateScene =
        !string.Equals(Environment.GetEnvironmentVariable("HIME_NO_ROTATE"), "1", StringComparison.Ordinal);
    private static readonly string? DebugHiddenMaterial =
        Environment.GetEnvironmentVariable("HIME_HIDE_MATERIAL");
    private static readonly bool SolidBodyHelpers =
        !string.Equals(Environment.GetEnvironmentVariable("HIME_SOLID_BODY_HELPERS"), "0", StringComparison.Ordinal);
    private static readonly string UvProfile =
        Environment.GetEnvironmentVariable("HIME_UV_PROFILE")?.Trim().ToLowerInvariant() ?? string.Empty;
    private readonly PmxModel _model;
    private readonly List<PmxPart> _parts = [];
    private readonly List<PmxMaterialPart> _materialParts = [];
    private readonly Dictionary<string, BitmapSource?> _textureCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapSource?> _sanitizedTextureCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, Dictionary<int, Vector3D>> _vertexMorphCache = [];
    private readonly Dictionary<int, List<PmxWeightedMaterialMorphOffset>> _materialMorphCache = [];
    private readonly Dictionary<int, double> _clothingMorphWeights = [];
    private readonly Dictionary<int, double> _adultMorphWeights = [];
    private readonly Dictionary<int, PmxColor> _baseMaterialColors = [];
    private readonly Dictionary<int, float> _jiggleInfluences = [];
    private readonly string _modelDirectory;
    private readonly int _blinkMorphIndex;
    private readonly Dictionary<int, Vector3D> _blinkOffsets;
    private int _expressionMorphIndex = -1;
    private double _blinkWeight;
    private double _lastBlinkWeight = double.NaN;
    private double _jigglePosition;
    private double _jiggleVelocity;
    private bool _showAdultBodyTexture;

    public PmxRenderer(PmxModel model)
    {
        _model = model;
        _modelDirectory = Path.GetDirectoryName(_model.SourcePath) ?? Directory.GetCurrentDirectory();
        _blinkMorphIndex = model.FindBlinkMorph();
        _blinkOffsets = _blinkMorphIndex >= 0 ? model.BuildVertexMorph(_blinkMorphIndex) : [];
        BuildJiggleInfluences();
        Scene = BuildScene();
    }

    public Model3DGroup Scene { get; }
    public bool HasBlinkMorph => _blinkOffsets.Count > 0;
    public int VisibleMaterialCount => _materialParts.Count(part => part.Geometry.Material != null);
    public int ActiveExpressionMorphIndex => _expressionMorphIndex;
    public int ActiveClothingMorphCount => _clothingMorphWeights.Count;
    public int ActiveAdultMorphCount => _adultMorphWeights.Count;
    internal int JiggleInfluenceCount => _jiggleInfluences.Count;
    internal bool IsJiggleActive => Math.Abs(_jigglePosition) > 0.0001 || Math.Abs(_jiggleVelocity) > 0.0001;
    internal int CurrentDeformedVertexCount
    {
        get
        {
            var count = 0;
            foreach (var part in _parts)
            {
                var positions = part.Mesh.Positions;
                for (var index = 0; index < part.BasePositions.Length; index++)
                {
                    var current = positions[index];
                    var original = part.BasePositions[index];
                    if (Math.Abs(current.X - original.X) > 0.000001
                        || Math.Abs(current.Y - original.Y) > 0.000001
                        || Math.Abs(current.Z - original.Z) > 0.000001)
                    {
                        count++;
                    }
                }
            }

            return count;
        }
    }

    internal bool IsMaterialVisible(int materialIndex)
    {
        return _materialParts
            .Where(part => part.MaterialIndex == materialIndex)
            .Any(part => part.Geometry.Material != null);
    }

    public void SetBlink(double weight)
    {
        weight = Math.Clamp(weight, 0.0, 1.0);
        if (Math.Abs(weight - _lastBlinkWeight) < 0.001)
        {
            return;
        }

        _lastBlinkWeight = weight;
        _blinkWeight = weight;
        RefreshGeometry();
    }

    public void StartDragJiggle(double deltaX, double deltaY)
    {
        var distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (distance < 2.0 || _jiggleInfluences.Count == 0)
        {
            return;
        }

        var impulse = Math.Clamp(distance / 80.0, 0.65, 2.5);
        _jiggleVelocity = Math.Clamp(_jiggleVelocity + 7.5 * impulse, -24.0, 24.0);
    }

    public bool TickAnimation(double elapsedSeconds)
    {
        if (!IsJiggleActive)
        {
            return false;
        }

        var delta = Math.Clamp(elapsedSeconds, 0.001, 0.1);
        const double stiffness = 70.0;
        const double damping = 8.0;
        _jiggleVelocity += (-stiffness * _jigglePosition - damping * _jiggleVelocity) * delta;
        _jigglePosition += _jiggleVelocity * delta;

        if (Math.Abs(_jigglePosition) < 0.0001 && Math.Abs(_jiggleVelocity) < 0.0001)
        {
            _jigglePosition = 0.0;
            _jiggleVelocity = 0.0;
        }

        RefreshGeometry();
        return IsJiggleActive;
    }

    public void SetExpressionMorph(int morphIndex)
    {
        if (morphIndex < 0 || morphIndex >= _model.Morphs.Count || _model.Morphs[morphIndex].Type is not (0 or 1))
        {
            morphIndex = -1;
        }

        if (_expressionMorphIndex == morphIndex)
        {
            return;
        }

        _expressionMorphIndex = morphIndex;
        RefreshGeometry();
        RefreshMaterials();
    }

    public bool SetAdultMorphWeight(int morphIndex, double weight)
    {
        if (morphIndex < 0 || morphIndex >= _model.Morphs.Count || _model.Morphs[morphIndex].Type is not (0 or 1))
        {
            return false;
        }

        weight = Math.Clamp(weight, 0.0, 1.0);
        if (weight < 0.001)
        {
            _adultMorphWeights.Remove(morphIndex);
        }
        else
        {
            _adultMorphWeights[morphIndex] = weight;
        }

        UpdateAdultTextureVisibility();
        RefreshGeometry();
        RefreshMaterials();
        return true;
    }

    public bool SetClothingMorphWeight(int morphIndex, double weight)
    {
        if (morphIndex < 0 || morphIndex >= _model.Morphs.Count || _model.Morphs[morphIndex].Type != 8)
        {
            return false;
        }

        weight = Math.Clamp(weight, 0.0, 1.0);
        if (weight < 0.001)
        {
            _clothingMorphWeights.Remove(morphIndex);
        }
        else
        {
            _clothingMorphWeights[morphIndex] = weight;
        }

        UpdateAdultTextureVisibility();
        RefreshMaterials();
        return true;
    }

    public void ReplaceClothingMorphGroup(IEnumerable<int> morphIndices, int selectedMorphIndex)
    {
        foreach (var morphIndex in morphIndices)
        {
            _clothingMorphWeights.Remove(morphIndex);
        }

        if (selectedMorphIndex >= 0
            && selectedMorphIndex < _model.Morphs.Count
            && _model.Morphs[selectedMorphIndex].Type == 8)
        {
            _clothingMorphWeights[selectedMorphIndex] = 1.0;
        }

        UpdateAdultTextureVisibility();
        RefreshMaterials();
    }

    public void ResetClothingMorphs()
    {
        if (_clothingMorphWeights.Count == 0)
        {
            return;
        }

        _clothingMorphWeights.Clear();
        UpdateAdultTextureVisibility();
        RefreshMaterials();
    }

    public void ResetAdultMorphs(IEnumerable<int> morphIndices)
    {
        var changed = false;
        foreach (var morphIndex in morphIndices)
        {
            if (_adultMorphWeights.Remove(morphIndex))
            {
                changed = true;
            }

            if (_clothingMorphWeights.Remove(morphIndex))
            {
                changed = true;
            }
        }

        if (changed)
        {
            UpdateAdultTextureVisibility();
            RefreshGeometry();
            RefreshMaterials();
        }
    }

    private Model3DGroup BuildScene()
    {
        var scene = new Model3DGroup();
        // PMX models use the opposite front direction from this WPF camera.
        // Rotate the complete scene so the character faces the user.
        if (RotateScene)
        {
            scene.Transform = new RotateTransform3D(
                new AxisAngleRotation3D(new Vector3D(0.0, 1.0, 0.0), 180.0));
        }

        var indexCursor = 0;
        for (var materialIndex = 0; materialIndex < _model.Materials.Count; materialIndex++)
        {
            var material = _model.Materials[materialIndex];
            var indexCount = Math.Max(0, material.IndexCount);
            var indexEnd = Math.Min(indexCursor + indexCount, _model.Indices.Count);
            var localVertexMap = new Dictionary<int, int>();
            var globalVertexIndices = new List<int>();
            var triangleIndices = new List<int>();

            for (var index = indexCursor; index + 2 < indexEnd; index += 3)
            {
                var globalA = _model.Indices[index];
                var globalB = _model.Indices[index + 1];
                var globalC = _model.Indices[index + 2];
                if (!IsValidVertex(globalA) || !IsValidVertex(globalB) || !IsValidVertex(globalC))
                {
                    continue;
                }

                triangleIndices.Add(GetLocalIndex(globalA, localVertexMap, globalVertexIndices));
                triangleIndices.Add(GetLocalIndex(globalB, localVertexMap, globalVertexIndices));
                triangleIndices.Add(GetLocalIndex(globalC, localVertexMap, globalVertexIndices));
            }

            indexCursor = indexEnd;
            if (triangleIndices.Count == 0)
            {
                continue;
            }

            var basePositions = new Point3D[globalVertexIndices.Count];
            var normals = new Vector3D[globalVertexIndices.Count];
            var textureCoordinates = new Point[globalVertexIndices.Count];
            for (var index = 0; index < globalVertexIndices.Count; index++)
            {
                var vertex = _model.Vertices[globalVertexIndices[index]];
                basePositions[index] = vertex.Position;
                normals[index] = vertex.Normal;
                // WPF's 3D brush coordinates use the opposite vertical image
                // origin from PMX/OBJ UVs. Keep the model-authored map intact
                // by converting only that image-origin convention here.
                var textureV = UseDirectTextureV(material)
                    ? vertex.UV.Y
                    : (FlipTextureV ? 1.0 - vertex.UV.Y : vertex.UV.Y);
                textureCoordinates[index] = new Point(vertex.UV.X, textureV);
            }

            var mesh = new MeshGeometry3D
            {
                Positions = new Point3DCollection(basePositions),
                Normals = new Vector3DCollection(normals),
                TextureCoordinates = new PointCollection(textureCoordinates),
                TriangleIndices = new Int32Collection(triangleIndices),
            };
            // Keep geometry for materials that start transparent. PMX material morphs
            // commonly reveal these helper materials when a clothing preset is selected.
            // Dropping them here makes the later alpha change unable to restore the mesh.
            var initialDiffuse = GetInitialMaterialColor(material);
            Material? materialObject = initialDiffuse.A > 0.001f
                ? CreateMaterial(material, initialDiffuse)
                : null;
            var geometry = new GeometryModel3D
            {
                Geometry = mesh,
                Material = materialObject,
                BackMaterial = (material.Flags & 0x01) != 0 ? materialObject : null,
            };
            scene.Children.Add(geometry);
            _parts.Add(new PmxPart(basePositions, globalVertexIndices.ToArray(), mesh));
            _materialParts.Add(new PmxMaterialPart(materialIndex, material, geometry));
            _baseMaterialColors[materialIndex] = initialDiffuse;
        }

        return scene;
    }

    private static bool UseDirectTextureV(PmxMaterial material)
    {
        if (UvProfile is "all-direct" or "direct")
        {
            return true;
        }

        if (UvProfile is "all-flip" or "flip")
        {
            return false;
        }

        var isBody = material.Name.Contains("[BODY]", StringComparison.OrdinalIgnoreCase);
        if (UvProfile is "body-direct")
        {
            return isBody;
        }

        if (UvProfile is "base-direct" or "body-base-direct")
        {
            return material.Name.Contains("[BODY]Body", StringComparison.OrdinalIgnoreCase);
        }

        var isBodyHelper = material.Name.Contains("[BODY]Nipple", StringComparison.OrdinalIgnoreCase)
            || material.Name.Contains("[BODY]HideSocks", StringComparison.OrdinalIgnoreCase)
            || material.Name.Contains("[BODY]HideShoes", StringComparison.OrdinalIgnoreCase)
            || material.Name.Contains("[BODY]HideBra", StringComparison.OrdinalIgnoreCase)
            || material.Name.Contains("[BODY]BustEX", StringComparison.OrdinalIgnoreCase);
        if (UvProfile is "helpers-direct")
        {
            return isBodyHelper;
        }

        if (UvProfile is "helpers-flip")
        {
            return false;
        }

        // The PMX/OBJ exports in this asset use the same authored V direction.
        // Keep that direction for the production renderer; the named profiles
        // above remain available only for offline diagnostics.
        return true;
    }

    private void RefreshGeometry()
    {
        var offsets = new Dictionary<int, Vector3D>();
        AddMorphOffsets(_blinkMorphIndex, _blinkWeight, offsets);
        AddMorphOffsets(_expressionMorphIndex, 1.0, offsets);
        foreach (var (morphIndex, weight) in _adultMorphWeights)
        {
            AddMorphOffsets(morphIndex, weight, offsets);
        }
        AddJiggleOffsets(offsets);

        foreach (var part in _parts)
        {
            var positions = new Point3DCollection(part.BasePositions.Length);
            for (var index = 0; index < part.BasePositions.Length; index++)
            {
                var basePosition = part.BasePositions[index];
                if (offsets.TryGetValue(part.GlobalVertexIndices[index], out var offset))
                {
                    positions.Add(new Point3D(
                        basePosition.X + offset.X,
                        basePosition.Y + offset.Y,
                        basePosition.Z + offset.Z));
                }
                else
                {
                    positions.Add(basePosition);
                }
            }

            part.Mesh.Positions = positions;
        }
    }

    private void AddMorphOffsets(int morphIndex, double weight, Dictionary<int, Vector3D> destination)
    {
        if (morphIndex < 0 || Math.Abs(weight) < 0.000001)
        {
            return;
        }

        var offsets = GetVertexMorph(morphIndex);
        foreach (var (vertexIndex, offset) in offsets)
        {
            var scaled = new Vector3D(offset.X * weight, offset.Y * weight, offset.Z * weight);
            if (destination.TryGetValue(vertexIndex, out var previous))
            {
                destination[vertexIndex] = previous + scaled;
            }
            else
            {
                destination[vertexIndex] = scaled;
            }
        }
    }

    private Dictionary<int, Vector3D> GetVertexMorph(int morphIndex)
    {
        if (_vertexMorphCache.TryGetValue(morphIndex, out var offsets))
        {
            return offsets;
        }

        offsets = _model.BuildVertexMorph(morphIndex);
        _vertexMorphCache[morphIndex] = offsets;
        return offsets;
    }

    private void RefreshMaterials()
    {
        var materialMorphs = new List<PmxWeightedMaterialMorphOffset>();
        AddMaterialMorphs(_expressionMorphIndex, 1.0, materialMorphs);
        foreach (var (morphIndex, weight) in _clothingMorphWeights)
        {
            AddMaterialMorphs(morphIndex, weight, materialMorphs);
        }

        foreach (var (morphIndex, weight) in _adultMorphWeights)
        {
            AddMaterialMorphs(morphIndex, weight, materialMorphs);
        }

        foreach (var part in _materialParts)
        {
            // This is a flat adult helper layer in the source model. A proper
            // MMD runtime uses its material/alpha rules; this WPF fallback
            // cannot reproduce that clipping layer, so never draw the helper
            // mesh as a solid rectangle or let it contaminate the body map.
            if (IsAdultDetailMaterial(part.Source) || IsDebugHiddenMaterial(part.Source))
            {
                part.Geometry.Material = null;
                part.Geometry.BackMaterial = null;
                continue;
            }

            var diffuse = _baseMaterialColors[part.MaterialIndex];
            if (IsAdultDetailMaterial(part.Source)
                && _showAdultBodyTexture
                && diffuse.A <= 0.001f)
            {
                diffuse = new PmxColor(diffuse.R, diffuse.G, diffuse.B, part.Source.Diffuse.A);
            }

            foreach (var weightedMorph in materialMorphs)
            {
                var offset = weightedMorph.Offset;
                if (offset.MaterialIndex != -1 && offset.MaterialIndex != part.MaterialIndex)
                {
                    continue;
                }

                diffuse = ApplyMaterialMorph(diffuse, offset, weightedMorph.Weight);
            }

            if (diffuse.A <= 0.001f)
            {
                part.Geometry.Material = null;
                part.Geometry.BackMaterial = null;
                continue;
            }

            var materialObject = CreateMaterial(part.Source, diffuse);
            part.Geometry.Material = materialObject;
            part.Geometry.BackMaterial = (part.Source.Flags & 0x01) != 0 ? materialObject : null;
        }
    }

    private void AddMaterialMorphs(int morphIndex, double weight, List<PmxWeightedMaterialMorphOffset> destination)
    {
        if (morphIndex < 0 || Math.Abs(weight) < 0.000001)
        {
            return;
        }

        if (!_materialMorphCache.TryGetValue(morphIndex, out var morphs))
        {
            morphs = _model.BuildMaterialMorph(morphIndex);
            _materialMorphCache[morphIndex] = morphs;
        }

        foreach (var morph in morphs)
        {
            destination.Add(new PmxWeightedMaterialMorphOffset(morph.Offset, morph.Weight * weight));
        }
    }

    private static PmxColor ApplyMaterialMorph(
        PmxColor baseColor,
        PmxMaterialMorphOffset offset,
        double weight)
    {
        return new PmxColor(
            ApplyMaterialChannel(baseColor.R, offset.Diffuse.R, offset.Operation, weight),
            ApplyMaterialChannel(baseColor.G, offset.Diffuse.G, offset.Operation, weight),
            ApplyMaterialChannel(baseColor.B, offset.Diffuse.B, offset.Operation, weight),
            ApplyMaterialChannel(baseColor.A, offset.Diffuse.A, offset.Operation, weight));
    }

    private static float ApplyMaterialChannel(float baseValue, float morphValue, byte operation, double weight)
    {
        var result = operation == 0
            ? baseValue * (1.0 + (morphValue - 1.0) * weight)
            : baseValue + morphValue * weight;
        return (float)result;
    }

    private Material CreateMaterial(PmxMaterial source, PmxColor diffuse)
    {
        var alpha = Math.Clamp(diffuse.A, 0.0f, 1.0f);
        Brush brush;
        if (SolidBodyHelpers && IsLowerBodyHelperMaterial(source))
        {
            // The helper meshes cover the clothing seam, but their atlas UVs
            // point at unrelated hand/foot artwork. Use a close skin tone so
            // the seam disappears without leaking another body part.
            brush = CreateColorBrush(new PmxColor(1.0f, 0.83f, 0.78f, diffuse.A), alpha);
        }
        else
        {
            brush = (Brush?)CreateTextureBrush(source.TexturePath, _modelDirectory, alpha)
                ?? CreateColorBrush(diffuse, alpha);
        }
        var material = new DiffuseMaterial(brush);
        material.Freeze();
        return material;
    }

    private static bool IsLowerBodyHelperMaterial(PmxMaterial material)
    {
        return material.Name.Contains("[BODY]HideSocks", StringComparison.OrdinalIgnoreCase)
            || material.Name.Contains("[BODY]HideShoes", StringComparison.OrdinalIgnoreCase);
    }

    private static PmxColor GetInitialMaterialColor(PmxMaterial material)
    {
        // Hime's adult helper mesh is opaque in the PMX base state and samples
        // the red adult detail area from Body.png. Keep it available for an
        // explicit material morph, but do not show it in the normal pet pose.
        if (material.Name.Contains("[BODY]Adult", StringComparison.OrdinalIgnoreCase))
        {
            return new PmxColor(material.Diffuse.R, material.Diffuse.G, material.Diffuse.B, 0.0f);
        }

        return material.Diffuse;
    }

    private void BuildJiggleInfluences()
    {
        for (var vertexIndex = 0; vertexIndex < _model.Vertices.Count; vertexIndex++)
        {
            var position = _model.Vertices[vertexIndex].Position;
            var absX = Math.Abs(position.X);
            if (absX < 0.25 || absX > 2.45 || position.Y < 13.15 || position.Y > 16.45 || position.Z > 0.35)
            {
                continue;
            }

            var sideWeight = Math.Clamp(1.0 - Math.Abs(absX - 1.05) / 1.25, 0.0, 1.0);
            var heightWeight = Math.Clamp(1.0 - Math.Abs(position.Y - 14.75) / 1.85, 0.0, 1.0);
            var frontWeight = Math.Clamp((-position.Z + 0.05) / 1.15, 0.0, 1.0);
            var influence = (float)(sideWeight * heightWeight * frontWeight);
            if (influence > 0.015f)
            {
                _jiggleInfluences[vertexIndex] = influence;
            }
        }
    }

    private void AddJiggleOffsets(Dictionary<int, Vector3D> destination)
    {
        if (Math.Abs(_jigglePosition) < 0.000001 || _jiggleInfluences.Count == 0)
        {
            return;
        }

        foreach (var (vertexIndex, influence) in _jiggleInfluences)
        {
            var position = _model.Vertices[vertexIndex].Position;
            var side = position.X >= 0.0 ? 1.0 : -1.0;
            var amount = _jigglePosition * influence;
            var offset = new Vector3D(
                side * amount * 0.06,
                amount * 0.18,
                -amount * 0.11);
            if (destination.TryGetValue(vertexIndex, out var previous))
            {
                destination[vertexIndex] = previous + offset;
            }
            else
            {
                destination[vertexIndex] = offset;
            }
        }
    }

    private ImageBrush? CreateTextureBrush(string texturePath, string modelDirectory, float opacity)
    {
        if (string.IsNullOrWhiteSpace(texturePath))
        {
            return null;
        }

        var resolved = ResolveTexture(texturePath, modelDirectory);
        if (resolved == null)
        {
            return null;
        }

        var isBodyTexture = string.Equals(
            Path.GetFileName(resolved),
            "[BODY]Body.png",
            StringComparison.OrdinalIgnoreCase);
        var cache = isBodyTexture ? _sanitizedTextureCache : _textureCache;
        if (!cache.TryGetValue(resolved, out var cached))
        {
            cached = LoadTexture(resolved);
            if (cached != null && isBodyTexture)
            {
                cached = MaskUnsupportedAdultPatch(cached);
            }

            cache[resolved] = cached;
        }

        return cached == null ? null : CreateImageBrush(cached, opacity);
    }

    private static BitmapSource? LoadTexture(string resolved)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(resolved, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource MaskUnsupportedAdultPatch(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0.0);
        converted.Freeze();
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var left = width * 0.44;
        var right = width * 0.56;
        var top = height * 0.50;
        var bottom = height * 0.69;
        var feather = Math.Max(8.0, width * 0.018);
        var sampleOffset = Math.Max(1, (int)Math.Round(width * 0.105));

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var red = pixels[y * stride + x * 4 + 2];
                var green = pixels[y * stride + x * 4 + 1];
                var blue = pixels[y * stride + x * 4];
                var isAdultPatchPixel = red > 150
                    && red > green * 1.35
                    && red > blue * 1.35;
                if (!isAdultPatchPixel)
                {
                    continue;
                }

                var distanceToEdge = Math.Min(
                    Math.Min(x - left, right - x),
                    Math.Min(y - top, bottom - y));
                var mask = Math.Clamp(distanceToEdge / feather, 0.0, 1.0);
                if (mask <= 0.0)
                {
                    continue;
                }

                var leftX = Math.Clamp(x - sampleOffset, 0, width - 1);
                var rightX = Math.Clamp(x + sampleOffset, 0, width - 1);
                var leftOffset = y * stride + leftX * 4;
                var rightOffset = y * stride + rightX * 4;
                var offset = y * stride + x * 4;
                for (var channel = 0; channel < 4; channel++)
                {
                    var target = (byte)Math.Clamp(
                        (pixels[leftOffset + channel] + pixels[rightOffset + channel]) * 0.5,
                        0.0,
                        255.0);
                    pixels[offset + channel] = (byte)Math.Clamp(
                        pixels[offset + channel] * (1.0 - mask) + target * mask,
                        0.0,
                        255.0);
                }
            }
        }

        var sanitized = BitmapSource.Create(
            width,
            height,
            converted.DpiX,
            converted.DpiY,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        sanitized.Freeze();
        return sanitized;
    }

    private void UpdateAdultTextureVisibility()
    {
        _showAdultBodyTexture = _adultMorphWeights.Keys.Any(IsAdultDetailMorph)
            || _clothingMorphWeights.Keys.Any(IsAdultDetailMorph);
    }

    private bool IsAdultDetailMorph(int morphIndex)
    {
        if (morphIndex < 0 || morphIndex >= _model.Morphs.Count)
        {
            return false;
        }

        var name = _model.Morphs[morphIndex].Name;
        return name.Contains("全裸", StringComparison.OrdinalIgnoreCase)
            || name.Contains("腹", StringComparison.OrdinalIgnoreCase)
            || name.Contains("股", StringComparison.OrdinalIgnoreCase)
            || name.Contains("穴", StringComparison.OrdinalIgnoreCase)
            || name.Contains("異物", StringComparison.OrdinalIgnoreCase)
            || name.Contains("本番", StringComparison.OrdinalIgnoreCase)
            || name.Contains("玩具", StringComparison.OrdinalIgnoreCase)
            || name.Contains("T-バック", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Tバック", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAdultDetailMaterial(PmxMaterial material)
    {
        return material.Name.Contains("[BODY]Adult", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDebugHiddenMaterial(PmxMaterial material)
    {
        return !string.IsNullOrWhiteSpace(DebugHiddenMaterial)
            && material.Name.Contains(DebugHiddenMaterial, StringComparison.OrdinalIgnoreCase);
    }

    private static ImageBrush CreateImageBrush(BitmapSource bitmap, float opacity)
    {
        var brush = new ImageBrush(bitmap)
        {
            Stretch = Stretch.Fill,
            Opacity = opacity,
        };
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush CreateColorBrush(PmxColor color, float alpha)
    {
        var brush = new SolidColorBrush(Color.FromScRgb(
            alpha,
            Math.Clamp(color.R, 0.0f, 1.0f),
            Math.Clamp(color.G, 0.0f, 1.0f),
            Math.Clamp(color.B, 0.0f, 1.0f)));
        brush.Freeze();
        return brush;
    }

    private static string? ResolveTexture(string texturePath, string modelDirectory)
    {
        var normalized = texturePath.Trim().Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var candidate = Path.IsPathRooted(normalized)
            ? normalized
            : Path.Combine(modelDirectory, normalized.TrimStart('.', Path.DirectorySeparatorChar));
        candidate = Path.GetFullPath(candidate);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        var fileName = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(fileName) || !Directory.Exists(modelDirectory))
        {
            return null;
        }

        try
        {
            return Directory.EnumerateFiles(modelDirectory, fileName, SearchOption.AllDirectories).FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private bool IsValidVertex(int index)
    {
        return index >= 0 && index < _model.Vertices.Count;
    }

    private static int GetLocalIndex(int globalIndex, Dictionary<int, int> localVertexMap, List<int> globalVertexIndices)
    {
        if (localVertexMap.TryGetValue(globalIndex, out var localIndex))
        {
            return localIndex;
        }

        localIndex = globalVertexIndices.Count;
        localVertexMap[globalIndex] = localIndex;
        globalVertexIndices.Add(globalIndex);
        return localIndex;
    }

    private sealed class PmxPart
    {
        public PmxPart(Point3D[] basePositions, int[] globalVertexIndices, MeshGeometry3D mesh)
        {
            BasePositions = basePositions;
            GlobalVertexIndices = globalVertexIndices;
            Mesh = mesh;
        }

        public Point3D[] BasePositions { get; }
        public int[] GlobalVertexIndices { get; }
        public MeshGeometry3D Mesh { get; }
    }

    private sealed class PmxMaterialPart
    {
        public PmxMaterialPart(int materialIndex, PmxMaterial source, GeometryModel3D geometry)
        {
            MaterialIndex = materialIndex;
            Source = source;
            Geometry = geometry;
        }

        public int MaterialIndex { get; }
        public PmxMaterial Source { get; }
        public GeometryModel3D Geometry { get; }
    }
}
