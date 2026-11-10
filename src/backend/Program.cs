namespace FortItemParser;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var serve = args.Contains("--serve");
            var options = ScanOptions.Parse(args.Where(arg => arg != "--serve").ToArray());
            await ItemScanner.ScanAsync(options, serve
                ? (provider, version) => PreviewService.RunAsync(provider, version, options.CacheDirectory)
                : null);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
