namespace AbmFramework.Config;

public sealed class ConfigValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public ConfigValidationException(IReadOnlyList<string> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    private static string BuildMessage(IReadOnlyList<string> errors) =>
        "Invalid simulation configuration:" + Environment.NewLine +
        string.Join(Environment.NewLine, errors.Select(e => "  - " + e));
}
