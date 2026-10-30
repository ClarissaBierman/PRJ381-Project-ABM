// =============================================================================
// ConfigValidationException.cs  (AbmFramework.Config)
//
// Thrown by ConfigLoader.Validate when a config has problems. It carries ALL
// the problems at once (not just the first), so the dashboard can show the
// user everything they need to fix in one go.
// =============================================================================

namespace AbmFramework.Config;

/// <summary>A config failed validation. <see cref="Errors"/> lists every problem found.</summary>
public sealed class ConfigValidationException : Exception
{
    /// <summary>One readable sentence per problem, for example "GridWidth must be positive (got 0).".</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Creates the exception from the list of problems.</summary>
    public ConfigValidationException(IReadOnlyList<string> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    // Builds the Message text: a heading plus one "  - problem" line per error.
    private static string BuildMessage(IReadOnlyList<string> errors) =>
        "Invalid simulation configuration:" + Environment.NewLine +
        string.Join(Environment.NewLine, errors.Select(e => "  - " + e));
}
