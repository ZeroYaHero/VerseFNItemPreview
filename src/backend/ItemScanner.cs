using System.Collections.Concurrent;
using System.Text.Json;
using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.AssetRegistry;
using CUE4Parse.UE4.AssetRegistry.Objects;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.i18N;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.GameplayTags;
using CUE4Parse.UE4.Versions;

namespace FortItemParser;

internal sealed record ScanOptions(string PaksDirectory, string OutputFile, string CacheDirectory)
{
    public static ScanOptions Parse(string[] args)
    {
        var paks = @"C:\Program Files\Epic Games\Fortnite\FortniteGame\Content\Paks"; // Default..."works on my machine" lol.
        var output = Path.GetFullPath(Path.Combine("data", "local_items_scan.json"));
        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VerseFNItemPreview", "mappings");
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for {args[i]}");
            }
            switch (args[i])
            {
                case "--paks":
                    paks = args[i + 1];
                    break;
                case "--out":
                    output = args[i + 1];
                    break;
                case "--cache-dir":
                    cache = args[i + 1];
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {args[i]}");
            }
        }
        return new ScanOptions(Path.GetFullPath(paks), Path.GetFullPath(output), Path.GetFullPath(cache));
    }
}

internal sealed record ScannedItem(
    string Category, string ClassName, string AssetName, string ObjectPath,
    string? DisplayName, string? IconPath, string? LargeIconPath, string[] GameplayTags, string? Error);

internal sealed record ScannedSource(string ClassName, string AssetName, string ObjectPath);

internal sealed record ScannedImageGroup(
    string Category, string DisplayName, string IconPath, string? LargeIconPath,
    string[] GameplayTags, ScannedSource[] Sources);

internal sealed class ItemScanner
{
    private readonly DefaultFileProvider _provider;
    private readonly ConcurrentBag<ScannedItem> _results = new();
    private int _candidateCount;
    private int _completed;

    private ItemScanner(DefaultFileProvider provider)
    {
        _provider = provider;
    }

    // Credit for categories and excluded names to H4lfHeartDev's FortnitePorting.
    private static readonly Dictionary<string, string> Categories = new(StringComparer.Ordinal)
    {
        ["AthenaGadgetItemDefinition"] = "item",
        ["FortWeaponRangedItemDefinition"] = "item",
        ["FortWeaponMeleeItemDefinition"] = "item",
        ["FortCreativeWeaponMeleeItemDefinition"] = "item",
        ["FortCreativeWeaponRangedItemDefinition"] = "item",
        ["FortWeaponMeleeDualWieldItemDefinition"] = "item",
        ["FortIngredientItemDefinition"] = "resource",
        ["FortResourceItemDefinition"] = "resource",
        ["FortTrapItemDefinition"] = "trap"
    };

    private static readonly Dictionary<string, string[]> ExcludedNames = new()
    {
        ["item"] = new[] { "_Harvest", "Weapon_Pickaxe_", "Weapons_Pickaxe_", "Dev_WID", "Juno" },
        ["resource"] = new[] { "SurvivorItemData", "OutpostUpgrade_StormShieldAmplifier" },
        ["trap"] = new[] { "TID_Creative", "TID_Floor_Minigame_Trigger_Plate" }
    };

    public static async Task ScanAsync(ScanOptions options, Func<DefaultFileProvider, string, Task>? onReady = null)
    {
        if (!Directory.Exists(options.PaksDirectory))
        {
            throw new DirectoryNotFoundException($"Fortnite archive directory not found: {options.PaksDirectory}");
        }

        using var api = new FortApi();
        Console.Error.WriteLine("Fetching version metadata and mappings...");
        var version = await api.FetchVersionAsync();
        var mappingsPath = await api.GetMappingsAsync(version, options.CacheDirectory);

        using var provider = await InitializeProvider(options, version, mappingsPath);
        var (candidates, registriesRead, registryErrors) = await ReadRegistries(provider);
        var scanner = new ItemScanner(provider);
        var scanned = await scanner.LoadAssets(candidates);
        await WriteReport(options, version, provider, registriesRead, registryErrors, candidates.Count, scanned);
        if (onReady != null)
        {
            await onReady(provider, version.Version);
        }
    }

    private static async Task<DefaultFileProvider> InitializeProvider(ScanOptions options, FortApi.FortVersion version, string mappingsPath)
    {
        Directory.CreateDirectory(options.CacheDirectory);
        await ZlibHelper.InitializeAsync(Path.Combine(options.CacheDirectory, ZlibHelper.DllName));
        await OodleHelper.InitializeAsync(Path.Combine(options.CacheDirectory, OodleHelper.OodleFileName));
        var provider = new DefaultFileProvider(options.PaksDirectory, SearchOption.TopDirectoryOnly,
            new VersionContainer(EGame.GAME_UE6_0), StringComparer.OrdinalIgnoreCase)
        {
            MappingsContainer = new FileUsmapTypeMappingsProvider(mappingsPath, StringComparer.Ordinal)
        };

        Console.Error.WriteLine("Mounting Fortnite archives...");
        provider.Initialize();
        await provider.SubmitKeyAsync(new FGuid(), new FAesKey(version.Keys.MainKey.Key));
        foreach (var key in version.Keys.ExtraKeys)
        {
            await provider.SubmitKeyAsync(new FGuid(key.Guid), new FAesKey(key.Key));
        }
        provider.LoadVirtualPaths();
        provider.PostMount();
        Console.Error.WriteLine($"Mounted {provider.MountedVfs.Count} containers. \n{provider.Files.Count} files visible.");

        return provider;
    }

    private static async Task<(Dictionary<string, FPartialAssetData> Assets, int Count, List<string> Errors)> ReadRegistries(DefaultFileProvider provider)
    {
        var candidates = new Dictionary<string, FPartialAssetData>(StringComparer.OrdinalIgnoreCase);
        var registriesRead = 0;
        var registryErrors = new List<string>();
        foreach (var (path, file) in provider.Files)
        {
            if (!path.EndsWith("AssetRegistry.bin", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (path.Contains("Editor", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using var archive = await file.SafeCreateReaderAsync();
                if (archive == null)
                {
                    throw new IOException("Could not read registry archive");
                }
                var registry = new FPartialAssetRegistryState(archive);
                registriesRead++;
                foreach (var data in registry.PreallocatedAssetDataBuffers)
                {
                    if (!IsCandidate(data))
                    {
                        continue;
                    }
                    candidates.TryAdd(data.ObjectPath, data);
                }
                Console.Error.WriteLine($"Read {path}: {registry.PreallocatedAssetDataBuffers.Length} assets");
            }
            catch (Exception error)
            {
                registryErrors.Add($"{path}: {error.Message}");
                Console.Error.WriteLine($"Could not read {path}: {error.Message}");
            }
        }

        if (registriesRead == 0)
        {
            throw new InvalidOperationException("No asset registries could be read. " + string.Join(". ", registryErrors));
        }
        return (candidates, registriesRead, registryErrors);
    }

    // Simple way to filter any asset that we don't want
    private static bool IsCandidate(FPartialAssetData data)
    {
        if (!Categories.TryGetValue(data.AssetClass.Text, out var category))
        {
            return false;
        }
        if (data.AssetName.Text.EndsWith("Random", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (data.PackageName.Text.Contains("Placeholder", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        foreach (var name in ExcludedNames[category])
        {
            if (data.PackageName.Text.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    private async Task<ScannedItem[]> LoadAssets(Dictionary<string, FPartialAssetData> candidates)
    {
        _candidateCount = candidates.Count;
        Console.Error.WriteLine($"Loading {_candidateCount} candidate item definitions...");
        var options = new ParallelOptions { MaxDegreeOfParallelism = 2 };
        await Parallel.ForEachAsync(candidates.Values, options, LoadAssetAsync);
        return _results.ToArray();
    }

    private async ValueTask LoadAssetAsync(FPartialAssetData data, CancellationToken cancellationToken)
    {
        var category = Categories[data.AssetClass.Text];
        try
        {
            var asset = await _provider.SafeLoadPackageObjectAsync(data.ObjectPath);
            if (asset == null)
            {
                throw new IOException("Asset was not loadable");
            }

            var icon = FindIcon(asset, "Icon", "SmallPreviewImage", "LargeIcon", "LargePreviewImage");
            var largeIcon = FindIcon(asset, "LargeIcon", "LargePreviewImage", "Icon", "SmallPreviewImage");
            var displayName = asset.GetOrDefault<FText?>("DisplayName")?.Text;
            if (displayName == null)
            {
                displayName = asset.GetOrDefault<FText?>("ItemName")?.Text;
            }
            _results.Add(new ScannedItem(category, data.AssetClass.Text, data.AssetName.Text, data.ObjectPath,
                displayName, icon?.GetPathName(), largeIcon?.GetPathName(), GetGameplayTags(asset), null));
        }
        catch (Exception error)
        {
            _results.Add(new ScannedItem(category, data.AssetClass.Text, data.AssetName.Text, data.ObjectPath,
                null, null, null, Array.Empty<string>(), error.Message));
        }
        var count = Interlocked.Increment(ref _completed);
        if (count % 500 == 0)
        {
            Console.Error.WriteLine($"Processed {count}/{_candidateCount}");
        }
    }

    private static async Task WriteReport(ScanOptions options, FortApi.FortVersion version, DefaultFileProvider provider,
        int registriesRead, List<string> registryErrors, int candidateCount, ScannedItem[] scanned)
    {
        var iconBearing = scanned.Where(HasIcon).ToArray();
        var items = iconBearing
            .GroupBy(GetImageGroupKey)
            .Select(CreateImageGroup)
            .OrderBy(group => group.Category)
            .ThenBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.IconPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var report = new
        {
            version = version.Version,
            archiveDirectory = options.PaksDirectory,
            mountedContainers = provider.MountedVfs.Count,
            registriesRead,
            registryErrors,
            candidates = candidateCount,
            loaded = scanned.Count(item => item.Error == null),
            withoutIcon = scanned.Count(item => item.Error == null && item.IconPath == null && item.LargeIconPath == null),
            loadFailures = scanned.Count(item => item.Error != null),
            withIcon = iconBearing.Length,
            imageGroups = items.Length,
            items
        };
        Directory.CreateDirectory(Path.GetDirectoryName(options.OutputFile)!);
        await using var output = File.Create(options.OutputFile);
        await JsonSerializer.SerializeAsync(output, report, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        Console.Error.WriteLine($"Wrote {items.Length} image groups from {iconBearing.Length} definitions to {options.OutputFile}");
    }

    private static bool HasIcon(ScannedItem item)
    {
        return item.IconPath != null || item.LargeIconPath != null;
    }

    private static string GetDisplayName(ScannedItem item)
    {
        var name = item.DisplayName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return item.AssetName;
        }
        return name;
    }

    private static string GetIconPath(ScannedItem item)
    {
        if (item.IconPath != null)
        {
            return item.IconPath;
        }
        if (item.LargeIconPath != null)
        {
            return item.LargeIconPath;
        }
        throw new InvalidOperationException("Item has no icon path.");
    }

    private static (string Category, string Name, string Icon) GetImageGroupKey(ScannedItem item)
    {
        var name = NormalizeName(GetDisplayName(item));
        var icon = GetIconPath(item).ToLowerInvariant();
        return (item.Category, name, icon);
    }

    private static ScannedImageGroup CreateImageGroup(IGrouping<(string Category, string Name, string Icon), ScannedItem> group)
    {
        var first = group.OrderBy(item => item.ObjectPath, StringComparer.OrdinalIgnoreCase).First();
        var tags = group.SelectMany(item => item.GameplayTags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sources = group.Select(item => new ScannedSource(item.ClassName, item.AssetName, item.ObjectPath))
            .OrderBy(source => source.ObjectPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new ScannedImageGroup(first.Category, GetDisplayName(first), GetIconPath(first),
            first.LargeIconPath, tags, sources);
    }

    private static string NormalizeName(string name)
    {
        var characters = name.Normalize(System.Text.NormalizationForm.FormKD)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray();
        return new string(characters);
    }

    private static UTexture2D? FindIcon(UObject asset, params string[] names)
    {
        if (asset.TryGetValue(out FInstancedStruct[] dataList, "DataList"))
        {
            foreach (var entry in dataList)
            {
                var data = entry.NonConstStruct;
                if (data != null && data.TryGetValue(out UTexture2D icon, names))
                {
                    return icon;
                }
            }
        }
        foreach (var name in names)
        {
            if (asset.TryGetValue(out UTexture2D icon, name))
            {
                return icon;
            }
        }
        return null;
    }

    private static string[] GetGameplayTags(UObject asset)
    {
        if (asset.TryGetValue(out FInstancedStruct[] dataList, "DataList"))
        {
            foreach (var entry in dataList)
            {
                var data = entry.NonConstStruct;
                if (data != null && data.TryGetValue(out FGameplayTagContainer tags, "Tags"))
                {
                    return tags.GameplayTags.Select(tag => tag.TagName.Text).ToArray();
                }
            }
        }
        if (asset.TryGetValue(out FGameplayTagContainer directTags, "GameplayTags"))
        {
            return directTags.GameplayTags.Select(tag => tag.TagName.Text).ToArray();
        }
        return Array.Empty<string>();
    }
}
