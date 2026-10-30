using System.Globalization;
using Microsoft.CSharp.RuntimeBinder;
using System.Runtime.InteropServices;

return FavoriteImageMoverApp.Run(args);

internal static class FavoriteImageMoverApp
{
    private static readonly HashSet<string> DefaultImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".gif",
        ".bmp",
        ".tif",
        ".tiff",
        ".webp",
        ".heic",
        ".heif",
        ".avif"
    };

    public static int Run(string[] args)
    {
        if (args.Any(IsHelpArgument))
        {
            PrintUsage();
            return 0;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("This app reads Windows Shell metadata and must be run on Windows.");
            return 1;
        }

        if (!Options.TryParse(args, out var options, out var parseError))
        {
            Console.Error.WriteLine(parseError);
            Console.WriteLine();
            PrintUsage();
            return 1;
        }

        var sourceFolder = Path.GetFullPath(options.SourceFolder);
        var destinationFolder = Path.GetFullPath(options.DestinationFolder);

        if (!Directory.Exists(sourceFolder))
        {
            Console.Error.WriteLine($"Source folder does not exist: {sourceFolder}");
            return 1;
        }

        if (PathsEqual(sourceFolder, destinationFolder))
        {
            Console.Error.WriteLine("Source and destination folders must be different.");
            return 1;
        }

        Directory.CreateDirectory(destinationFolder);

        ShellFavoriteReader reader;
        try
        {
            reader = new ShellFavoriteReader();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not start Windows Shell metadata reader: {ex.Message}");
            return 1;
        }

        using (reader)
        {
            var stats = new MoveStats();

            foreach (var filePath in EnumerateFiles(sourceFolder, options.Recursive, destinationFolder, OnEnumerationError))
            {
                if (!IsSupportedImage(filePath, options.ImageExtensions))
                {
                    continue;
                }

                if (!options.IncludeHidden && IsHiddenOrSystem(filePath))
                {
                    stats.HiddenSkipped++;
                    continue;
                }

                stats.ImagesScanned++;
                var favoriteResult = reader.Check(filePath);

                if (favoriteResult.HadError)
                {
                    stats.MetadataErrors++;
                    if (options.Verbose)
                    {
                        Console.Error.WriteLine($"metadata error: \"{filePath}\" ({favoriteResult.Reason})");
                    }

                    continue;
                }

                if (!favoriteResult.IsFavorite)
                {
                    if (options.Verbose)
                    {
                        Console.WriteLine($"skip: \"{filePath}\"");
                    }

                    continue;
                }

                stats.FavoritesFound++;
                var targetPath = GetAvailableDestinationPath(destinationFolder, filePath);

                try
                {
                    if (options.DryRun)
                    {
                        Console.WriteLine($"would {ActionName(options)}: \"{filePath}\" -> \"{targetPath}\" ({favoriteResult.Reason})");
                        stats.WouldProcess++;
                    }
                    else if (options.CopyInsteadOfMove)
                    {
                        File.Copy(filePath, targetPath);
                        Console.WriteLine($"copied: \"{filePath}\" -> \"{targetPath}\" ({favoriteResult.Reason})");
                        stats.Processed++;
                    }
                    else
                    {
                        File.Move(filePath, targetPath);
                        Console.WriteLine($"moved: \"{filePath}\" -> \"{targetPath}\" ({favoriteResult.Reason})");
                        stats.Processed++;
                    }
                }
                catch (Exception ex) when (IsExpectedFileException(ex))
                {
                    stats.MoveErrors++;
                    Console.Error.WriteLine($"failed: \"{filePath}\" ({ex.Message})");
                }
            }

            PrintSummary(stats, options);
            return stats.MoveErrors > 0 || stats.MetadataErrors > 0 ? 2 : 0;
        }
    }

    private static bool IsHelpArgument(string arg)
    {
        return arg.Equals("-h", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--help", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("/?", StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("FavoriteImageMover");
        Console.WriteLine();
        Console.WriteLine("Moves image files that are marked as favorite or have a Windows star rating.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project FavoriteImageMover -- <source-folder> <destination-folder> [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -r, --recursive              Include subfolders.");
        Console.WriteLine("  --dry-run                    Show what would be moved without changing files.");
        Console.WriteLine("  --copy                       Copy favorites instead of moving them.");
        Console.WriteLine("  --include-hidden             Include hidden/system files.");
        Console.WriteLine("  --extensions .jpg,.png       Override image extensions to scan.");
        Console.WriteLine("  --verbose                    Print skipped files and metadata errors.");
        Console.WriteLine("  -h, --help                   Show help.");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run --project FavoriteImageMover -- \"C:\\Photos\" \"D:\\Favorites\" --recursive --dry-run");
        Console.WriteLine("  dotnet run --project FavoriteImageMover -- \"C:\\Photos\" \"D:\\Favorites\" --recursive");
    }

    private static IEnumerable<string> EnumerateFiles(
        string sourceFolder,
        bool recursive,
        string destinationFolder,
        Action<string, Exception> onError)
    {
        var pendingFolders = new Stack<string>();
        pendingFolders.Push(sourceFolder);

        while (pendingFolders.Count > 0)
        {
            var currentFolder = pendingFolders.Pop();

            if (!PathsEqual(currentFolder, sourceFolder) && IsSameOrInsideDirectory(currentFolder, destinationFolder))
            {
                continue;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(currentFolder);
            }
            catch (Exception ex) when (IsExpectedFileException(ex))
            {
                onError(currentFolder, ex);
                continue;
            }

            foreach (var file in files)
            {
                if (!IsSameOrInsideDirectory(file, destinationFolder))
                {
                    yield return file;
                }
            }

            if (!recursive)
            {
                continue;
            }

            string[] childFolders;
            try
            {
                childFolders = Directory.GetDirectories(currentFolder);
            }
            catch (Exception ex) when (IsExpectedFileException(ex))
            {
                onError(currentFolder, ex);
                continue;
            }

            foreach (var childFolder in childFolders)
            {
                if (!IsSameOrInsideDirectory(childFolder, destinationFolder))
                {
                    pendingFolders.Push(childFolder);
                }
            }
        }
    }

    private static void OnEnumerationError(string path, Exception ex)
    {
        Console.Error.WriteLine($"Could not read \"{path}\": {ex.Message}");
    }

    private static bool IsSupportedImage(string filePath, HashSet<string> imageExtensions)
    {
        return imageExtensions.Contains(Path.GetExtension(filePath));
    }

    private static bool IsHiddenOrSystem(string filePath)
    {
        try
        {
            var attributes = File.GetAttributes(filePath);
            return attributes.HasFlag(FileAttributes.Hidden) || attributes.HasFlag(FileAttributes.System);
        }
        catch
        {
            return false;
        }
    }

    private static string GetAvailableDestinationPath(string destinationFolder, string sourceFilePath)
    {
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(sourceFilePath);
        var extension = Path.GetExtension(sourceFilePath);
        var candidatePath = Path.Combine(destinationFolder, Path.GetFileName(sourceFilePath));

        for (var index = 1; File.Exists(candidatePath); index++)
        {
            candidatePath = Path.Combine(destinationFolder, $"{fileNameWithoutExtension} ({index}){extension}");
        }

        return candidatePath;
    }

    private static void PrintSummary(MoveStats stats, Options options)
    {
        Console.WriteLine();
        Console.WriteLine("Summary");
        Console.WriteLine($"  Images scanned:     {stats.ImagesScanned}");
        Console.WriteLine($"  Favorites found:    {stats.FavoritesFound}");

        if (options.DryRun)
        {
            Console.WriteLine($"  Would {ActionName(options)}:       {stats.WouldProcess}");
        }
        else
        {
            Console.WriteLine($"  {PastActionName(options)}:             {stats.Processed}");
        }

        Console.WriteLine($"  Hidden skipped:     {stats.HiddenSkipped}");
        Console.WriteLine($"  Metadata errors:    {stats.MetadataErrors}");
        Console.WriteLine($"  File errors:        {stats.MoveErrors}");
    }

    private static string ActionName(Options options)
    {
        return options.CopyInsteadOfMove ? "copy" : "move";
    }

    private static string PastActionName(Options options)
    {
        return options.CopyInsteadOfMove ? "Copied" : "Moved";
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameOrInsideDirectory(string candidatePath, string directoryPath)
    {
        var fullCandidatePath = Path.GetFullPath(candidatePath);
        var fullDirectoryPath = Path.GetFullPath(directoryPath);

        if (PathsEqual(fullCandidatePath, fullDirectoryPath))
        {
            return true;
        }

        var directoryWithSeparator = EnsureTrailingSeparator(fullDirectoryPath);
        return fullCandidatePath.StartsWith(directoryWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        return trimmed.EndsWith(Path.DirectorySeparatorChar)
            ? trimmed
            : trimmed + Path.DirectorySeparatorChar;
    }

    private static bool IsExpectedFileException(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or DirectoryNotFoundException
            or PathTooLongException;
    }

    private sealed class MoveStats
    {
        public int ImagesScanned { get; set; }
        public int FavoritesFound { get; set; }
        public int WouldProcess { get; set; }
        public int Processed { get; set; }
        public int HiddenSkipped { get; set; }
        public int MetadataErrors { get; set; }
        public int MoveErrors { get; set; }
    }

    private sealed class Options
    {
        private Options(
            string sourceFolder,
            string destinationFolder,
            bool recursive,
            bool dryRun,
            bool copyInsteadOfMove,
            bool includeHidden,
            bool verbose,
            HashSet<string> imageExtensions)
        {
            SourceFolder = sourceFolder;
            DestinationFolder = destinationFolder;
            Recursive = recursive;
            DryRun = dryRun;
            CopyInsteadOfMove = copyInsteadOfMove;
            IncludeHidden = includeHidden;
            Verbose = verbose;
            ImageExtensions = imageExtensions;
        }

        public string SourceFolder { get; }
        public string DestinationFolder { get; }
        public bool Recursive { get; }
        public bool DryRun { get; }
        public bool CopyInsteadOfMove { get; }
        public bool IncludeHidden { get; }
        public bool Verbose { get; }
        public HashSet<string> ImageExtensions { get; }

        public static bool TryParse(string[] args, out Options options, out string error)
        {
            var positionalArgs = new List<string>();
            var imageExtensions = new HashSet<string>(DefaultImageExtensions, StringComparer.OrdinalIgnoreCase);
            var recursive = false;
            var dryRun = false;
            var copyInsteadOfMove = false;
            var includeHidden = false;
            var verbose = false;

            for (var index = 0; index < args.Length; index++)
            {
                var arg = args[index];

                if (arg.Equals("-r", StringComparison.OrdinalIgnoreCase)
                    || arg.Equals("--recursive", StringComparison.OrdinalIgnoreCase))
                {
                    recursive = true;
                    continue;
                }

                if (arg.Equals("--dry-run", StringComparison.OrdinalIgnoreCase))
                {
                    dryRun = true;
                    continue;
                }

                if (arg.Equals("--copy", StringComparison.OrdinalIgnoreCase))
                {
                    copyInsteadOfMove = true;
                    continue;
                }

                if (arg.Equals("--include-hidden", StringComparison.OrdinalIgnoreCase))
                {
                    includeHidden = true;
                    continue;
                }

                if (arg.Equals("--verbose", StringComparison.OrdinalIgnoreCase))
                {
                    verbose = true;
                    continue;
                }

                if (arg.Equals("--extensions", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length)
                    {
                        options = Empty();
                        error = "Missing value after --extensions.";
                        return false;
                    }

                    if (!TryParseExtensions(args[++index], out imageExtensions, out error))
                    {
                        options = Empty();
                        return false;
                    }

                    continue;
                }

                if (arg.StartsWith("--extensions=", StringComparison.OrdinalIgnoreCase))
                {
                    var extensionValue = arg["--extensions=".Length..];
                    if (!TryParseExtensions(extensionValue, out imageExtensions, out error))
                    {
                        options = Empty();
                        return false;
                    }

                    continue;
                }

                if (arg.StartsWith('-'))
                {
                    options = Empty();
                    error = $"Unknown option: {arg}";
                    return false;
                }

                positionalArgs.Add(arg);
            }

            if (positionalArgs.Count != 2)
            {
                options = Empty();
                error = "Provide exactly two folder paths: <source-folder> <destination-folder>.";
                return false;
            }

            options = new Options(
                positionalArgs[0],
                positionalArgs[1],
                recursive,
                dryRun,
                copyInsteadOfMove,
                includeHidden,
                verbose,
                imageExtensions);
            error = string.Empty;
            return true;
        }

        private static bool TryParseExtensions(
            string value,
            out HashSet<string> imageExtensions,
            out string error)
        {
            imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rawExtension in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var extension = rawExtension.StartsWith('.') ? rawExtension : "." + rawExtension;

                if (extension.Length == 1 || extension.Any(Path.GetInvalidFileNameChars().Contains))
                {
                    error = $"Invalid extension: {rawExtension}";
                    return false;
                }

                imageExtensions.Add(extension);
            }

            if (imageExtensions.Count == 0)
            {
                error = "At least one extension is required.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static Options Empty()
        {
            return new Options(string.Empty, string.Empty, false, false, false, false, false, []);
        }
    }
}

internal sealed class ShellFavoriteReader : IDisposable
{
    private readonly object _shellObject;
    private readonly dynamic _shellApplication;
    private bool _disposed;

    public ShellFavoriteReader()
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application")
            ?? throw new InvalidOperationException("Shell.Application COM object was not found.");

        _shellObject = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Shell.Application COM object could not be created.");

        _shellApplication = _shellObject;
    }

    public FavoriteResult Check(string filePath)
    {
        object? folderObject = null;
        object? itemObject = null;

        try
        {
            var folderPath = Path.GetDirectoryName(filePath)
                ?? throw new InvalidOperationException("File does not have a parent folder.");
            var fileName = Path.GetFileName(filePath);

            folderObject = _shellApplication.NameSpace(folderPath);
            if (folderObject is null)
            {
                return FavoriteResult.Error("Shell folder could not be opened.");
            }

            dynamic folder = folderObject;
            itemObject = folder.ParseName(fileName);
            if (itemObject is null)
            {
                return FavoriteResult.Error("Shell item could not be opened.");
            }

            dynamic item = itemObject;

            object? isFavorite = GetExtendedProperty(item, "System.IsFavorite");
            if (TryGetBoolean(isFavorite) == true)
            {
                return FavoriteResult.Favorite("System.IsFavorite=true");
            }

            object? simpleRatingValue = GetExtendedProperty(item, "System.SimpleRating");
            var simpleRating = TryGetUnsignedInteger(simpleRatingValue);
            if (simpleRating.HasValue && simpleRating.Value > 0)
            {
                return FavoriteResult.Favorite($"System.SimpleRating={simpleRating.Value}");
            }

            object? ratingValue = GetExtendedProperty(item, "System.Rating");
            var rating = TryGetUnsignedInteger(ratingValue);
            if (rating.HasValue && rating.Value > 0)
            {
                return FavoriteResult.Favorite($"System.Rating={rating.Value}");
            }

            return FavoriteResult.NotFavorite();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or RuntimeBinderException)
        {
            return FavoriteResult.Error(ex.Message);
        }
        finally
        {
            ReleaseComObject(itemObject);
            ReleaseComObject(folderObject);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ReleaseComObject(_shellObject);
        _disposed = true;
    }

    private static object? GetExtendedProperty(dynamic item, string propertyName)
    {
        try
        {
            return item.ExtendedProperty(propertyName);
        }
        catch (Exception ex) when (ex is COMException or RuntimeBinderException)
        {
            return null;
        }
    }

    private static bool? TryGetBoolean(object? value)
    {
        return value switch
        {
            null => null,
            DBNull => null,
            bool boolValue => boolValue,
            string stringValue when bool.TryParse(stringValue, out var boolValue) => boolValue,
            string stringValue when uint.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue) => intValue > 0,
            byte byteValue => byteValue > 0,
            short shortValue => shortValue > 0,
            ushort ushortValue => ushortValue > 0,
            int intValue => intValue > 0,
            uint uintValue => uintValue > 0,
            long longValue => longValue > 0,
            ulong ulongValue => ulongValue > 0,
            _ => null
        };
    }

    private static uint? TryGetUnsignedInteger(object? value)
    {
        return value switch
        {
            null => null,
            DBNull => null,
            byte byteValue => byteValue,
            short shortValue when shortValue >= 0 => (uint)shortValue,
            ushort ushortValue => ushortValue,
            int intValue when intValue >= 0 => (uint)intValue,
            uint uintValue => uintValue,
            long longValue when longValue >= 0 => (uint)longValue,
            ulong ulongValue when ulongValue <= uint.MaxValue => (uint)ulongValue,
            decimal decimalValue when decimalValue >= 0 && decimalValue <= uint.MaxValue => (uint)decimalValue,
            double doubleValue when doubleValue >= 0 && doubleValue <= uint.MaxValue => (uint)doubleValue,
            string stringValue when uint.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uintValue) => uintValue,
            _ => null
        };
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}

internal sealed record FavoriteResult(bool IsFavorite, bool HadError, string Reason)
{
    public static FavoriteResult Favorite(string reason)
    {
        return new FavoriteResult(true, false, reason);
    }

    public static FavoriteResult NotFavorite()
    {
        return new FavoriteResult(false, false, "No favorite metadata found.");
    }

    public static FavoriteResult Error(string reason)
    {
        return new FavoriteResult(false, true, reason);
    }
}
