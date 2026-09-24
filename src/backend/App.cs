using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using CUE4Parse.UE4.Readers;
using CUE4Parse.Utils;
using CUE4Parse.UE4.IO;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.Encryption.Aes;

var archivePath = "C:\\Program Files\\Epic Games\\Fortnite\\FortniteGame\\Content\\Paks";
var dirInfo = new DirectoryInfo(archivePath);

var provider = new DefaultFileProvider(dirInfo, SearchOption.TopDirectoryOnly, new VersionContainer(EGame.GAME_UE6_0));
provider.Initialize();
Console.WriteLine($"Loose: {provider.LooseFileCount}, Unloaded: {provider.UnloadedVfs.Count}, Mounted: {provider.MountedVfs.Count}, Files: {provider.Files.Count}");

await provider.SubmitKeyAsync(new FGuid(), new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));
Console.WriteLine($"Unloaded: {provider.UnloadedVfs.Count}, Mounted: {provider.MountedVfs.Count}, Files: {provider.Files.Count}");

// Rarity
if(await provider.SafeLoadPackageObjectAsync("FortniteGame/Content/Balance/RarityData") is { } rarityData)
{
    Console.WriteLine("Hello");
}
else
{
    Console.WriteLine("Rarity Data Null");
}
