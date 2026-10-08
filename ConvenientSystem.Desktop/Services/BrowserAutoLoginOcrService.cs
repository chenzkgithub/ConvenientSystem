using System.Drawing.Drawing2D;
using Microsoft.Extensions.Logging;
using Tesseract;

namespace ConvenientSystem;

/// <summary>使用 Tesseract 在本机识别 ERP 四位图片验证码。</summary>
public sealed class BrowserAutoLoginOcrService : IDisposable
{
    private const string AllowedCharacters = "2345689abdefhkmnrxyABCDEFGHJKLMNPRSTWXY";
    private const string AllowedCharactersLower = "2345689abcdefghjklmnprstwxy";

    private readonly TesseractEngine? _engine;
    private readonly ILogger<BrowserAutoLoginOcrService> _logger;
    private readonly SemaphoreSlim _engineLock = new(1, 1);

    public BrowserAutoLoginOcrService(ILogger<BrowserAutoLoginOcrService> logger)
    {
        _logger = logger;
        try
        {
            var dataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
            TesseractEnviornment.CustomSearchPath = AppContext.BaseDirectory;
            _engine = new TesseractEngine(dataPath, "eng", EngineMode.Default);
            _engine.SetVariable("tessedit_char_whitelist", AllowedCharacters);
            _engine.SetVariable("load_system_dawg", false);
            _engine.SetVariable("load_freq_dawg", false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "初始化本地验证码识别引擎失败，将使用人工输入");
        }
    }

    public bool IsAvailable => _engine is not null;

    public async Task<IReadOnlyList<string>> RecognizeCandidatesAsync(
        string imageBase64,
        CancellationToken cancellationToken)
    {
        if (_engine is null) return [];

        try
        {
            var sourceBytes = Convert.FromBase64String(imageBase64);
            await _engineLock.WaitAsync(cancellationToken);
            try
            {
                return await Task.Run(() => RecognizeCandidates(sourceBytes, cancellationToken), cancellationToken);
            }
            finally
            {
                _engineLock.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "本地 OCR 未能识别 ERP 验证码");
            return [];
        }
    }

    private IReadOnlyList<string> RecognizeCandidates(byte[] sourceBytes, CancellationToken cancellationToken)
    {
        var variants = new (bool Threshold, bool Crop, PageSegMode Mode)[]
        {
            (false, true, PageSegMode.SingleWord),
            (true, true, PageSegMode.SingleWord),
            (true, true, PageSegMode.RawLine),
            (true, true, PageSegMode.SingleLine),
            (false, false, PageSegMode.SingleLine),
            (true, false, PageSegMode.SingleLine),
        };

        var candidates = new List<string>();
        foreach (var variant in variants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var preparedBytes = PrepareImage(sourceBytes, variant.Threshold, variant.Crop);
            using var pix = Pix.LoadFromMemory(preparedBytes);
            using var page = _engine!.Process(pix, variant.Mode);
            var candidate = new string(page.GetText().Where(char.IsLetterOrDigit).ToArray());
            if (candidate.Length != 4 ||
                candidate.Any(c => !AllowedCharactersLower.Contains(char.ToLowerInvariant(c))) ||
                candidates.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            candidates.Add(candidate);
        }
        return candidates;
    }

    private static byte[] PrepareImage(byte[] sourceBytes, bool threshold, bool crop)
    {
        using var sourceStream = new MemoryStream(sourceBytes);
        using var source = new Bitmap(sourceStream);
        var sourceRect = crop
            ? new Rectangle(0, 0, Math.Min(100, source.Width), Math.Min(28, source.Height))
            : new Rectangle(0, 0, source.Width, source.Height);
        using var prepared = new Bitmap(sourceRect.Width * 5 + 40, sourceRect.Height * 5 + 40);
        using (var graphics = Graphics.FromImage(prepared))
        {
            graphics.Clear(Color.White);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(
                source,
                new Rectangle(20, 20, sourceRect.Width * 5, sourceRect.Height * 5),
                sourceRect,
                GraphicsUnit.Pixel);
        }

        if (threshold)
        {
            for (var y = 0; y < prepared.Height; y++)
            {
                for (var x = 0; x < prepared.Width; x++)
                {
                    var pixel = prepared.GetPixel(x, y);
                    var isForeground = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B)) < 220;
                    prepared.SetPixel(x, y, isForeground ? Color.Black : Color.White);
                }
            }
        }

        using var output = new MemoryStream();
        prepared.Save(output, System.Drawing.Imaging.ImageFormat.Png);
        return output.ToArray();
    }

    public void Dispose()
    {
        _engine?.Dispose();
        _engineLock.Dispose();
    }
}
