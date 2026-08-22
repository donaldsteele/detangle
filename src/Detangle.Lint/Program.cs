using Detangle.Lint;

if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
{
    Console.Out.WriteLine(LintCommand.Usage);

    return args.Length == 0 ? LintCommand.CouldNotRun : 0;
}

(LintOptions? options, string? error) = LintCommand.Parse(args);

if (options is null)
{
    Console.Error.WriteLine($"detangle-lint: {error}");
    Console.Error.WriteLine();
    Console.Error.WriteLine(LintCommand.Usage);

    return LintCommand.CouldNotRun;
}

return LintCommand.Run(options, Console.Out, Console.Error);
