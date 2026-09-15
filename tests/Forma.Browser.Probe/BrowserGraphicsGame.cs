using Forma;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

internal sealed class BrowserGraphicsGame : Game
{
    private readonly IReadOnlyDictionary<string, byte[]> _fontBytes;
    private readonly Func<string, Label> _createView;
    private UIContext _ui = new();
    private readonly List<UIFontFace> _faces = [];
    private readonly List<Label> _labels = [];
    private readonly Color _background = new(12, 18, 24);
    private int _frames;
    private long[]? _fullInk;
    public string Result { get; private set; } = "";

    public BrowserGraphicsGame(IReadOnlyDictionary<string, byte[]> fontBytes, Func<string, Label> createView)
    {
        _fontBytes = fontBytes;
        _createView = createView;
        _ = new GraphicsDeviceManager(this) { PreferredBackBufferWidth = 720, PreferredBackBufferHeight = 420 };
        IsFixedTimeStep = false;
    }

    protected override void LoadContent()
    {
        var samples = new Dictionary<string, string>
        {
            ["en"] = "Hello, Luna! café", ["ar"] = "مَرْحَبًا يا لونا!",
            ["es-419"] = "¡Hola, Luna! ¿Cómo estás?", ["ja"] = "こんにちは、ルナ！友達",
            ["ko"] = "안녕, 루나! 친구", ["pt-BR"] = "Olá, Luna! coração",
            ["zh-Hans"] = "你好，露娜！朋友"
        };
        foreach (var (locale, sample) in samples)
        {
            var face = UIFontFace.FromMemory(_fontBytes[locale]);
            _faces.Add(face);
            var label = _createView(sample);
            if (label.Text != sample) throw new InvalidOperationException($"{locale}: compiled typed binding did not populate text.");
            label.UIFont = new DynamicUIFont(face, 28, UIFontHinting.None);
            label.Language = locale;
            label.Position = new Vector2(12, 8 + _labels.Count * 56);
            label.Size = new Vector2(660, 48);
            _labels.Add(label);
            _ui.Add(label);
        }
        _ui.ViewportSize = new Vector2(720, 420);
    }

    protected override void Update(GameTime gameTime) => _ui.Update(gameTime, default, default);

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(_background);
        _ui.Draw(GraphicsDevice);
        if (++_frames < 3 || Result.Length > 0) return;
        var pixels = new Color[720 * 420];
        GraphicsDevice.GetBackBufferData(pixels);
        var counts = new long[7];
        for (var y = 0; y < 420; y++)
            for (var x = 0; x < 720; x++)
            {
                var pixel = pixels[y * 720 + x];
                if (pixel == _background) continue;
                if (x >= 684) throw new InvalidOperationException("Forma text escaped its clipping bounds.");
                if (_frames is 8 or 9 && x >= 52) throw new InvalidOperationException("Forma text ignored the narrowed clip.");
                var row = (y - 8) / 56;
                if (y < 8 || row >= counts.Length) throw new InvalidOperationException("Forma rendered outside the expected label rows.");
                counts[row]++;
            }
        if (_frames == 3)
        {
            if (counts.Any(count => count < 20)) throw new InvalidOperationException("One or more locales produced no real GPU glyph coverage: " + string.Join(",", counts));
            _fullInk = counts;
            foreach (var label in _labels) label.VisibleCharacters = 1;
        }
        else if (_frames == 5)
        {
            if (counts.Where((count, index) => count <= 0 || count >= _fullInk![index]).Any())
                throw new InvalidOperationException("Post-shaping reveal did not reduce GPU coverage for every locale.");
            foreach (var label in _labels) label.VisibleCharacters = -1;
        }
        else if (_frames == 7)
        {
            if (!counts.SequenceEqual(_fullInk!)) throw new InvalidOperationException("Warm glyph atlas/reveal restoration changed GPU output.");
            foreach (var label in _labels) label.Size = new Vector2(40, 48);
        }
        else if (_frames == 9)
        {
            if (counts.Where((count, index) => count <= 0 || count >= _fullInk![index]).Any())
                throw new InvalidOperationException("Narrowing text clips did not reduce GPU coverage for every locale.");
            foreach (var label in _labels) label.Size = new Vector2(660, 48);
        }
        else if (_frames == 11)
        {
            if (!counts.SequenceEqual(_fullInk!)) throw new InvalidOperationException("Clip restoration changed GPU output.");
            _ui.Dispose();
            foreach (var face in _faces) face.Dispose();
            _faces.Clear();
            _labels.Clear();
            _ui = new UIContext();
            LoadContent();
        }
        else if (_frames == 14)
        {
            if (!counts.SequenceEqual(_fullInk!)) throw new InvalidOperationException("Reloading identical font bytes reused disposed faces or changed GPU output.");
            Result = "PASS Forma GPU compiled-XAML + seven-locale Alpha8 + reveal + clipping + disposed font-pack reload: " + string.Join(",", counts);
            Console.WriteLine(Result);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ui.Dispose();
            foreach (var face in _faces) face.Dispose();
        }
        base.Dispose(disposing);
    }
}
