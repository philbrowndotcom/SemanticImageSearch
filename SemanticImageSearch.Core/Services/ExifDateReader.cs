using System.Globalization;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace SemanticImageSearch.Core.Services;

public static class ExifDateReader
{
    private static readonly string[] ExifDateFormats =
    {
        "yyyy:MM:dd HH:mm:ss",
        "yyyy:MM:dd HH:mm:ss.fff",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss"
    };

    /// <summary>
    /// Reads the capture date from EXIF metadata (DateTimeOriginal, then DateTime).
    /// EXIF dates have no time zone, so they are treated as local time and converted to UTC.
    /// Returns <paramref name="fallbackUtc"/> if no valid EXIF date is found.
    /// </summary>
    public static DateTime GetDateTakenUtc(string filePath, DateTime fallbackUtc)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(filePath);

            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (TryGetDate(subIfd, ExifDirectoryBase.TagDateTimeOriginal, out var taken) ||
                TryGetDate(subIfd, ExifDirectoryBase.TagDateTimeDigitized, out taken))
            {
                return ToUtc(taken);
            }

            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            if (TryGetDate(ifd0, ExifDirectoryBase.TagDateTime, out taken))
            {
                return ToUtc(taken);
            }
        }
        catch (Exception ex) when (ex is ImageProcessingException or IOException)
        {
        }

        return DateTime.SpecifyKind(fallbackUtc, DateTimeKind.Utc);
    }

    private static bool TryGetDate(MetadataExtractor.Directory? directory, int tag, out DateTime value)
    {
        value = default;
        if (directory is null || !directory.ContainsTag(tag))
        {
            return false;
        }

        var raw = directory.GetString(tag)?.Trim().TrimEnd('\0');
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (DateTime.TryParseExact(raw, ExifDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
        {
            return value.Year > 1900;
        }

        return false;
    }

    private static DateTime ToUtc(DateTime local) =>
        DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();
}
