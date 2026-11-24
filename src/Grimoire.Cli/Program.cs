namespace Grimoire.Cli;

// Not top-level statements: those compile to a `Program` class that would clash with the API's
// in test projects that see both assemblies.
internal static class CliProgram
{
    public static Task<int> Main(string[] args) => CliApp.RunAsync(args, CliHost.Default());
}
