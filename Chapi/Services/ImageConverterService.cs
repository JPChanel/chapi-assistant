using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using System.IO;

namespace Chapi.Services;

public class ImageConverterService
{
    public class ConversionResult
    {
        public string SourcePath { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        public long OriginalSize { get; set; }
        public long ConvertedSize { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        
        public double CompressionRatio => OriginalSize > 0 
            ? Math.Round((1 - (double)ConvertedSize / OriginalSize) * 100, 2) 
            : 0;
    }

    private static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg" };

    /// <summary>
    /// Convierte una imagen individual a formato WebP
    /// </summary>
    public async Task<ConversionResult> ConvertImageAsync(
        string sourcePath, 
        string outputDirectory, 
        int quality = 85,
        IProgress<string>? progress = null)
    {
        var result = new ConversionResult
        {
            SourcePath = sourcePath
        };

        try
        {
            if (!File.Exists(sourcePath))
            {
                result.Success = false;
                result.ErrorMessage = "El archivo no existe";
                return result;
            }

            var fileInfo = new FileInfo(sourcePath);
            result.OriginalSize = fileInfo.Length;

            var fileName = Path.GetFileNameWithoutExtension(sourcePath);
            var outputPath = Path.Combine(outputDirectory, $"{fileName}.webp");
            result.OutputPath = outputPath;

            progress?.Report($"Convirtiendo: {Path.GetFileName(sourcePath)}");

            // Cargar y convertir la imagen
            using var image = await Image.LoadAsync(sourcePath);
            
            var encoder = new WebpEncoder
            {
                Quality = quality,
                FileFormat = WebpFileFormatType.Lossy,
                Method = WebpEncodingMethod.BestQuality
            };

            await image.SaveAsync(outputPath, encoder);

            var outputInfo = new FileInfo(outputPath);
            result.ConvertedSize = outputInfo.Length;
            result.Success = true;

            progress?.Report($"✓ Completado: {Path.GetFileName(sourcePath)} ({result.CompressionRatio}% reducción)");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            progress?.Report($"✗ Error: {Path.GetFileName(sourcePath)} - {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Convierte múltiples imágenes a formato WebP
    /// </summary>
    public async Task<List<ConversionResult>> ConvertMultipleImagesAsync(
        IEnumerable<string> sourcePaths,
        string outputDirectory,
        int quality = 85,
        IProgress<string>? progress = null,
        IProgress<int>? percentProgress = null)
    {
        var results = new List<ConversionResult>();
        var paths = sourcePaths.ToList();
        var total = paths.Count;
        var current = 0;

        // Crear directorio de salida si no existe
        Directory.CreateDirectory(outputDirectory);

        foreach (var path in paths)
        {
            var result = await ConvertImageAsync(path, outputDirectory, quality, progress);
            results.Add(result);

            current++;
            percentProgress?.Report((int)((double)current / total * 100));
        }

        return results;
    }

    /// <summary>
    /// Convierte todas las imágenes de una carpeta a formato WebP
    /// </summary>
    public async Task<List<ConversionResult>> ConvertFolderAsync(
        string sourceFolder,
        string outputDirectory,
        int quality = 85,
        bool includeSubfolders = false,
        IProgress<string>? progress = null,
        IProgress<int>? percentProgress = null)
    {
        if (!Directory.Exists(sourceFolder))
        {
            throw new DirectoryNotFoundException($"La carpeta no existe: {sourceFolder}");
        }

        progress?.Report($"Escaneando carpeta: {sourceFolder}");

        var searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var imageFiles = Directory.GetFiles(sourceFolder, "*.*", searchOption)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();

        progress?.Report($"Encontradas {imageFiles.Count} imágenes para convertir");

        return await ConvertMultipleImagesAsync(imageFiles, outputDirectory, quality, progress, percentProgress);
    }

    /// <summary>
    /// Valida si un archivo es una imagen soportada
    /// </summary>
    public static bool IsSupportedImage(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return SupportedExtensions.Contains(extension);
    }

    /// <summary>
    /// Obtiene el tamaño formateado de un archivo
    /// </summary>
    public static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }
}
