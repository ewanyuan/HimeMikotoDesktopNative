using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace HimeMikotoDesktopNative;

public partial class MainWindow : Window
{
    private const double SinglePetWidth = 480.0;
    private const double DualPetWidth = 1040.0;
    private static readonly string[] SupportedMusicExtensions =
    [
        ".mp3",
        ".wav",
        ".ogg",
        ".m4a",
        ".aac",
        ".flac",
        ".mp4",
    ];
    private readonly bool _autoLoad;
    private readonly string _repositoryRoot;
    private readonly string[] _modelPaths;
    private readonly DanceMotionDefinition[] _danceMotions;
    private readonly bool _english;
    private readonly ContextMenu _petMenu = new();
    private readonly MenuItem _switchCharacterMenu = new();
    private readonly MenuItem _danceMenu = new();
    private readonly MenuItem _pauseDanceMenu = new();
    private readonly MenuItem _appearanceMenu = new();
    private readonly MenuItem _skinToneMenu = new();
    private readonly MenuItem _nakedMenu = new();
    private readonly MenuItem _eyeExpressionMenu = new();
    private readonly MenuItem _mouthExpressionMenu = new();
    private readonly MenuItem _clothingMenu = new();
    private readonly MenuItem _adultMenu = new();
    private readonly MenuItem _allMorphsMenu = new();
    private readonly MenuItem _resetMorphsMenu = new();
    private readonly MenuItem _alwaysOnTopMenu = new();
    private readonly MenuItem _closeMenu = new();
    private readonly TaskCompletionSource<bool> _runtimeReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<int, MenuItem> _morphMenuItems = [];
    private readonly Dictionary<MenuItem, int> _activeMorphBySubmenu = [];
    private readonly List<MenuItem> _danceMenuItems = [];
    private readonly Dictionary<string, MenuItem> _skinToneMenuItems = [];
    private static readonly SolidColorBrush MenuHoverBackground = CreateBrush(0xF4, 0xDC, 0xEB);
    private static readonly SolidColorBrush MenuHoverForeground = CreateBrush(0x7D, 0x31, 0x5E);
    private static readonly IReadOnlyDictionary<string, string> ChineseMorphNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Blink"] = "眨眼",
            ["Blink_L"] = "左眨眼",
            ["Blink_R"] = "右眨眼",
            ["Laugh"] = "笑",
            ["Laugh_L"] = "左笑眼",
            ["Laugh_R"] = "右笑眼",
            ["Fleer"] = "嘲笑",
            ["Relax"] = "放松",
            ["Jitome"] = "斜眼",
            ["EyeAngry"] = "生气眼",
            ["Surprised"] = "惊讶",
            ["SadSquint"] = "悲伤眼",
            ["Squint"] = "眯眼",
            ["SquintLow"] = "低眼",
            ["Puzzled"] = "疑惑",
            ["UpperLid"] = "上眼睑",
            ["LowerLid"] = "下眼睑",
            ["LowerSneer"] = "轻蔑",
            ["LookUP"] = "向上看",
            ["LookDOWN"] = "向下看",
            ["LookNEAR"] = "斗鸡眼",
            ["LookAT"] = "看向前方",
            ["Iris-"] = "缩小虹膜",
            ["Pupil-"] = "缩小瞳孔",
            ["BlackEye"] = "黑眼",
            ["PupilHeart"] = "爱心眼",
            ["StarEyes"] = "星星眼",
            ["x_HiLit"] = "隐藏眼睛高光",
            ["x_Pupil"] = "隐藏瞳孔",
            ["Serious"] = "认真",
            ["Anger"] = "生气",
            ["BrowMid+"] = "眉心上提",
            ["BrowMid-"] = "眉心下压",
            ["BrowIn+"] = "眉头上提",
            ["BrowIn-"] = "眉头下压",
            ["BrowInTip+"] = "内眉上提",
            ["BrowInTip-"] = "内眉下压",
            ["BrowOut+"] = "眉尾上提",
            ["BrowOut-"] = "眉尾下压",
            ["BrowV+"] = "眉毛上提",
            ["BrowV-"] = "眉毛下压",
            ["BrowZFix"] = "眉毛防遮挡",
            ["Aa"] = "啊",
            ["Ih"] = "咿",
            ["Ou"] = "呜",
            ["Ee"] = "诶",
            ["Oh"] = "哦",
            ["Whee"] = "欢呼",
            ["Shout"] = "大喊",
            ["Nya"] = "喵",
            ["Sniff"] = "嗅闻",
            ["LipMid+"] = "嘴唇上提",
            ["LipMid-"] = "嘴唇下压",
            ["CornerUP"] = "嘴角上扬",
            ["CornerDOWN"] = "嘴角下压",
            ["CornerWIDE"] = "嘴角扩大",
            ["TongueSmile_L"] = "左侧吐舌",
            ["TongueSmile_R"] = "右侧吐舌",
            ["FoolSmile"] = "流口水",
            ["MouthUP"] = "嘴巴上移",
            ["MouthDOWN"] = "嘴巴下移",
            ["MouthLEFT"] = "嘴巴左移",
            ["MouthRIGHT"] = "嘴巴右移",
            ["JawFront"] = "下巴前移",
            ["x_Teeth"] = "隐藏牙齿",
            ["TeethOpen"] = "张牙",
            ["TeethClose"] = "收牙",
            ["TeethBack"] = "牙齿后移",
            ["TeethWIDE"] = "牙齿扩大",
            ["TeethGiza"] = "锯齿",
            ["TongueSharpe+"] = "舌头展开",
            ["TongueSharpe-"] = "舌头收拢",
            ["ShadowShade"] = "阴影",
            ["BlushLine"] = "害羞线",
            ["Flattered"] = "害羞",
            ["FaceShade"] = "脸部阴影",
            ["Sweat"] = "汗",
            ["Tattoo"] = "纹身",
            ["x_Barefoot"] = "赤脚",
            ["x_OnlyShoes"] = "仅保留鞋子",
            ["x_OnlyStocking"] = "仅保留长袜",
            ["x_Choker"] = "项圈",
            ["x_Skirt"] = "裙子",
            ["x_TankTop"] = "背心",
            ["TTop_Slip"] = "胸口衣物下滑",
            ["x_Pants"] = "短裤",
            ["TbTop_SlipUP"] = "胸口衣物上移",
            ["TbTop_SlipDOWN"] = "胸口衣物下移",
            ["x_Tubetop"] = "抹胸",
            ["x_T-Back"] = "丁字裤",
            ["Blowjob"] = "口部形变",
            ["NippleSize+"] = "乳头变大",
            ["NippleStand"] = "乳头挺立",
            ["NippleSize-"] = "乳头变小",
            ["Boko"] = "腹部受击",
            ["BokoInv"] = "腹部凹陷",
            ["BokoLine"] = "腹部线条",
            ["CliSize+"] = "阴蒂变大",
            ["LabOpen_S"] = "阴唇打开（小）",
            ["LabOpen_M"] = "阴唇打开（中）",
            ["LabOpen_L"] = "阴唇打开（大）",
            ["LabOpen_XL"] = "阴唇打开（特大）",
            ["AnaOpen"] = "肛部打开",
            ["AnaOpenEX"] = "肛部打开（异物）",
        };
    private static readonly IReadOnlySet<string> VisibleExpressionMorphNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Eye and face expressions with an obvious result at desktop-pet scale.
            "Blink",
            "Blink_L",
            "Blink_R",
            "Laugh",
            "Laugh_L",
            "Laugh_R",
            "Fleer",
            "Relax",
            "Jitome",
            "EyeAngry",
            "Surprised",
            "SadSquint",
            "Squint",
            "SquintLow",
            "Puzzled",
            "UpperLid",
            "LookUP",
            "LookDOWN",
            "LookNEAR",
            "LookAT",
            "Iris-",
            "Pupil-",
            "BlackEye",
            "PupilHeart",
            "StarEyes",
            "x_HiLit",
            "x_Pupil",

            // Main brow expressions; corrective/anti-overlap and one-pixel helpers are omitted.
            "Serious",
            "Anger",
            "BrowMid+",
            "BrowMid-",
            "BrowIn+",
            "BrowIn-",
            "BrowV+",
            "BrowV-",

            // Main mouth expressions and phonemes; tiny positional/teeth/tongue helpers are omitted.
            "Aa",
            "Ih",
            "Ou",
            "Ee",
            "Oh",
            "Wa",
            "Whee",
            "Shout",
            "Nya",
            "Sniff",
            "LipMid+",
            "LipMid-",
            "CornerUP",
            "GrinClose",
            "Grin",
            "Tricky",
            "CornerDOWN",
            "Bad_L",
            "Annoy_L",
            "AnnoyLow_L",
            "PuffCheek_L",
            "CornerWIDE",
            "FoolSmile",
        };
    private static readonly IReadOnlySet<string> EyeExpressionMorphNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Blink",
            "Blink_L",
            "Blink_R",
            "Laugh",
            "Laugh_L",
            "Laugh_R",
            "Fleer",
            "Relax",
            "Jitome",
            "EyeAngry",
            "Surprised",
            "SadSquint",
            "Squint",
            "SquintLow",
            "Puzzled",
            "UpperLid",
            "LookUP",
            "LookDOWN",
            "LookNEAR",
            "LookAT",
            "Iris-",
            "Pupil-",
            "BlackEye",
            "PupilHeart",
            "StarEyes",
            "x_HiLit",
            "x_Pupil",
            "Serious",
            "Anger",
            "BrowMid+",
            "BrowMid-",
            "BrowIn+",
            "BrowIn-",
            "BrowV+",
            "BrowV-",
        };
    private TaskCompletionSource<JsonElement>? _pendingMessage;
    private string? _pendingMessageType;
    private string? _lastRuntimeError;
    private int _currentCharacter;
    private bool _browserInitialized;
    private bool _navigationStarted;
    private bool _closing;
    private bool _dualMode;
    private bool _fullNudeActive;
    private MenuItem? _activeDanceMenuItem;
    private MenuItem? _hoveredMenuItem;
    private string TracePath => Path.Combine(Directory.GetCurrentDirectory(), "mmd-runtime-self-test-trace.txt");

    public MainWindow(bool autoLoad)
    {
        _autoLoad = autoLoad;
        _repositoryRoot = FindRepositoryRoot();
        _modelPaths =
        [
            ResolveModelPath(
                "Hime_260426",
                "Hime.physics-stable.pmx"),
            ResolveModelPath(
                "Mikoto_260303",
                "Mikoto.physics-stable.pmx"),
        ];
        _danceMotions =
        [
            new(
                "snow-halation",
                "Snow Halation",
                "Snow Halation",
                Path.Combine(
                    "HimeMikotoDesktopNative",
                    "assets",
                    "motions",
                    "snow-halation-natsuki",
                    "nac_snow_halation",
                    "nac_snow_halation.vmd")),
            new(
                "helltaker",
                "Helltaker（循环舞）",
                "Helltaker (loop dance)",
                Path.Combine(
                    "HimeMikotoDesktopNative",
                    "assets",
                    "motions",
                    "helltaker",
                    "Helltaker_like_dance_1min_1.vmd")),
            new(
                "telepathy",
                "Telepathy（上半身）",
                "Telepathy (upper body)",
                Path.Combine(
                    "HimeMikotoDesktopNative",
                    "assets",
                    "motions",
                    "telepathy",
                    "P_Telepathy_motion_",
                    "Telepathy1235.vmd")),
            new(
                "beyond-the-way",
                "Beyond the way（力量感）",
                "Beyond the way (energetic)",
                Path.Combine(
                    "HimeMikotoDesktopNative",
                    "assets",
                    "motions",
                    "beyond-the-way",
                    "P_BeyondTW_Motion",
                    "BeyoudTW_motion.vmd")),
            new(
                "tokyo-shandy-rendezvous",
                "东京・香迪・兰德弗（风格化）",
                "Tokyo Shandy Rendezvous (stylized)",
                Path.Combine(
                    "HimeMikotoDesktopNative",
                    "assets",
                    "motions",
                    "tokyo-shandy-rendezvous",
                    "P_TokyoSR_モーション配布",
                    "TokyoSR_motion.vmd")),
            new(
                "queen",
                "QUEEN（酷帅）",
                "QUEEN (sharp)",
                Path.Combine(
                    "HimeMikotoDesktopNative",
                    "assets",
                    "motions",
                    "queen",
                    "QUEEN_miku.vmd")),
            new(
                "womanizer-dual",
                "Womanizer（双人・魅惑）",
                "Womanizer (dual, sensual)",
                Path.Combine(
                    "HimeMikotoDesktopNative",
                    "assets",
                    "motions",
                    "womanizer",
                    "womanizer-left.vmd"),
                Path.Combine(
                    "HimeMikotoDesktopNative",
                    "assets",
                    "motions",
                    "womanizer",
                    "womanizer-right.vmd")),
            new(
                "erotic-hip-duo",
                "Erotic Hip-Shaking Duo（双人）",
                "Erotic Hip-Shaking Duo (dual)",
                Path.Combine(
                    "HimeMikotoDesktopNative",
                    "assets",
                    "motions",
                    "erotic-hip-duo",
                    "EroHipDuoDance_Model-L_Motion.vmd"),
                Path.Combine(
                    "HimeMikotoDesktopNative",
                    "assets",
                    "motions",
                    "erotic-hip-duo",
                    "EroHipDuoDance_Model-R_Motion.vmd"),
                UsageNote: "动作作者 CraftieMMD；允许修改和 R18 使用，但禁止未经许可再分发动作数据。本机版本先只加载双人身体动作，不自动加载面部、镜头或持杆附加动作。",
                StartFrame: 330),
            new(
                "waist-dance",
                "腰振り舞（诱惑）",
                "Waist dance (sensual)",
                Path.Combine(
                    "HimeMikotoDesktopNative",
                    "assets",
                    "motions",
                    "waist-dance",
                    "waist-dance-loop.vmd"),
                Loop: true),
            new(
                "rust-veins",
                "Rust Veins 腰振舞",
                "Rust Veins hip-sway dance",
                Path.Combine(
                    "HimeMikotoDesktopNative",
                    "assets",
                    "motions",
                    "rust-veins",
                    "motion 1.vmd"),
                UsageNote: "动作作者 TottyMMD（totozoMMD）；仅作本机使用，使用前请保留随包 readme.txt 并自行确认发布范围。"),
            new(
                "tsuyoi",
                "つよっ！（单人）",
                "Tsuyoi! (solo)",
                Path.Combine(
                    "HimeMikotoDesktopNative",
                    "assets",
                    "motions",
                    "requested",
                    "tsuyoi",
                    "tsuyoi.vmd"),
                UsageNote: "非商业使用；禁止 R18、再分发或转交动作文件。",
                UnavailableNote: "动作文件缺失"),
            new(
                "inmu-king",
                "INMU KING（动作待补）",
                "INMU KING (motion pending)",
                Path.Combine(
                    "HimeMikotoDesktopNative",
                    "assets",
                    "motions",
                    "requested",
                    "inmu-king",
                    "INMU-KING.vmd"),
                UnavailableNote: "原视频未公开可核验的 VMD 动作文件，暂不伪造动作。"),
            new(
                "tick-trick",
                "Tick-Trick（Rick式）",
                "Tick-Trick (Rick)",
                Path.Combine(
                    "HimeMikotoDesktopNative",
                    "assets",
                    "motions",
                    "requested",
                    "tick-trick",
                    "Tick-Trick.vmd"),
                UsageNote: "非商业、非 R15；禁止再分发，且需保留乐曲作者前線的署名。",
                UnavailableNote: "动作文件缺失"),
            new(
                "oppai-fukkireta",
                "Oppai Fukkireta（授权待定）",
                "Oppai Fukkireta (license pending)",
                Path.Combine(
                    "HimeMikotoDesktopNative",
                    "assets",
                    "motions",
                    "requested",
                    "oppai-fukkireta",
                    "Oppai-Fukkireta.vmd"),
                UnavailableNote: "原作者条款禁止将动作嵌入游戏，当前不能合法集成。"),
        ];
        _english = !IsChineseSystemLanguage();

        InitializeComponent();
        Topmost = true;
        BuildBaseContextMenu();
        Browser.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0, 0, 0, 0);
        Loaded += OnLoaded;
    }

    public string RepositoryRoot => _repositoryRoot;

    internal IReadOnlyList<DanceMotionDefinition> DanceMotions => _danceMotions;

    internal string GetDanceMotionPath(DanceMotionDefinition dance)
    {
        return ResolveProjectPath(dance.RelativePath);
    }

    internal string? GetSecondaryDanceMotionPath(DanceMotionDefinition dance)
    {
        return dance.SecondaryRelativePath is null
            ? null
            : ResolveProjectPath(dance.SecondaryRelativePath);
    }

    internal string? GetDanceMusicPath(DanceMotionDefinition dance)
    {
        var musicDirectory = ResolveProjectPath(
            Path.Combine("HimeMikotoDesktopNative", "assets", "music"));
        if (Directory.Exists(musicDirectory))
        {
            var musicPath = Directory.EnumerateFiles(musicDirectory)
                .FirstOrDefault(path =>
                    string.Equals(
                        Path.GetFileNameWithoutExtension(path),
                        dance.Key,
                        StringComparison.OrdinalIgnoreCase)
                    && SupportedMusicExtensions.Contains(
                        Path.GetExtension(path),
                        StringComparer.OrdinalIgnoreCase));
            if (musicPath is not null)
            {
                return musicPath;
            }
        }

        return null;
    }

    internal async Task<JsonElement> LoadDanceMusicAsync(DanceMotionDefinition dance)
    {
        var musicPath = GetDanceMusicPath(dance);
        var messageType = musicPath is null ? "music-cleared" : "music-loaded";
        var messageTask = WaitForMessageAsync(messageType);
        if (musicPath is null)
        {
            PostCommand(new { type = "clear-music" });
        }
        else
        {
            PostCommand(new
            {
                type = "load-music",
                url = ToRuntimeUrl(musicPath),
            });
        }

        return await messageTask;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_autoLoad)
        {
            return;
        }

        try
        {
            await InitializeRuntimeAsync();
            await LoadCharacterAsync(_currentCharacter);
            ActivatePetFocus();
        }
        catch (Exception exception)
        {
            _lastRuntimeError = exception.ToString();
            Trace($"startup-failure:{exception}");
            ShowStartupError(exception);
            Close();
        }
    }

    private void ShowStartupError(Exception exception)
    {
        var rootException = exception.GetBaseException();
        var message = rootException switch
        {
            FileNotFoundException fileException
                when fileException.FileName?.EndsWith(".pmx", StringComparison.OrdinalIgnoreCase) == true
                => Text(
                    "没有找到人物模型。请确认便携包内保留了 assets\\Hime_&_Mikoto 文件夹。",
                    "The character model is missing. Make sure the portable package still contains the assets\\Hime_&_Mikoto folder."),
            _ when rootException.Message.Contains("WebView2", StringComparison.OrdinalIgnoreCase)
                => Text(
                    "桌宠需要 Microsoft Edge WebView2 Runtime。请先安装它，再重新双击程序。",
                    "This desktop pet needs the Microsoft Edge WebView2 Runtime. Install it, then start the app again."),
            _ => Text(
                "桌宠启动失败。请确认你运行的是完整文件夹里的 exe，而不是只复制了 exe 文件。\n\n"
                    + rootException.Message,
                "The desktop pet could not start. Make sure you are running the exe from the complete folder, not a copied exe file.\n\n"
                    + rootException.Message),
        };

        MessageBox.Show(
            this,
            message,
            Text("Hime & Mikoto 桌宠", "Hime & Mikoto Desktop Pet"),
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    public async Task InitializeRuntimeAsync()
    {
        Trace("initialize-start");
        if (!_browserInitialized)
        {
            var dataDirectory = Path.Combine(
                Path.GetTempPath(),
                "HimeMikotoDesktopNative-WebView2-" + Guid.NewGuid().ToString("N"));
            Trace("before-environment");
            var environment = await CoreWebView2Environment.CreateAsync(null, dataDirectory);
            Trace("after-environment");
            Browser.CoreWebView2InitializationCompleted += OnCoreWebView2InitializationCompleted;
            Trace("before-ensure");
            await Browser.EnsureCoreWebView2Async(environment);
            Trace("after-ensure");
            _browserInitialized = true;
        }

        if (!_navigationStarted)
        {
            var core = Browser.CoreWebView2
                ?? throw new InvalidOperationException("WebView2 did not initialize.");
            core.WebMessageReceived += OnWebMessageReceived;
            core.NavigationCompleted += OnNavigationCompleted;
            core.ProcessFailed += OnProcessFailed;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.SetVirtualHostNameToFolderMapping(
                "hime.local",
                _repositoryRoot,
                CoreWebView2HostResourceAccessKind.Allow);
            _navigationStarted = true;
            Trace("before-navigate");
            var webIndexPath = ResolveProjectPath(
                Path.Combine("HimeMikotoDesktopNative", "web", "index.html"));
            core.Navigate(ToRuntimeUrl(webIndexPath));
            Trace("after-navigate");
        }

        Trace("before-ready");
        await _runtimeReady.Task.WaitAsync(TimeSpan.FromSeconds(45));
        Trace("after-ready");
    }

    public async Task<JsonElement> LoadCharacterAsync(int characterIndex)
    {
        if (characterIndex < 0 || characterIndex >= _modelPaths.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(characterIndex));
        }

        var path = _modelPaths[characterIndex];
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The PMX model was not found.", path);
        }

        _currentCharacter = characterIndex;
        var messageTask = WaitForMessageAsync("model-loaded");
        PostCommand(new
        {
            type = "load-character",
            index = characterIndex,
            url = ToRuntimeUrl(path),
        });
        var message = await messageTask;
        PopulateMorphMenus(message);
        _dualMode = false;
        Width = SinglePetWidth;
        _pauseDanceMenu.IsEnabled = false;
        _appearanceMenu.IsEnabled = true;
        _resetMorphsMenu.IsEnabled = true;
        await ApplyFullNudeIfNeededAsync();
        return message;
    }

    public async Task<JsonElement> LoadMotionAsync(string name, string path, bool loop = false)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The VMD motion was not found.", path);
        }

        var messageTask = WaitForMessageAsync("motion-loaded");
        PostCommand(new
        {
            type = "load-motion",
            name,
            url = ToRuntimeUrl(path),
            loop,
        });
        return await messageTask;
    }

    public async Task<JsonElement> LoadDualCharactersAsync(int primaryIndex, int secondaryIndex)
    {
        if (primaryIndex < 0 || primaryIndex >= _modelPaths.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(primaryIndex));
        }
        if (secondaryIndex < 0 || secondaryIndex >= _modelPaths.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(secondaryIndex));
        }

        var primaryPath = _modelPaths[primaryIndex];
        var secondaryPath = _modelPaths[secondaryIndex];
        if (!File.Exists(primaryPath))
        {
            throw new FileNotFoundException("The primary PMX model was not found.", primaryPath);
        }
        if (!File.Exists(secondaryPath))
        {
            throw new FileNotFoundException("The secondary PMX model was not found.", secondaryPath);
        }

        var messageTask = WaitForMessageAsync("dual-models-loaded");
        PostCommand(new
        {
            type = "load-dual-character",
            primaryIndex,
            primaryUrl = ToRuntimeUrl(primaryPath),
            secondaryIndex,
            secondaryUrl = ToRuntimeUrl(secondaryPath),
        });
        var message = await messageTask;
        PopulateMorphMenus(message.GetProperty("primary"));
        _currentCharacter = primaryIndex;
        _dualMode = true;
        Width = DualPetWidth;
        _pauseDanceMenu.IsEnabled = false;
        _appearanceMenu.IsEnabled = true;
        _resetMorphsMenu.IsEnabled = true;
        await ApplyFullNudeIfNeededAsync();
        return message;
    }

    public async Task<JsonElement> LoadDualMotionAsync(
        string name,
        string primaryPath,
        string secondaryPath,
        int startFrame = 0)
    {
        if (!File.Exists(primaryPath))
        {
            throw new FileNotFoundException("The primary VMD motion was not found.", primaryPath);
        }
        if (!File.Exists(secondaryPath))
        {
            throw new FileNotFoundException("The secondary VMD motion was not found.", secondaryPath);
        }

        var messageTask = WaitForMessageAsync("dual-motion-loaded");
        PostCommand(new
        {
            type = "load-dual-motion",
            name,
            primaryUrl = ToRuntimeUrl(primaryPath),
            secondaryUrl = ToRuntimeUrl(secondaryPath),
            startFrame,
        });
        return await messageTask;
    }

    public async Task<JsonElement> SetSkinToneAsync(string tone)
    {
        var messageTask = WaitForMessageAsync("skin-tone-updated");
        PostCommand(new
        {
            type = "set-skin-tone",
            tone,
        });
        return await messageTask;
    }

    public async Task<JsonElement> SetMorphAsync(int index, double weight)
    {
        var messageTask = WaitForMessageAsync("morph-updated");
        PostCommand(new
        {
            type = "set-morph",
            index,
            weight,
        });
        return await messageTask;
    }

    public async Task<JsonElement> ResetMorphsAsync()
    {
        var messageTask = WaitForMessageAsync("morphs-reset");
        PostCommand(new { type = "reset-morphs" });
        return await messageTask;
    }

    public async Task PlayAsync()
    {
        PostCommand(new { type = "play" });
        await Task.Yield();
    }

    public async Task PauseAsync()
    {
        PostCommand(new { type = "pause" });
        await Task.Yield();
    }

    internal Task<JsonElement> WaitForRuntimeMessageAsync(string type)
    {
        return WaitForMessageAsync(type);
    }

    public async Task CapturePreviewAsync(string outputPath)
    {
        var core = Browser.CoreWebView2
            ?? throw new InvalidOperationException("WebView2 is not initialized.");
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
    }

    internal void UseOpaqueCaptureBackgroundForTest()
    {
        Browser.DefaultBackgroundColor = System.Drawing.Color.White;
    }

    internal void UseTransparentCaptureBackgroundForTest()
    {
        Browser.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0, 0, 0, 0);
    }

    private void OnCoreWebView2InitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        Trace($"core-init:{e.IsSuccess}");
        if (!e.IsSuccess)
        {
            _lastRuntimeError = e.InitializationException?.ToString() ?? "WebView2 initialization failed.";
            _runtimeReady.TrySetException(new InvalidOperationException(_lastRuntimeError));
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        Trace($"navigation:{e.IsSuccess}:{e.WebErrorStatus}");
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        Trace($"process-failed:{e.ProcessFailedKind}:{e.Reason}");
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(type))
            {
                return;
            }
            Trace($"message:{type}");
            if (type.Equals("motion-loaded", StringComparison.Ordinal)
                && root.TryGetProperty("rootOffset", out var rootOffset))
            {
                Trace($"motion-root-offset:{rootOffset}");
            }

            if (type.Equals("runtime-ready", StringComparison.Ordinal))
            {
                _runtimeReady.TrySetResult(true);
                return;
            }

            if (type.Equals("runtime-error", StringComparison.Ordinal))
            {
                var message = root.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : "Unknown MMD runtime error.";
                var stack = root.TryGetProperty("stack", out var stackElement)
                    ? stackElement.GetString()
                    : string.Empty;
                _lastRuntimeError = $"{message}{Environment.NewLine}{stack}";
                Trace($"runtime-error:{_lastRuntimeError}");
                var exception = new InvalidOperationException(_lastRuntimeError);
                _runtimeReady.TrySetException(exception);
                _pendingMessage?.TrySetException(exception);
                return;
            }

            if (type.Equals("context-menu", StringComparison.Ordinal))
            {
                OpenPetMenu();
                return;
            }

            if (string.Equals(type, _pendingMessageType, StringComparison.Ordinal))
            {
                _pendingMessage?.TrySetResult(root.Clone());
            }
        }
        catch (Exception exception)
        {
            _lastRuntimeError = exception.ToString();
            _pendingMessage?.TrySetException(exception);
        }
    }

    private async Task<JsonElement> WaitForMessageAsync(string type)
    {
        if (_pendingMessage != null)
        {
            throw new InvalidOperationException("Another MMD runtime request is already pending.");
        }

        var source = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingMessage = source;
        _pendingMessageType = type;
        try
        {
            return await source.Task.WaitAsync(TimeSpan.FromSeconds(60));
        }
        finally
        {
            if (ReferenceEquals(_pendingMessage, source))
            {
                _pendingMessage = null;
                _pendingMessageType = null;
            }
        }
    }

    private void PostCommand(object command)
    {
        var core = Browser.CoreWebView2
            ?? throw new InvalidOperationException("WebView2 is not initialized.");
        core.PostWebMessageAsJson(JsonSerializer.Serialize(command));
    }

    private void BuildBaseContextMenu()
    {
        _switchCharacterMenu.Header = Text("👤 切换人物", "👤 Switch character");
        ConfigureMenuItem(_switchCharacterMenu);
        _switchCharacterMenu.StaysOpenOnClick = true;
        _switchCharacterMenu.Click += OnSwitchCharacterMenuClick;

        _appearanceMenu.Header = Text("✨ 外观", "✨ Appearance");
        ConfigureMenuItem(_appearanceMenu);
        _appearanceMenu.IsEnabled = false;

        _skinToneMenu.Header = Text("肤色", "Skin tone");
        BuildSkinToneMenu();

        _nakedMenu.Header = Text("全裸", "Full nude");
        ConfigureMenuItem(_nakedMenu);
        _nakedMenu.Visibility = Visibility.Collapsed;
        _nakedMenu.StaysOpenOnClick = true;
        _nakedMenu.Click += OnMorphMenuClick;

        _danceMenu.Header = Text("🎵 舞蹈", "🎵 Dance");
        ConfigureMenuItem(_danceMenu);
        BuildDanceMenu();

        _pauseDanceMenu.Header = Text("暂停动作", "Pause motion");
        ConfigureMenuItem(_pauseDanceMenu);
        _pauseDanceMenu.StaysOpenOnClick = true;
        _pauseDanceMenu.Click += OnPauseDanceMenuClick;
        _pauseDanceMenu.IsEnabled = false;
        _danceMenu.Items.Add(new Separator());
        _danceMenu.Items.Add(_pauseDanceMenu);

        _eyeExpressionMenu.Header = Text("眼睛表情", "Eye expressions");
        _mouthExpressionMenu.Header = Text("嘴部表情", "Mouth expressions");
        _clothingMenu.Header = Text("服装与鞋袜", "Clothes and footwear");
        _adultMenu.Header = Text("成人形变", "Adult morphs");
        _allMorphsMenu.Header = Text("全部形变", "All morphs");
        ConfigureMenuItem(_eyeExpressionMenu);
        ConfigureMenuItem(_mouthExpressionMenu);
        ConfigureMenuItem(_clothingMenu);
        ConfigureMenuItem(_adultMenu);
        ConfigureMenuItem(_allMorphsMenu);
        _allMorphsMenu.Visibility = Visibility.Collapsed;

        _resetMorphsMenu.Header = Text("恢复默认形变", "Reset morphs");
        ConfigureMenuItem(_resetMorphsMenu);
        _resetMorphsMenu.StaysOpenOnClick = true;
        _resetMorphsMenu.Click += OnResetMorphsMenuClick;
        _resetMorphsMenu.IsEnabled = false;

        _appearanceMenu.Items.Add(_skinToneMenu);
        _appearanceMenu.Items.Add(_nakedMenu);
        _appearanceMenu.Items.Add(new Separator());
        _appearanceMenu.Items.Add(_eyeExpressionMenu);
        _appearanceMenu.Items.Add(_mouthExpressionMenu);
        _appearanceMenu.Items.Add(_clothingMenu);
        _appearanceMenu.Items.Add(_adultMenu);
        _appearanceMenu.Items.Add(_allMorphsMenu);
        _appearanceMenu.Items.Add(new Separator());
        _appearanceMenu.Items.Add(_resetMorphsMenu);

        _alwaysOnTopMenu.Header = Text("📌 始终置顶", "📌 Always on top");
        ConfigureMenuItem(_alwaysOnTopMenu);
        _alwaysOnTopMenu.IsCheckable = true;
        _alwaysOnTopMenu.IsChecked = true;
        _alwaysOnTopMenu.StaysOpenOnClick = true;
        _alwaysOnTopMenu.Click += OnAlwaysOnTopMenuClick;

        _closeMenu.Header = Text("关闭桌宠", "Close desktop pet");
        ConfigureMenuItem(_closeMenu);
        _closeMenu.Click += OnCloseMenuClick;

        _petMenu.Items.Add(_switchCharacterMenu);
        _petMenu.Items.Add(_danceMenu);
        _petMenu.Items.Add(_appearanceMenu);
        _petMenu.Items.Add(_alwaysOnTopMenu);
        _petMenu.Items.Add(new Separator());
        _petMenu.Items.Add(_closeMenu);
        _petMenu.PlacementTarget = this;
        _petMenu.Opened += OnPetMenuOpened;
        _petMenu.Closed += OnPetMenuClosed;
        ContextMenu = _petMenu;
    }

    private void BuildSkinToneMenu()
    {
        var options = new[]
        {
            (Key: "brighter", Header: Text("默认", "Default")),
            (Key: "lighter", Header: Text("稍深", "Slightly darker")),
            (Key: "deep", Header: Text("更深", "Darker")),
        };

        foreach (var option in options)
        {
            var item = new MenuItem
            {
                Header = option.Header,
                Tag = option.Key,
                IsCheckable = false,
                StaysOpenOnClick = true,
            };
            ConfigureMenuItem(item);
            SetRadioIndicator(item, selected: false);
            item.Click += OnSkinToneMenuClick;
            _skinToneMenu.Items.Add(item);
            _skinToneMenuItems[option.Key] = item;
        }

        SetSkinToneMenuSelection("brighter");
    }

    private void BuildDanceMenu()
    {
        var availableCount = 0;
        foreach (var dance in _danceMotions)
        {
            var path = GetDanceMotionPath(dance);
            var secondaryPath = GetSecondaryDanceMotionPath(dance);
            var musicPath = GetDanceMusicPath(dance);
            var isAvailable = File.Exists(path)
                && (secondaryPath is null || File.Exists(secondaryPath));
            if (!isAvailable)
            {
                continue;
            }

            availableCount++;
            var item = new MenuItem
            {
                Header = Text(dance.ChineseName, dance.EnglishName)
                    + (musicPath is null ? string.Empty : Text("（有本地音乐）", " (local music)")),
                Tag = dance,
                IsEnabled = true,
                IsCheckable = false,
                StaysOpenOnClick = true,
            };
            ConfigureMenuItem(item);
            SetRadioIndicator(item, selected: false);
            _danceMenuItems.Add(item);
            var tooltipParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(dance.UsageNote))
            {
                tooltipParts.Add(dance.UsageNote);
            }

            if (musicPath is not null)
            {
                tooltipParts.Add(Text(
                    $"音乐：{Path.GetRelativePath(_repositoryRoot, musicPath).Replace('\\', '/')}",
                    $"Music: {Path.GetRelativePath(_repositoryRoot, musicPath).Replace('\\', '/')}"));
            }
            else if (item.IsEnabled)
            {
                tooltipParts.Add(Text(
                    "未附带音乐；可将与动作同名的 mp3、wav、ogg 或 m4a 放入 assets/music",
                    "No track is bundled; add a same-name mp3, wav, ogg, or m4a file to assets/music"));
            }

            if (tooltipParts.Count > 0)
            {
                item.ToolTip = string.Join(Environment.NewLine, tooltipParts);
            }

            item.Click += OnDanceMotionMenuClick;
            _danceMenu.Items.Add(item);
        }

        if (availableCount == 0)
        {
            AddDisabledItem(_danceMenu, Text("暂无可用舞蹈", "No dance motion is available"));
        }
    }

    private async void OnSkinToneMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tone })
        {
            return;
        }

        SetSkinToneMenuSelection(tone);
        try
        {
            await SetSkinToneAsync(tone);
        }
        catch (Exception exception)
        {
            _lastRuntimeError = exception.ToString();
        }
    }

    private void SetSkinToneMenuSelection(string tone)
    {
        foreach (var item in _skinToneMenuItems)
        {
            SetRadioIndicator(
                item.Value,
                string.Equals(item.Key, tone, StringComparison.Ordinal));
        }
    }

    private void PopulateMorphMenus(JsonElement message)
    {
        _morphMenuItems.Clear();
        _activeMorphBySubmenu.Clear();
        _nakedMenu.Tag = null;
        _nakedMenu.Visibility = Visibility.Collapsed;
        SetRadioIndicator(_nakedMenu, selected: false);
        ClearMenu(_eyeExpressionMenu);
        ClearMenu(_mouthExpressionMenu);
        ClearMenu(_clothingMenu);
        ClearMenu(_adultMenu);
        ClearMenu(_allMorphsMenu);

        var eyeExpressions = new List<(int Index, string Name)>();
        var mouthExpressions = new List<(int Index, string Name)>();
        var clothing = new List<(int Index, string Name)>();
        var adult = new List<(int Index, string Name)>();
        var inAdultSection = false;

        foreach (var morph in message.GetProperty("morphs").EnumerateArray())
        {
            var index = morph.GetProperty("index").GetInt32();
            var name = morph.GetProperty("name").GetString() ?? $"Morph {index}";
            var englishName = morph.TryGetProperty("englishName", out var englishNameElement)
                ? englishNameElement.GetString() ?? string.Empty
                : string.Empty;
            var category = morph.TryGetProperty("category", out var categoryElement)
                ? categoryElement.GetInt32()
                : 4;
            var nameLower = name.ToLowerInvariant();

            if (name.StartsWith("--", StringComparison.Ordinal))
            {
                inAdultSection = name.Contains("r18", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("adult", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("成人", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (IsInternalMorph(name, englishName))
            {
                continue;
            }

            if (IsFullNudeMorph(name, englishName))
            {
                _nakedMenu.Tag = index;
                _nakedMenu.Visibility = Visibility.Visible;
                _morphMenuItems[index] = _nakedMenu;
                continue;
            }

            var isAdult = inAdultSection
                || nameLower.Contains("r18", StringComparison.Ordinal)
                || nameLower.Contains("adult", StringComparison.Ordinal)
                || name.Contains("成人", StringComparison.Ordinal);
            var isClothing = ContainsAny(nameLower,
                "服", "衣", "裙", "裤", "鞋", "袜", "靴", "裸足", "ストッキング", "ソックス", "パンツ",
                "スカート", "チョーカー", "タンク", "胸はだけ", "スリップ", "チューブ", "t-バック", "tバック",
                "水着", "下着", "cloth", "wear", "shoes", "stocking", "pants");
            var isExpression = category is 1 or 2 or 3 || ContainsAny(nameLower,
                "笑", "怒", "困", "驚", "びっくり", "まばたき", "瞬き", "ウィンク", "wink", "blink",
                "smile", "angry", "blush", "赤面", "涙", "目", "口", "舌", "mouth", "eye");

            if (isAdult)
            {
                if (IsUsefulAdultMorph(name, englishName, isClothing))
                {
                    adult.Add((index, MorphHeader(name, englishName)));
                }
            }
            else if (isClothing)
            {
                clothing.Add((index, MorphHeader(name, englishName)));
            }
            else if (isExpression && IsVisibleExpressionMorph(name, englishName))
            {
                var expression = (index, MorphHeader(name, englishName));
                if (IsEyeExpressionMorph(name, englishName))
                {
                    eyeExpressions.Add(expression);
                }
                else
                {
                    mouthExpressions.Add(expression);
                }
            }
        }

        AddMorphItems(_eyeExpressionMenu, eyeExpressions);
        AddMorphItems(_mouthExpressionMenu, mouthExpressions);
        AddMorphItems(_clothingMenu, clothing);
        AddMorphItems(_adultMenu, adult);

        _eyeExpressionMenu.Visibility = eyeExpressions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _mouthExpressionMenu.Visibility = mouthExpressions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _clothingMenu.Visibility = clothing.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _adultMenu.Visibility = adult.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        // Keep the diagnostic catch-all list out of the normal pet menu. It
        // duplicates the curated categories and makes a single radio choice
        // look like several independent controls.
        _allMorphsMenu.Visibility = Visibility.Collapsed;
    }

    private void AddMorphItems(MenuItem parent, IEnumerable<(int Index, string Name)> morphs)
    {
        foreach (var morph in morphs)
        {
            var item = new MenuItem
            {
                Header = morph.Name,
                Tag = morph.Index,
                IsCheckable = false,
                StaysOpenOnClick = true,
            };
            ConfigureMenuItem(item);
            SetRadioIndicator(item, selected: false);
            item.Click += OnMorphMenuClick;
            parent.Items.Add(item);
            _morphMenuItems[morph.Index] = item;
        }
    }

    private async void OnMorphMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not int index)
        {
            return;
        }

        await ApplyMorphSelectionAsync(item, index);
    }

    internal bool FullNudeActiveForTest => _fullNudeActive;
    internal int ActiveEyeExpressionCountForTest =>
        _activeMorphBySubmenu.ContainsKey(_eyeExpressionMenu) ? 1 : 0;
    internal int? ActiveEyeExpressionIndexForTest =>
        _activeMorphBySubmenu.TryGetValue(_eyeExpressionMenu, out var index) ? index : null;
    internal int ActiveMouthExpressionCountForTest =>
        _activeMorphBySubmenu.ContainsKey(_mouthExpressionMenu) ? 1 : 0;
    internal int? ActiveMouthExpressionIndexForTest =>
        _activeMorphBySubmenu.TryGetValue(_mouthExpressionMenu, out var index) ? index : null;
    internal bool IsCuratedEyeMorphForTest(int index)
    {
        return _morphMenuItems.TryGetValue(index, out var item)
            && ReferenceEquals(GetMorphSubmenu(item), _eyeExpressionMenu);
    }
    internal bool IsCuratedMouthMorphForTest(int index)
    {
        return _morphMenuItems.TryGetValue(index, out var item)
            && ReferenceEquals(GetMorphSubmenu(item), _mouthExpressionMenu);
    }

    internal async Task SelectMorphForTestAsync(int index)
    {
        if (!_morphMenuItems.TryGetValue(index, out var item))
        {
            throw new InvalidDataException($"Morph index {index} was not present in the curated menu.");
        }

        await ApplyMorphSelectionAsync(item, index);
    }

    private async Task ApplyMorphSelectionAsync(MenuItem item, int index)
    {
        if (ReferenceEquals(item, _nakedMenu))
        {
            await ApplyFullNudeSelectionAsync(index);
            return;
        }

        var submenu = GetMorphSubmenu(item);
        if (submenu is null)
        {
            return;
        }

        var hadPrevious = _activeMorphBySubmenu.TryGetValue(submenu, out var previousIndex);
        var selectingClothing = ReferenceEquals(submenu, _clothingMenu);
        var wasFullNudeActive = _fullNudeActive;
        var nakedMorphIndex = _nakedMenu.Tag is int value ? value : -1;

        try
        {
            if (selectingClothing && _fullNudeActive && nakedMorphIndex >= 0)
            {
                await SetMorphAsync(nakedMorphIndex, 0.0);
                _fullNudeActive = false;
            }

            if (hadPrevious && previousIndex != index)
            {
                await SetMorphAsync(previousIndex, 0.0);
            }

            await SetMorphAsync(index, 1.0);
            _activeMorphBySubmenu[submenu] = index;
            UpdateMorphRadioIndicators();
        }
        catch (Exception exception)
        {
            _fullNudeActive = wasFullNudeActive;
            if (hadPrevious && previousIndex != index)
            {
                try
                {
                    await SetMorphAsync(previousIndex, 1.0);
                }
                catch
                {
                    // Keep the original runtime error as the user-facing failure.
                }
            }

            if (selectingClothing && wasFullNudeActive && nakedMorphIndex >= 0)
            {
                try
                {
                    await SetMorphAsync(nakedMorphIndex, 1.0);
                }
                catch
                {
                    // Keep the original runtime error as the user-facing failure.
                }
            }

            UpdateMorphRadioIndicators();
            _lastRuntimeError = exception.ToString();
        }
    }

    private async Task ApplyFullNudeSelectionAsync(int index)
    {
        var hadClothing = _activeMorphBySubmenu.TryGetValue(_clothingMenu, out var clothingIndex);
        var wasFullNudeActive = _fullNudeActive;
        try
        {
            if (hadClothing)
            {
                await SetMorphAsync(clothingIndex, 0.0);
                _activeMorphBySubmenu.Remove(_clothingMenu);
            }

            await SetMorphAsync(index, 1.0);
            _fullNudeActive = true;
            UpdateMorphRadioIndicators();
        }
        catch (Exception exception)
        {
            _fullNudeActive = wasFullNudeActive;
            if (hadClothing)
            {
                try
                {
                    await SetMorphAsync(clothingIndex, 1.0);
                    _activeMorphBySubmenu[_clothingMenu] = clothingIndex;
                }
                catch
                {
                    // Keep the original runtime error as the user-facing failure.
                }
            }

            UpdateMorphRadioIndicators();
            _lastRuntimeError = exception.ToString();
        }
    }

    private MenuItem? GetMorphSubmenu(MenuItem item)
    {
        if (_eyeExpressionMenu.Items.Contains(item))
        {
            return _eyeExpressionMenu;
        }

        if (_mouthExpressionMenu.Items.Contains(item))
        {
            return _mouthExpressionMenu;
        }

        if (_clothingMenu.Items.Contains(item))
        {
            return _clothingMenu;
        }

        if (_adultMenu.Items.Contains(item))
        {
            return _adultMenu;
        }

        return null;
    }

    private void OnSwitchCharacterMenuClick(object? sender, RoutedEventArgs e)
    {
        _ = SwitchCharacterAsync();
    }

    private async void OnDanceMotionMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not DanceMotionDefinition dance)
        {
            return;
        }

        var previousDance = _activeDanceMenuItem;
        foreach (var danceItem in _danceMenuItems)
        {
            SetRadioIndicator(danceItem, ReferenceEquals(danceItem, item));
        }

        _danceMenu.IsEnabled = false;
        try
        {
            JsonElement motion;
            if (dance.IsDual)
            {
                var secondaryPath = GetSecondaryDanceMotionPath(dance)
                    ?? throw new InvalidDataException("The dual dance has no secondary VMD path.");
                if (!_dualMode)
                {
                    await LoadDualCharactersAsync(0, 1);
                }

                motion = await LoadDualMotionAsync(
                    dance.Key,
                    GetDanceMotionPath(dance),
                    secondaryPath,
                    dance.StartFrame);
                if (motion.GetProperty("primaryBoneTracks").GetInt32() <= 0
                    || motion.GetProperty("secondaryBoneTracks").GetInt32() <= 0)
                {
                    throw new InvalidDataException("The selected dual VMD motion contained no bone tracks.");
                }
            }
            else
            {
                if (_dualMode)
                {
                    await LoadCharacterAsync(_currentCharacter);
                }

                motion = await LoadMotionAsync(
                    dance.Key,
                    GetDanceMotionPath(dance),
                    dance.Loop);
                if (motion.GetProperty("boneTracks").GetInt32() <= 0)
                {
                    throw new InvalidDataException("The selected VMD contained no bone tracks.");
                }
            }

            await LoadDanceMusicAsync(dance);
            await PlayAsync();
            _activeDanceMenuItem = item;
            _pauseDanceMenu.IsEnabled = true;
        }
        catch (Exception exception)
        {
            SetRadioIndicator(item, selected: false);
            if (previousDance is not null)
            {
                SetRadioIndicator(previousDance, selected: true);
            }

            _activeDanceMenuItem = previousDance;
            _lastRuntimeError = exception.ToString();
        }
        finally
        {
            _danceMenu.IsEnabled = true;
        }
    }

    private async void OnPauseDanceMenuClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await PauseAsync();
        }
        catch (Exception exception)
        {
            _lastRuntimeError = exception.ToString();
        }
    }

    private async Task SwitchCharacterAsync()
    {
        try
        {
            await LoadCharacterAsync((_currentCharacter + 1) % _modelPaths.Length);
        }
        catch (Exception exception)
        {
            _lastRuntimeError = exception.ToString();
        }
    }

    private async void OnResetMorphsMenuClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await ResetMorphStateAsync();
        }
        catch (Exception exception)
        {
            _lastRuntimeError = exception.ToString();
        }
    }

    private void OnAlwaysOnTopMenuClick(object? sender, RoutedEventArgs e)
    {
        Topmost = _alwaysOnTopMenu.IsChecked;
    }

    private void OnCloseMenuClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _closing)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The pointer can be released between the preview event and DragMove.
        }

        e.Handled = true;
    }

    private void OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_closing)
        {
            return;
        }

        OpenPetMenu();
        e.Handled = true;
    }

    private void OpenPetMenu()
    {
        if (!IsLoaded || _closing)
        {
            return;
        }

        _petMenu.PlacementTarget = this;
        _petMenu.IsOpen = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void ActivatePetFocus()
    {
        Focus();
        Keyboard.Focus(this);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Trace("closed");
        _closing = true;
        _petMenu.IsOpen = false;
        if (Browser is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private string ToRuntimeUrl(string absolutePath)
    {
        var relativePath = Path.GetRelativePath(_repositoryRoot, absolutePath).Replace('\\', '/');
        var encodedPath = string.Join(
            "/",
            relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));
        return "https://hime.local/" + encodedPath;
    }

    private string ResolveModelPath(string modelDirectory, string fileName)
    {
        var portablePath = Path.Combine(
            _repositoryRoot,
            "assets",
            "Hime_&_Mikoto",
            modelDirectory,
            fileName);
        if (File.Exists(portablePath))
        {
            return portablePath;
        }

        return Path.Combine(
            _repositoryRoot,
            "HimeMikotoDesktop",
            "assets",
            "Hime_&_Mikoto",
            modelDirectory,
            fileName);
    }

    private string ResolveProjectPath(string relativePath)
    {
        var sourceLayoutPath = Path.Combine(_repositoryRoot, relativePath);
        if (File.Exists(sourceLayoutPath) || Directory.Exists(sourceLayoutPath))
        {
            return sourceLayoutPath;
        }

        const string nativeProjectPrefix = "HimeMikotoDesktopNative";
        if (relativePath.Equals(nativeProjectPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return _repositoryRoot;
        }

        var portableRelativePath = relativePath.StartsWith(
            nativeProjectPrefix + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase)
            ? relativePath[(nativeProjectPrefix.Length + 1)..]
            : relativePath.StartsWith(
                nativeProjectPrefix + Path.AltDirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
                ? relativePath[(nativeProjectPrefix.Length + 1)..]
                : relativePath;
        return Path.Combine(_repositoryRoot, portableRelativePath);
    }

    private string Text(string chinese, string english)
    {
        return _english ? english : chinese;
    }

    private static void ClearMenu(MenuItem menu)
    {
        menu.Items.Clear();
    }

    private async Task ApplyFullNudeIfNeededAsync()
    {
        if (!_fullNudeActive || _nakedMenu.Tag is not int nakedMorphIndex)
        {
            return;
        }

        await SetMorphAsync(nakedMorphIndex, 1.0);
        SetRadioIndicator(_nakedMenu, selected: true);
    }

    internal Task ClearMorphStateForTestAsync()
    {
        return ResetMorphStateAsync();
    }

    private async Task ResetMorphStateAsync()
    {
        _fullNudeActive = false;
        _activeMorphBySubmenu.Clear();
        foreach (var item in _morphMenuItems.Values.Distinct())
        {
            SetRadioIndicator(item, selected: false);
        }

        await ResetMorphsAsync();
    }

    private void UpdateMorphRadioIndicators()
    {
        foreach (var item in _morphMenuItems.Values.Distinct())
        {
            SetRadioIndicator(item, IsActiveMorphItem(item));
        }
    }

    private bool IsActiveMorphItem(MenuItem item)
    {
        if (ReferenceEquals(item, _nakedMenu))
        {
            return _fullNudeActive;
        }

        if (item.Tag is not int index)
        {
            return false;
        }

        var submenu = GetMorphSubmenu(item);
        return submenu is not null
            && _activeMorphBySubmenu.TryGetValue(submenu, out var activeIndex)
            && activeIndex == index;
    }

    private void AddDisabledItem(MenuItem parent, string header)
    {
        var item = new MenuItem
        {
            Header = header,
            IsEnabled = false,
        };
        ConfigureMenuItem(item);
        parent.Items.Add(item);
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        return tokens.Any(value.Contains);
    }

    private static void SetRadioIndicator(MenuItem item, bool selected)
    {
        if (item.Icon is not TextBlock indicator)
        {
            indicator = new TextBlock
            {
                Width = 18,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI Symbol"),
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(125, 49, 94)),
            };
            item.Icon = indicator;
        }

        indicator.Text = selected ? "●" : "○";
    }

    private static SolidColorBrush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private void ConfigureMenuItem(MenuItem item)
    {
        item.PreviewMouseMove += OnMenuItemPreviewMouseMove;
        item.MouseEnter += OnMenuItemMouseEnter;
        item.MouseLeave += OnMenuItemMouseLeave;
        item.SubmenuOpened += OnMenuItemSubmenuOpened;
        item.SubmenuClosed += OnMenuItemSubmenuClosed;
    }

    private static void OnMenuItemMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is MenuItem item && item.IsEnabled)
        {
            ApplyMenuHover(item);
        }
    }

    private void OnMenuItemPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is MenuItem item && item.IsEnabled)
        {
            if (!ReferenceEquals(_hoveredMenuItem, item))
            {
                if (_hoveredMenuItem is not null)
                {
                    ClearMenuHover(_hoveredMenuItem);
                }

                _hoveredMenuItem = item;
            }

            ApplyMenuHover(item);
        }
    }

    private static void OnMenuItemMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is MenuItem item && !item.IsSubmenuOpen)
        {
            ClearMenuHover(item);
        }
    }

    private static void OnMenuItemSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.IsEnabled)
        {
            ApplyMenuHover(item);
        }
    }

    private static void OnMenuItemSubmenuClosed(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && !item.IsMouseOver)
        {
            ClearMenuHover(item);
        }
    }

    private static void ApplyMenuHover(MenuItem item)
    {
        item.Background = MenuHoverBackground;
        item.Foreground = MenuHoverForeground;
    }

    private static void ClearMenuHover(MenuItem item)
    {
        item.ClearValue(MenuItem.BackgroundProperty);
        item.ClearValue(MenuItem.ForegroundProperty);
    }

    private void OnPetMenuOpened(object? sender, RoutedEventArgs e)
    {
        _hoveredMenuItem = null;
    }

    private void OnPetMenuClosed(object? sender, RoutedEventArgs e)
    {
        if (_hoveredMenuItem is not null)
        {
            ClearMenuHover(_hoveredMenuItem);
            _hoveredMenuItem = null;
        }
    }

    private static bool IsInternalMorph(string name, string englishName)
    {
        return name.StartsWith("├", StringComparison.Ordinal)
            || name.StartsWith("└", StringComparison.Ordinal)
            || name.Contains("__", StringComparison.Ordinal)
            || englishName.Contains("__", StringComparison.Ordinal);
    }

    private string MorphHeader(string name, string englishName)
    {
        if (_english)
        {
            return string.IsNullOrWhiteSpace(englishName) ? name : englishName;
        }

        if (ChineseMorphNames.TryGetValue(englishName, out var chineseName))
        {
            return chineseName;
        }

        if (!string.IsNullOrWhiteSpace(englishName)
            && englishName.Any(char.IsLetter))
        {
            return englishName
                .Replace("x_", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("__", string.Empty, StringComparison.Ordinal);
        }

        return name;
    }

    private static bool IsFullNudeMorph(string name, string englishName)
    {
        return ContainsAny(
            name.ToLowerInvariant(),
            "全裸",
            "naked",
            "nude")
            || ContainsAny(englishName.ToLowerInvariant(), "castoff", "naked", "nude");
    }

    private static bool IsVisibleExpressionMorph(string name, string englishName)
    {
        return VisibleExpressionMorphNames.Contains(englishName)
            || (string.IsNullOrWhiteSpace(englishName) && VisibleExpressionMorphNames.Contains(name));
    }

    private static bool IsEyeExpressionMorph(string name, string englishName)
    {
        return EyeExpressionMorphNames.Contains(englishName)
            || (string.IsNullOrWhiteSpace(englishName) && EyeExpressionMorphNames.Contains(name));
    }

    private static bool IsUsefulAdultMorph(string name, string englishName, bool isClothing)
    {
        if (isClothing)
        {
            return true;
        }

        var combined = $"{name.ToLowerInvariant()} {englishName.ToLowerInvariant()}";
        return ContainsAny(
            combined,
            "口淫",
            "blowjob",
            "乳首",
            "nipple",
            "腹ボコ",
            "腹パン",
            "腹ゴリ",
            "boko",
            "陰唇開",
            "labopen",
            "vagopen",
            "ｱﾅﾙ開",
            "アナル開",
            "anaopen");
    }

    private static bool IsChineseSystemLanguage()
    {
        try
        {
            const uint muiLanguageName = 0x8;
            uint languageCount = 0;
            uint bufferLength = 0;
            if (GetUserPreferredUILanguages(
                    muiLanguageName,
                    ref languageCount,
                    null,
                    ref bufferLength)
                && bufferLength > 0)
            {
                var buffer = new StringBuilder((int)bufferLength);
                if (GetUserPreferredUILanguages(
                        muiLanguageName,
                        ref languageCount,
                        buffer,
                        ref bufferLength))
                {
                    var preferredLanguage = buffer
                        .ToString()
                        .Split('\0', StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(preferredLanguage))
                    {
                        return preferredLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
        }
        catch (DllNotFoundException)
        {
            // Non-Windows test hosts use the culture fallback below.
        }
        catch (EntryPointNotFoundException)
        {
            // Older Windows versions use the culture fallback below.
        }

        var installedLanguage = CultureInfo.InstalledUICulture.Name;
        if (!string.IsNullOrWhiteSpace(installedLanguage))
        {
            return installedLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        }

        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            .Equals("zh", StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserPreferredUILanguages(
        uint dwFlags,
        ref uint pulNumLanguages,
        StringBuilder? pwszLanguagesBuffer,
        ref uint pcchLanguagesBuffer);

    private void Trace(string message)
    {
        try
        {
            File.AppendAllText(
                TracePath,
                $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never interfere with the desktop pet.
        }
    }

    private static string FindRepositoryRoot()
    {
        var startingPoints = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
        };

        foreach (var startingPoint in startingPoints)
        {
            var directory = new DirectoryInfo(startingPoint);
            for (var depth = 0; depth < 12 && directory != null; depth++, directory = directory.Parent)
            {
                var portableWebIndex = Path.Combine(directory.FullName, "web", "index.html");
                var portableAssetMarker = Path.Combine(
                    directory.FullName,
                    "assets",
                    "Hime_&_Mikoto");
                var portablePackageMarker = Path.Combine(
                    directory.FullName,
                    "使用说明.txt");
                if (File.Exists(portableWebIndex)
                    && (Directory.Exists(portableAssetMarker)
                        || File.Exists(portablePackageMarker)))
                {
                    return directory.FullName;
                }

                var nativeProject = Path.Combine(directory.FullName, "HimeMikotoDesktopNative");
                var assetMarker = Path.Combine(
                    directory.FullName,
                    "HimeMikotoDesktop",
                    "assets",
                    "Hime_&_Mikoto");
                if (Directory.Exists(nativeProject) && Directory.Exists(assetMarker))
                {
                    return directory.FullName;
                }

                var localWeb = Path.Combine(directory.FullName, "web");
                var parentAssetMarker = directory.Parent == null
                    ? string.Empty
                    : Path.Combine(directory.Parent.FullName, "HimeMikotoDesktop", "assets", "Hime_&_Mikoto");
                if (Directory.Exists(localWeb) && Directory.Exists(parentAssetMarker))
                {
                    return directory.Parent!.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("The Hime/Mikoto project root could not be located.");
    }
}
