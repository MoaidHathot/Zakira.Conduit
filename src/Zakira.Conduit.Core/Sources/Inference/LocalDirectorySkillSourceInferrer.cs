using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Sources.Inference;

/// <summary>
///     Recognises a URI as a local-filesystem path using strict prefix rules.
///     No filesystem probing is performed, so inference is fully deterministic.
///     <list type="bullet">
///         <item><description>Starts with <c>./</c> or <c>.\</c></description></item>
///         <item><description>Starts with <c>../</c> or <c>..\</c></description></item>
///         <item><description>Starts with <c>/</c> (Unix absolute)</description></item>
///         <item><description>Starts with <c>~</c></description></item>
///         <item><description>Starts with <c>$</c> or <c>${</c> or <c>%</c> (env-var expansion)</description></item>
///         <item><description>Starts with a Windows drive letter (e.g. <c>C:\</c> or <c>D:/</c>)</description></item>
///     </list>
/// </summary>
public sealed class LocalDirectorySkillSourceInferrer : ISkillSourceInferrer
{
    /// <inheritdoc />
    public string Kind => LocalDirectorySkillSource.TypeDiscriminator;

    /// <inheritdoc />
    public bool CanHandle(string uri) => LooksLikeLocalPath(uri);

    /// <inheritdoc />
    public ISkillSource Infer(UriBasedSkillSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        RejectIrrelevantFields(source);

        return new LocalDirectorySkillSource
        {
            Path = source.Uri,
        };
    }

    internal static bool LooksLikeLocalPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var v = value;
        if (v.StartsWith("./", StringComparison.Ordinal) || v.StartsWith(@".\", StringComparison.Ordinal)) return true;
        if (v.StartsWith("../", StringComparison.Ordinal) || v.StartsWith(@"..\", StringComparison.Ordinal)) return true;
        if (v.StartsWith('/')) return true;
        if (v.StartsWith('~')) return true;
        if (v.StartsWith('$')) return true;
        if (v.StartsWith('%')) return true;
        if (v.Length >= 3 && char.IsLetter(v[0]) && v[1] == ':' && (v[2] == '\\' || v[2] == '/')) return true;
        return false;
    }

    private static void RejectIrrelevantFields(UriBasedSkillSource source)
    {
        var bad = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(source.Branch)) bad.Add("branch");
        if (!string.IsNullOrWhiteSpace(source.Tag)) bad.Add("tag");
        if (!string.IsNullOrWhiteSpace(source.Commit)) bad.Add("commit");
        if (!string.IsNullOrWhiteSpace(source.BaseUrl)) bad.Add("baseUrl");
        if (source.Auth is { Count: > 0 }) bad.Add("auth");
        if (!string.IsNullOrWhiteSpace(source.PatEnv)) bad.Add("patEnv");
        if (source.Path is not null || source.Paths is { Count: > 0 })
        {
            bad.Add("path/paths (local source uri already names the directory)");
        }

        if (bad.Count > 0)
        {
            throw new SkillSourceInferenceException(
                $"Field(s) [{string.Join(", ", bad)}] do not apply to an inferred local source. Use 'type: local' if you need them.");
        }
    }
}
