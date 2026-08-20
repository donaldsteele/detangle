using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Svg.Skia;
using Detangle.Core.Vault;

namespace Detangle.Rendering.Controls;

/// <summary>Loads an attachment into something Avalonia can draw.</summary>
public interface IImageLoader
{
    /// <summary>Returns the image, or null when it cannot be loaded.</summary>
    IImage? Load(VaultDocument document);
}

/// <summary>
/// Loads images from disk, caching by path. SVG goes through Skia's SVG control rather
/// than the bitmap decoder (plan.md section 11) — the same path phase 3's rendered
/// diagrams will take.
/// </summary>
public sealed class FileImageLoader : IImageLoader
{
    private static readonly string[] VectorExtensions = [".svg", ".svgz"];

    private readonly Dictionary<string, IImage?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    /// <summary>A shared loader; the cache is keyed by absolute path.</summary>
    public static FileImageLoader Instance { get; } = new();

    /// <inheritdoc />
    public IImage? Load(VaultDocument document)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(document.AbsolutePath, out IImage? cached))
            {
                return cached;
            }

            IImage? image = LoadCore(document);
            _cache[document.AbsolutePath] = image;

            return image;
        }
    }

    /// <summary>Forgets cached images, so an edited attachment is picked up.</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _cache.Clear();
        }
    }

    private static IImage? LoadCore(VaultDocument document)
    {
        try
        {
            if (!File.Exists(document.AbsolutePath))
            {
                return null;
            }

            if (VectorExtensions.Contains(document.Extension, StringComparer.OrdinalIgnoreCase))
            {
                var svg = new SvgImage { Source = SvgSource.Load(document.AbsolutePath, null) };

                return svg.Source is null ? null : svg;
            }

            return new Bitmap(document.AbsolutePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException or InvalidOperationException)
        {
            // A broken or unsupported attachment renders as its alt text; it is never a
            // reason for the page around it to fail.
            return null;
        }
    }
}
