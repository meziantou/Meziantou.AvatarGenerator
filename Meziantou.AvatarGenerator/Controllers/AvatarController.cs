using System;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Meziantou.AvatarGenerator.Controllers
{
    [Route("[controller]")]
    public class AvatarController : Controller
    {
        private Color[] _backgroundColors;
        private Regex _colorRegex = new Regex("[0-9A-Fa-f]", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
        private readonly IMemoryCache _cache;
        private readonly ILogger<AvatarController> _logger;

        public AvatarController(IMemoryCache cache, ILogger<AvatarController> logger)
        {
            if (cache == null) throw new ArgumentNullException(nameof(cache));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            string[] colors = { "#1abc9c", "#2ecc71", "#3498db", "#9b59b6", "#34495e", "#16a085", "#27ae60", "#2980b9", "#8e44ad", "#2c3e50", "#f1c40f", "#e67e22", "#e74c3c", "#95a5a6", "#f39c12", "#d35400", "#c0392b", "#bdc3c7", "#7f8c8d" };
            _backgroundColors = colors.Select(c => { TryParseColor(c, out var result); return result; }).ToArray();
            _cache = cache;
            _logger = logger;
        }
        
        [HttpGet]
        [Route("")]
        [Route("{name}.{outputformat}")]
        public IActionResult Get(
            [FromRoute(Name = "name")][FromQuery(Name = "n")][Required]string name,
            [FromQuery(Name = "s")][Range(16, 1024)]int size = 64,
            [FromQuery(Name = "bg")]string backgroundColor = null,
            [FromQuery(Name = "fg")]string foregroundColor = null,
            [FromRoute(Name = "outputformat")][FromQuery(Name = "o")]OutputFormat outputFormat = OutputFormat.Png,
            [FromQuery(Name = "f")]BackgroundFormat backgroundFormat = BackgroundFormat.Square)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var text = GetInitials(name) ?? string.Empty;

            if (!TryParseColor(foregroundColor, out var fg))
            {
                fg = Color.White;
            }

            if (!TryParseColor(backgroundColor, out var bg))
            {
                int index = text.Select(c => (int)c).Sum();
                bg = _backgroundColors[index % _backgroundColors.Length];
            }

            if (!TryParseImageFormat(outputFormat, out var imageFormat))
            {
                imageFormat = ImageFormat.Png;
            }

            var options = new GenerateOptions(text, size, fg, bg, backgroundFormat, imageFormat);
            var cacheKey = options.ComputeCacheKey();
            if (!_cache.TryGetValue(cacheKey, out byte[] result))
            {
                _logger.LogInformation("Generating image: " + options.ToString());
                var bytes = DrawTextImage(options);
                result = _cache.Set(cacheKey, bytes);
            }

            return File(result, options.GetMimeType());
        }

        private string GetInitials(string text)
        {
            if (text == null)
                return null;

            var initials = text.Split(new[] { ' ', '-', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(c => char.ToUpperInvariant(c[0])).Take(2).ToArray();
            return new string(initials);
        }

        private bool TryParseColor(string value, out Color color)
        {
            color = Color.Empty;

            if (value == null)
                return false;

            if (value.StartsWith("#"))
            {
                try
                {
                    color = ColorTranslator.FromHtml(value);
                    if (!color.IsEmpty)
                        return true;
                }
                catch
                {
                }
            }

            if (_colorRegex.IsMatch(value))
            {
                try
                {
                    color = ColorTranslator.FromHtml("#" + value);
                    if (!color.IsEmpty)
                        return true;
                }
                catch
                {
                }
            }

            if (_colorRegex.IsMatch(value))
            {
                try
                {
                    color = ColorTranslator.FromHtml(value);
                    if (!color.IsEmpty)
                        return true;
                }
                catch
                {
                }
            }

            return false;
        }

        private bool TryParseImageFormat(OutputFormat value, out ImageFormat imageFormat)
        {
            switch (value)
            {
                case OutputFormat.Png:
                    imageFormat = ImageFormat.Png;
                    return true;

                case OutputFormat.Bmp:
                    imageFormat = ImageFormat.Bmp;
                    return true;

                case OutputFormat.Gif:
                    imageFormat = ImageFormat.Gif;
                    return true;

                case OutputFormat.Jpeg:
                    imageFormat = ImageFormat.Jpeg;
                    return true;

                case OutputFormat.Tiff:
                    imageFormat = ImageFormat.Tiff;
                    return true;
            }

            imageFormat = ImageFormat.Bmp;
            return false;
        }

        private byte[] DrawTextImage(GenerateOptions options)
        {
            var fontSize = 0.4f * options.Size;
            using (var font = new Font("Arial", fontSize))
            using (var img = new Bitmap(options.Size, options.Size))
            {
                using (var drawing = Graphics.FromImage(img))
                {
                    drawing.TextRenderingHint = TextRenderingHint.AntiAlias;
                    drawing.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                    drawing.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    drawing.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

                    drawing.Clear(Color.Transparent);

                    switch (options.BackgroundFormat)
                    {

                        case BackgroundFormat.Circle:
                            using (var backgroundBrush = new SolidBrush(options.BackgroundColor))
                            {
                                drawing.FillEllipse(backgroundBrush, 0, 0, options.Size, options.Size);
                            }
                            break;

                        case BackgroundFormat.Square:
                        default:
                            using (var backgroundBrush = new SolidBrush(options.BackgroundColor))
                            {
                                drawing.FillRectangle(backgroundBrush, 0, 0, options.Size, options.Size);
                            }
                            break;
                    }

                    using (Brush textBrush = new SolidBrush(options.ForegroundColor))
                    {
                        var size = drawing.MeasureString(options.Text, font);
                        drawing.DrawString(options.Text, font, textBrush, (options.Size - size.Width) / 2, (options.Size - size.Height) / 2);
                        drawing.Save();
                    }
                }

                using (var ms = new MemoryStream())
                {
                    img.Save(ms, options.ImageFormat);
                    ms.Seek(0, SeekOrigin.Begin);
                    return ms.ToArray();
                }
            }
        }

        public enum OutputFormat
        {
            Png,
            Bmp,
            Gif,
            Jpeg,
            Jpg = Jpeg,
            Tiff
        }

        public enum BackgroundFormat
        {
            Square,
            Circle
        }

        private struct GenerateOptions
        {
            public GenerateOptions(string text, int size, Color foreground, Color background, BackgroundFormat backgroundFormat, ImageFormat format)
            {
                Text = text;
                Size = size;
                ForegroundColor = foreground;
                BackgroundColor = background;
                ImageFormat = format;
                BackgroundFormat = backgroundFormat;
            }

            public string Text { get; }
            public int Size { get; }
            public Color ForegroundColor { get; }
            public Color BackgroundColor { get; }
            public ImageFormat ImageFormat { get; }
            public BackgroundFormat BackgroundFormat { get; }

            public string ComputeCacheKey()
            {
                return Text + ImageFormat.Guid.ToString("N") + ((int)BackgroundFormat).ToString(CultureInfo.InvariantCulture) + Size.ToString(CultureInfo.InvariantCulture) + ForegroundColor.ToArgb().ToString(CultureInfo.InvariantCulture) + BackgroundColor.ToArgb().ToString(CultureInfo.InvariantCulture);
            }

            public string GetMimeType()
            {
                var guid = ImageFormat.Guid;
                ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
                return codecs.First(codec => codec.FormatID == guid).MimeType;
            }

            public override string ToString()
            {
                return $"Text: {Text}; BackgroundFormat: {BackgroundFormat} Size: {Size}; ImageFormat: {GetMimeType()}; Fg: {ForegroundColor.Name}; Bg: {BackgroundColor.Name}";
            }
        }
    }
}
