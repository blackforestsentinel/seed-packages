namespace Bfs.Seed.OpenApi.Export;

/// <summary>Einstieg: <c>dotnet Bfs.Seed.OpenApi.Export.dll &lt;App.dll&gt; &lt;openapi.json&gt;</c>.</summary>
internal static class Program
{
    private static int Main(string[] args) => ExportCommand.Run(args, Console.Out, Console.Error);
}
