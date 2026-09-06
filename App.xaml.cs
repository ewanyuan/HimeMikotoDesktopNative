using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HimeMikotoDesktopNative;

public partial class App : System.Windows.Application
{
    private async void OnStartup(object sender, StartupEventArgs e)
    {
        if (e.Args.Any(argument => argument.Equals("--mmd-runtime-self-test", StringComparison.OrdinalIgnoreCase)))
        {
            var passed = await RunMmdRuntimeSelfTestAsync();
            Environment.ExitCode = passed ? 0 : 1;
            Shutdown();
            return;
        }

        if (e.Args.Any(argument => argument.Equals("--mmd-expression-audit", StringComparison.OrdinalIgnoreCase)))
        {
            var passed = await RunExpressionAuditAsync();
            Environment.ExitCode = passed ? 0 : 1;
            Shutdown();
            return;
        }

        var window = new MainWindow(autoLoad: true);
        MainWindow = window;
        window.Show();
    }

    private static async Task<bool> RunExpressionAuditAsync()
    {
        var outputDirectory = Directory.GetCurrentDirectory();
        var errorPath = Path.Combine(outputDirectory, "mmd-expression-audit-error.txt");
        var reportPath = Path.Combine(outputDirectory, "mmd-expression-audit.json");
        if (File.Exists(errorPath))
        {
            File.Delete(errorPath);
        }

        var window = new MainWindow(autoLoad: false)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000.0,
            Top = -10000.0,
            AllowsTransparency = false,
            ShowActivated = false,
            ShowInTaskbar = false,
        };

        try
        {
            window.Show();
            await window.InitializeRuntimeAsync();
            window.UseOpaqueCaptureBackgroundForTest();
            await window.SetSkinToneAsync("brighter");
            var hime = await window.LoadCharacterAsync(0);
            await window.ResetMorphsAsync();
            await Task.Delay(150);

            var baselinePath = Path.Combine(outputDirectory, "mmd-expression-audit-baseline.png");
            await window.CapturePreviewAsync(baselinePath);

            var results = new List<object>();
            foreach (var morph in hime.GetProperty("morphs").EnumerateArray())
            {
                var category = morph.GetProperty("category").GetInt32();
                var name = morph.GetProperty("name").GetString() ?? string.Empty;
                var englishName = morph.TryGetProperty("englishName", out var englishNameElement)
                    ? englishNameElement.GetString() ?? string.Empty
                    : string.Empty;
                if (category is not (1 or 2 or 3)
                    || name.StartsWith("├", StringComparison.Ordinal)
                    || name.StartsWith("└", StringComparison.Ordinal)
                    || name.Contains("__", StringComparison.Ordinal)
                    || englishName.Contains("__", StringComparison.Ordinal))
                {
                    continue;
                }

                var index = morph.GetProperty("index").GetInt32();
                await window.ResetMorphsAsync();
                await window.SetMorphAsync(index, 1.0);
                await Task.Delay(120);
                var previewPath = Path.Combine(outputDirectory, $"mmd-expression-audit-{index}.png");
                await window.CapturePreviewAsync(previewPath);
                var difference = ComparePngs(baselinePath, previewPath);
                results.Add(new
                {
                    index,
                    name,
                    englishName,
                    category,
                    difference.DifferentPixels,
                    difference.TotalPixels,
                    difference.DifferentPixelRatio,
                    difference.MeanAbsoluteChannelDifference,
                });
            }

            File.WriteAllText(
                reportPath,
                JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception exception)
        {
            File.WriteAllText(errorPath, exception.ToString());
            return false;
        }
        finally
        {
            window.Close();
        }
    }

    private static (
        int DifferentPixels,
        int TotalPixels,
        double DifferentPixelRatio,
        double MeanAbsoluteChannelDifference) ComparePngs(string firstPath, string secondPath)
    {
        var first = LoadBitmap(firstPath);
        var second = LoadBitmap(secondPath);
        var width = Math.Min(first.PixelWidth, second.PixelWidth);
        var height = Math.Min(first.PixelHeight, second.PixelHeight);
        var stride = width * 4;
        var firstPixels = new byte[stride * height];
        var secondPixels = new byte[stride * height];
        first.CopyPixels(new Int32Rect(0, 0, width, height), firstPixels, stride, 0);
        second.CopyPixels(new Int32Rect(0, 0, width, height), secondPixels, stride, 0);

        var totalPixels = width * height;
        var differentPixels = 0;
        long totalChannelDifference = 0;
        for (var offset = 0; offset < firstPixels.Length; offset += 4)
        {
            var maxDifference = 0;
            for (var channel = 0; channel < 3; channel++)
            {
                var difference = Math.Abs(firstPixels[offset + channel] - secondPixels[offset + channel]);
                maxDifference = Math.Max(maxDifference, difference);
                totalChannelDifference += difference;
            }

            if (maxDifference >= 3)
            {
                differentPixels++;
            }
        }

        return (
            differentPixels,
            totalPixels,
            totalPixels == 0 ? 0.0 : (double)differentPixels / totalPixels,
            firstPixels.Length == 0 ? 0.0 : (double)totalChannelDifference / (totalPixels * 3));
    }

    private static BitmapSource LoadBitmap(string path)
    {
        using var stream = File.OpenRead(path);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private static async Task<bool> RunMmdRuntimeSelfTestAsync()
    {
        var outputDirectory = Directory.GetCurrentDirectory();
        var errorPath = Path.Combine(outputDirectory, "mmd-runtime-self-test-error.txt");
        var tracePath = Path.Combine(outputDirectory, "mmd-runtime-self-test-trace.txt");
        if (File.Exists(errorPath))
        {
            File.Delete(errorPath);
        }
        if (File.Exists(tracePath))
        {
            File.Delete(tracePath);
        }

        var window = new MainWindow(autoLoad: false)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000.0,
            Top = -10000.0,
            AllowsTransparency = false,
            ShowActivated = false,
            ShowInTaskbar = false,
        };

        try
        {
            File.AppendAllText(tracePath, "before-show" + Environment.NewLine);
            window.Show();
            File.AppendAllText(tracePath, "after-show" + Environment.NewLine);
            await window.InitializeRuntimeAsync();
            File.AppendAllText(tracePath, "after-initialize" + Environment.NewLine);
            var slightlyDarkerTone = await window.SetSkinToneAsync("lighter");
            if (!string.Equals(slightlyDarkerTone.GetProperty("tone").GetString(), "lighter", StringComparison.Ordinal))
            {
                throw new InvalidDataException("The native runtime did not apply the slightly darker skin tone.");
            }
            var defaultTone = await window.SetSkinToneAsync("brighter");
            if (!string.Equals(defaultTone.GetProperty("tone").GetString(), "brighter", StringComparison.Ordinal))
            {
                throw new InvalidDataException("The default skin tone was not set to the brighter preset.");
            }
            window.UseOpaqueCaptureBackgroundForTest();

            var hime = await window.LoadCharacterAsync(0);
            File.AppendAllText(tracePath, "after-hime" + Environment.NewLine);
            ValidateModel(hime, expectedIndex: 0);
            File.WriteAllText(
                Path.Combine(outputDirectory, "mmd-runtime-hime-morphs-debug.json"),
                hime.GetProperty("morphs").GetRawText());
            foreach (var tone in new[] { "brighter", "lighter", "deep" })
            {
                await window.SetSkinToneAsync(tone);
                await Task.Delay(100);
                await window.CapturePreviewAsync(
                    Path.Combine(outputDirectory, $"mmd-runtime-hime-tone-{tone}.png"));
            }
            await window.SetSkinToneAsync("brighter");

            var himeNakedIndex = FindMorphIndex(hime, "全裸");
            if (himeNakedIndex < 0)
            {
                throw new InvalidDataException("The Hime model did not expose the 全裸 morph for persistence testing.");
            }

            var eyeExpressionIndex = -1;
            var mouthExpressionIndex = -1;
            foreach (var morph in hime.GetProperty("morphs").EnumerateArray())
            {
                var candidateIndex = morph.GetProperty("index").GetInt32();
                if (candidateIndex == himeNakedIndex)
                {
                    continue;
                }

                if (eyeExpressionIndex < 0 && window.IsCuratedEyeMorphForTest(candidateIndex))
                {
                    eyeExpressionIndex = candidateIndex;
                }

                if (mouthExpressionIndex < 0 && window.IsCuratedMouthMorphForTest(candidateIndex))
                {
                    mouthExpressionIndex = candidateIndex;
                }

                if (eyeExpressionIndex >= 0 && mouthExpressionIndex >= 0)
                {
                    break;
                }
            }

            if (eyeExpressionIndex < 0 || mouthExpressionIndex < 0)
            {
                throw new InvalidDataException("The Hime model did not expose both eye and mouth expression morphs.");
            }

            await window.SelectMorphForTestAsync(himeNakedIndex);
            await window.SelectMorphForTestAsync(eyeExpressionIndex);
            await window.SelectMorphForTestAsync(mouthExpressionIndex);
            if (window.ActiveEyeExpressionCountForTest != 1
                || window.ActiveEyeExpressionIndexForTest != eyeExpressionIndex
                || window.ActiveMouthExpressionCountForTest != 1
                || window.ActiveMouthExpressionIndexForTest != mouthExpressionIndex)
            {
                throw new InvalidDataException("Eye and mouth expression submenus did not allow one active choice each.");
            }

            var secondEyeExpressionIndex = hime.GetProperty("morphs")
                .EnumerateArray()
                .Select(morph => morph.GetProperty("index").GetInt32())
                .FirstOrDefault(candidateIndex =>
                    candidateIndex != himeNakedIndex
                    && candidateIndex != eyeExpressionIndex
                    && window.IsCuratedEyeMorphForTest(candidateIndex));
            if (secondEyeExpressionIndex <= 0)
            {
                throw new InvalidDataException("The Hime model did not expose a second curated eye expression morph.");
            }

            await window.SelectMorphForTestAsync(secondEyeExpressionIndex);
            if (window.ActiveEyeExpressionCountForTest != 1
                || window.ActiveEyeExpressionIndexForTest != secondEyeExpressionIndex
                || window.ActiveMouthExpressionIndexForTest != mouthExpressionIndex)
            {
                throw new InvalidDataException("Eye expression radio choices did not replace only the previous eye expression.");
            }

            await window.SelectMorphForTestAsync(eyeExpressionIndex);
            if (window.ActiveEyeExpressionCountForTest != 1
                || window.ActiveEyeExpressionIndexForTest != eyeExpressionIndex
                || window.ActiveMouthExpressionIndexForTest != mouthExpressionIndex)
            {
                throw new InvalidDataException("Eye expression radio choices did not switch back while keeping the mouth expression.");
            }

            if (!window.FullNudeActiveForTest)
            {
                throw new InvalidDataException("The full-nude state was lost after applying an expression morph.");
            }

            await window.LoadCharacterAsync(1);
            if (!window.FullNudeActiveForTest)
            {
                throw new InvalidDataException("The full-nude state was lost while switching characters.");
            }

            await window.ClearMorphStateForTestAsync();
            await window.LoadCharacterAsync(0);

            var testMorph = hime.GetProperty("morphs")
                .EnumerateArray()
                .FirstOrDefault(morph => morph.GetProperty("type").GetInt32() is 1 or 8);
            if (testMorph.ValueKind == System.Text.Json.JsonValueKind.Undefined)
            {
                testMorph = hime.GetProperty("morphs").EnumerateArray().First();
            }

            await window.SetMorphAsync(testMorph.GetProperty("index").GetInt32(), 1.0);
            await window.ResetMorphsAsync();
            File.AppendAllText(tracePath, "after-morph" + Environment.NewLine);
            await Task.Delay(250);
            await window.CapturePreviewAsync(Path.Combine(outputDirectory, "mmd-runtime-hime.png"));
            File.AppendAllText(tracePath, "after-hime-capture" + Environment.NewLine);

            var mikoto = await window.LoadCharacterAsync(1);
            File.AppendAllText(tracePath, "after-mikoto" + Environment.NewLine);
            ValidateModel(mikoto, expectedIndex: 1);
            await Task.Delay(250);
            await window.CapturePreviewAsync(Path.Combine(outputDirectory, "mmd-runtime-mikoto.png"));
            File.AppendAllText(tracePath, "after-mikoto-capture" + Environment.NewLine);

            var nakedMorph = mikoto.GetProperty("morphs")
                .EnumerateArray()
                .FirstOrDefault(morph =>
                    (morph.GetProperty("name").GetString() ?? string.Empty).Contains("全裸", StringComparison.Ordinal));
            if (nakedMorph.ValueKind != System.Text.Json.JsonValueKind.Undefined)
            {
                await window.SetMorphAsync(nakedMorph.GetProperty("index").GetInt32(), 1.0);
                await Task.Delay(250);
                await window.CapturePreviewAsync(Path.Combine(outputDirectory, "mmd-runtime-mikoto-naked-tone.png"));
                await window.ResetMorphsAsync();
                File.AppendAllText(tracePath, "after-mikoto-naked-tone-capture" + Environment.NewLine);
            }

            await window.LoadCharacterAsync(0);
            foreach (var dance in window.DanceMotions)
            {
                var motionPath = window.GetDanceMotionPath(dance);
                if (!File.Exists(motionPath))
                {
                    File.AppendAllText(tracePath, $"skip-motion:{dance.Key}" + Environment.NewLine);
                    continue;
                }

                JsonElement motion;
                if (dance.IsDual)
                {
                    var secondaryPath = window.GetSecondaryDanceMotionPath(dance)
                        ?? throw new InvalidDataException("The dual dance has no secondary VMD path.");
                    if (!File.Exists(secondaryPath))
                    {
                        File.AppendAllText(tracePath, $"skip-motion:{dance.Key}:secondary" + Environment.NewLine);
                        continue;
                    }

                    var dualModels = await window.LoadDualCharactersAsync(0, 1);
                    ValidateModel(dualModels.GetProperty("primary"), expectedIndex: 0);
                    ValidateModel(dualModels.GetProperty("secondary"), expectedIndex: 1);

                    var dualNakedMorph = FindMorphIndex(dualModels.GetProperty("primary"), "全裸");
                    if (dualNakedMorph < 0)
                    {
                        throw new InvalidDataException("The dual primary model did not expose the 全裸 morph.");
                    }

                    var dualNakedResult = await window.SetMorphAsync(dualNakedMorph, 1.0);
                    if (!dualNakedResult.TryGetProperty("appliedModelCount", out var appliedModelCount)
                        || appliedModelCount.GetInt32() < 2)
                    {
                        throw new InvalidDataException(
                            $"The dual 全裸 morph did not reach both models: {dualNakedResult}");
                    }

                    await Task.Delay(250);
                    var dualNakedPreviewPath = Path.Combine(
                        outputDirectory,
                        "mmd-runtime-womanizer-dual-naked.png");
                    await window.CapturePreviewAsync(dualNakedPreviewPath);
                    ValidatePreview(dualNakedPreviewPath, "womanizer-dual-naked");
                    await window.ResetMorphsAsync();
                    File.AppendAllText(tracePath, "after-dual-naked" + Environment.NewLine);

                    motion = await window.LoadDualMotionAsync(
                        dance.Key,
                        motionPath,
                        secondaryPath,
                        dance.StartFrame);
                    if (motion.GetProperty("primaryBoneTracks").GetInt32() <= 0
                        || motion.GetProperty("secondaryBoneTracks").GetInt32() <= 0)
                    {
                        throw new InvalidDataException($"The dual VMD contained no bone tracks: {dance.Key}.");
                    }
                    if (motion.GetProperty("startFrame").GetInt32() != dance.StartFrame)
                    {
                        throw new InvalidDataException($"The dual VMD did not start at the requested frame: {dance.Key}.");
                    }
                }
                else
                {
                    await window.LoadCharacterAsync(0);
                    motion = await window.LoadMotionAsync(dance.Key, motionPath, dance.Loop);
                    if (motion.GetProperty("boneTracks").GetInt32() <= 0)
                    {
                        throw new InvalidDataException($"The VMD contained no bone tracks: {dance.Key}.");
                    }
                }

                var music = await window.LoadDanceMusicAsync(dance);
                var musicEnabled = music.GetProperty("enabled").GetBoolean();
                if (window.GetDanceMusicPath(dance) is null && musicEnabled)
                {
                    throw new InvalidDataException($"Music was unexpectedly enabled: {dance.Key}.");
                }
                if (window.GetDanceMusicPath(dance) is not null && !musicEnabled)
                {
                    throw new InvalidDataException($"The available music did not load: {dance.Key}.");
                }

                File.AppendAllText(tracePath, $"after-motion:{dance.Key}" + Environment.NewLine);

                Task<JsonElement>? loopTask = null;
                if (dance.Loop)
                {
                    if (!motion.TryGetProperty("loop", out var loopProperty)
                        || !loopProperty.GetBoolean())
                    {
                        throw new InvalidDataException($"The motion was not marked for looping: {dance.Key}.");
                    }

                    loopTask = window.WaitForRuntimeMessageAsync("motion-looped");
                }

                await window.PlayAsync();
                await Task.Delay(700);
                var previewPath = Path.Combine(outputDirectory, $"mmd-runtime-dance-{dance.Key}.png");
                await window.CapturePreviewAsync(previewPath);
                ValidatePreview(previewPath, dance.Key);
                if (loopTask is not null)
                {
                    var loopTimeout = Math.Max(
                        5.0,
                        motion.GetProperty("endFrame").GetDouble() / 30.0 + 3.0);
                    await loopTask.WaitAsync(TimeSpan.FromSeconds(loopTimeout));
                    File.AppendAllText(tracePath, $"after-loop:{dance.Key}" + Environment.NewLine);
                }
                await window.PauseAsync();
                await window.ResetMorphsAsync();
            }

            var rustVeins = window.DanceMotions.First(dance => dance.Key == "rust-veins");
            await window.LoadCharacterAsync(1);
            var rustMotionMikoto = await window.LoadMotionAsync(
                rustVeins.Key,
                window.GetDanceMotionPath(rustVeins));
            if (rustMotionMikoto.GetProperty("boneTracks").GetInt32() <= 0)
            {
                throw new InvalidDataException("The Rust Veins VMD did not load on Mikoto.");
            }

            await window.PlayAsync();
            await Task.Delay(700);
            var rustMikotoPreviewPath = Path.Combine(
                outputDirectory,
                "mmd-runtime-dance-rust-veins-mikoto.png");
            await window.CapturePreviewAsync(rustMikotoPreviewPath);
            ValidatePreview(rustMikotoPreviewPath, "rust-veins-mikoto");
            await window.PauseAsync();
            await window.ResetMorphsAsync();

            var snowHalation = window.DanceMotions.First(dance => dance.Key == "snow-halation");
            var snowMotion = await window.LoadMotionAsync(
                snowHalation.Key,
                window.GetDanceMotionPath(snowHalation));
            if (snowMotion.GetProperty("boneTracks").GetInt32() <= 0)
            {
                throw new InvalidDataException("The Snow Halation VMD contained no bone tracks.");
            }

            await window.PlayAsync();
            await Task.Delay(700);
            await window.CapturePreviewAsync(Path.Combine(outputDirectory, "mmd-runtime-hime-motion.png"));
            File.AppendAllText(tracePath, "after-motion-capture" + Environment.NewLine);
            window.UseTransparentCaptureBackgroundForTest();
            await Task.Delay(150);
            await window.CapturePreviewAsync(Path.Combine(outputDirectory, "mmd-runtime-hime-transparent.png"));
            File.AppendAllText(tracePath, "after-transparent-capture" + Environment.NewLine);
            return true;
        }
        catch (Exception exception)
        {
            File.WriteAllText(errorPath, exception.ToString());
            File.AppendAllText(tracePath, "failure" + Environment.NewLine);
            return false;
        }
        finally
        {
            File.AppendAllText(tracePath, "before-close" + Environment.NewLine);
            window.Close();
            File.AppendAllText(tracePath, "after-close" + Environment.NewLine);
        }
    }

    private static void ValidateModel(System.Text.Json.JsonElement message, int expectedIndex)
    {
        if (message.GetProperty("index").GetInt32() != expectedIndex)
        {
            throw new InvalidDataException("The runtime loaded the wrong character index.");
        }

        if (message.GetProperty("meshCount").GetInt32() <= 0
            || message.GetProperty("materialCount").GetInt32() <= 0
            || message.GetProperty("boneCount").GetInt32() <= 0
            || message.GetProperty("morphs").GetArrayLength() <= 0)
        {
            throw new InvalidDataException("The PMX did not expose complete model metadata.");
        }

        if (!message.GetProperty("physics").GetBoolean()
            || message.GetProperty("rigidBodyCount").GetInt32() <= 0
            || message.GetProperty("jointCount").GetInt32() <= 0)
        {
            throw new InvalidDataException("The PMX did not enable its native rigid-body physics data.");
        }
    }

    private static int FindMorphIndex(JsonElement model, string name)
    {
        foreach (var morph in model.GetProperty("morphs").EnumerateArray())
        {
            var morphName = morph.GetProperty("name").GetString() ?? string.Empty;
            if (string.Equals(
                    morphName,
                    name,
                    StringComparison.Ordinal)
                || morphName.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                return morph.GetProperty("index").GetInt32();
            }
        }

        return -1;
    }

    private static void ValidatePreview(string path, string label)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The dance preview was not written.", path);
        }

        using var stream = File.OpenRead(path);
        var decoder = new PngBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
        {
            throw new InvalidDataException($"The dance preview contained no image frame: {label}.");
        }

        var image = new FormatConvertedBitmap(
            decoder.Frames[0],
            PixelFormats.Bgra32,
            null,
            0);
        var stride = image.PixelWidth * 4;
        var pixels = new byte[stride * image.PixelHeight];
        image.CopyPixels(pixels, stride, 0);

        var foregroundPixels = 0;
        var minX = image.PixelWidth;
        var maxX = -1;
        var minY = image.PixelHeight;
        var maxY = -1;
        for (var y = 0; y < image.PixelHeight; y++)
        {
            for (var x = 0; x < image.PixelWidth; x++)
            {
                var index = y * stride + x * 4;
                var alpha = pixels[index + 3];
                if (alpha <= 16
                    || (pixels[index] >= 245
                        && pixels[index + 1] >= 245
                        && pixels[index + 2] >= 245))
                {
                    continue;
                }

                foregroundPixels++;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }

        var minimumForegroundPixels = Math.Max(200, image.PixelWidth * image.PixelHeight / 100);
        if (foregroundPixels < minimumForegroundPixels)
        {
            throw new InvalidDataException(
                $"The dance preview was effectively blank: {label} ({foregroundPixels} foreground pixels).");
        }

        var minimumEdgeMargin = label.StartsWith("womanizer-dual", StringComparison.Ordinal)
            ? 48
            : 4;
        if (minX <= minimumEdgeMargin
            || maxX >= image.PixelWidth - 1 - minimumEdgeMargin
            || minY <= minimumEdgeMargin
            || maxY >= image.PixelHeight - 1 - minimumEdgeMargin)
        {
            throw new InvalidDataException(
                $"The dance preview touched the canvas edge: {label} "
                + $"(bounds {minX},{minY}-{maxX},{maxY}).");
        }
    }
}
