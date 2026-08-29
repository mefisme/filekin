using System.Security.Cryptography;
using System.Text.Json;

namespace Filekin.Infrastructure.Windows.Agents;

/// <summary>
/// Refuses Claude Code launches when an inherited variable or an applicable local settings file could
/// select separately billed API, cloud-provider, profile, federation, or gateway authentication.
/// Credential values are never decoded, returned, or retained after inspection.
/// </summary>
internal sealed class ClaudeBillingOverrideDetector
{
    private const int MaximumSettingsFileBytes = 4 * 1024 * 1024;

    private static readonly string[] CredentialOrEndpointVariables =
    [
        "ANTHROPIC_API_KEY",
        "ANTHROPIC_AUTH_TOKEN",
        "ANTHROPIC_AWS_API_KEY",
        "ANTHROPIC_AWS_BASE_URL",
        "ANTHROPIC_AWS_WORKSPACE_ID",
        "ANTHROPIC_BASE_URL",
        "ANTHROPIC_BEDROCK_BASE_URL",
        "ANTHROPIC_BEDROCK_MANTLE_BASE_URL",
        "ANTHROPIC_CUSTOM_HEADERS",
        "ANTHROPIC_FEDERATION_RULE_ID",
        "ANTHROPIC_FOUNDRY_API_KEY",
        "ANTHROPIC_FOUNDRY_AUTH_TOKEN",
        "ANTHROPIC_FOUNDRY_BASE_URL",
        "ANTHROPIC_FOUNDRY_RESOURCE",
        "ANTHROPIC_ORGANIZATION_ID",
        "ANTHROPIC_PROFILE",
        "ANTHROPIC_VERTEX_BASE_URL",
        "ANTHROPIC_VERTEX_PROJECT_ID",
        "ANTHROPIC_WORKSPACE_ID",
        "AWS_BEARER_TOKEN_BEDROCK",
    ];

    private static readonly string[] ProviderSelectorVariables =
    [
        "CLAUDE_CODE_USE_ANTHROPIC_AWS",
        "CLAUDE_CODE_USE_BEDROCK",
        "CLAUDE_CODE_USE_FOUNDRY",
        "CLAUDE_CODE_USE_MANTLE",
        "CLAUDE_CODE_USE_VERTEX",
    ];

    private static readonly HashSet<string> CredentialOrEndpointVariableSet =
        new(CredentialOrEndpointVariables, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ProviderSelectorVariableSet =
        new(ProviderSelectorVariables, StringComparer.OrdinalIgnoreCase);

    private readonly Func<string, string?> _environmentReader;
    private readonly string _defaultConfigurationDirectory;

    public ClaudeBillingOverrideDetector()
        : this(
            Environment.GetEnvironmentVariable,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude"))
    {
    }

    internal ClaudeBillingOverrideDetector(
        Func<string, string?> environmentReader,
        string defaultConfigurationDirectory)
    {
        ArgumentNullException.ThrowIfNull(environmentReader);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultConfigurationDirectory);
        _environmentReader = environmentReader;
        _defaultConfigurationDirectory = Path.GetFullPath(defaultConfigurationDirectory);
    }

    public void ThrowIfConfigured(string projectFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolderPath);
        var projectFolder = Path.GetFullPath(projectFolderPath);

        if (CredentialOrEndpointVariables.Any(
                variable => IsConfigured(_environmentReader(variable))) ||
            ProviderSelectorVariables.Any(
                variable => IsEnabled(_environmentReader(variable))))
        {
            throw RefusalException();
        }

        var configuredDirectory = _environmentReader("CLAUDE_CONFIG_DIR");
        var userConfigurationDirectory = string.IsNullOrWhiteSpace(configuredDirectory)
            ? _defaultConfigurationDirectory
            : Path.GetFullPath(configuredDirectory);
        var settingsDirectory = Path.Combine(projectFolder, ".claude");
        var settingsFiles = new[]
        {
            Path.Combine(userConfigurationDirectory, "settings.json"),
            Path.Combine(settingsDirectory, "settings.json"),
            Path.Combine(settingsDirectory, "settings.local.json"),
        };

        foreach (var settingsFile in settingsFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (ContainsBillingOverride(settingsFile))
            {
                throw RefusalException();
            }
        }
    }

    private static bool ContainsBillingOverride(string settingsFilePath)
    {
        if (!File.Exists(settingsFilePath))
        {
            return false;
        }

        byte[]? contents = null;
        try
        {
            var fileInfo = new FileInfo(settingsFilePath);
            if (fileInfo.Length > MaximumSettingsFileBytes)
            {
                throw new InvalidOperationException(
                    $"Claude settings at '{settingsFilePath}' are too large for Filekin to validate safely.");
            }

            contents = File.ReadAllBytes(settingsFilePath);
            if (contents.Length > MaximumSettingsFileBytes)
            {
                throw new InvalidOperationException(
                    $"Claude settings at '{settingsFilePath}' changed while Filekin was validating them.");
            }

            return ContainsBillingOverride(contents);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidOperationException(
                $"Filekin could not safely validate Claude settings at '{settingsFilePath}'.",
                exception);
        }
        finally
        {
            if (contents is not null)
            {
                CryptographicOperations.ZeroMemory(contents);
            }
        }
    }

    private static bool ContainsBillingOverride(ReadOnlySpan<byte> json)
    {
        if (json.Length >= 3 && json[0] == 0xEF && json[1] == 0xBB && json[2] == 0xBF)
        {
            json = json[3..];
        }

        var reader = new Utf8JsonReader(
            json,
            new JsonReaderOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Claude settings must contain a JSON object.");
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Claude settings contain an invalid property.");
            }

            var isEnvironment = reader.ValueTextEquals("env");
            var isApiKeyHelper = reader.ValueTextEquals("apiKeyHelper");
            if (!reader.Read())
            {
                throw new JsonException("Claude settings contain an incomplete property.");
            }

            if (isApiKeyHelper && IsConfigured(ref reader))
            {
                return true;
            }

            if (isEnvironment)
            {
                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    throw new JsonException("Claude settings 'env' must be an object.");
                }

                if (EnvironmentContainsBillingOverride(ref reader))
                {
                    return true;
                }

                continue;
            }

            reader.Skip();
        }

        return reader.TokenType == JsonTokenType.EndObject
            ? false
            : throw new JsonException("Claude settings contain an incomplete JSON object.");
    }

    private static bool EnvironmentContainsBillingOverride(ref Utf8JsonReader reader)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Claude settings 'env' contains an invalid property.");
            }

            var variableName = reader.GetString()
                ?? throw new JsonException("Claude settings contain an invalid environment variable name.");
            if (!reader.Read())
            {
                throw new JsonException("Claude settings contain an incomplete environment variable.");
            }

            if (CredentialOrEndpointVariableSet.Contains(variableName) && IsConfigured(ref reader))
            {
                return true;
            }

            if (ProviderSelectorVariableSet.Contains(variableName) && IsEnabled(ref reader))
            {
                return true;
            }

            reader.Skip();
        }

        return reader.TokenType == JsonTokenType.EndObject
            ? false
            : throw new JsonException("Claude settings contain an incomplete environment object.");
    }

    private static bool IsConfigured(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.Null => false,
        JsonTokenType.String => reader.HasValueSequence
            ? reader.ValueSequence.Length > 0
            : reader.ValueSpan.Length > 0,
        _ => true,
    };

    private static bool IsEnabled(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.True => true,
        JsonTokenType.String => IsEnabled(reader.GetString()),
        JsonTokenType.Number => reader.TryGetInt32(out var value) && value == 1,
        _ => false,
    };

    private static bool IsConfigured(string? value) => !string.IsNullOrWhiteSpace(value);

    private static bool IsEnabled(string? value) =>
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static InvalidOperationException RefusalException() =>
        new(
            "Filekin refused to start Claude Code because the project could select separately billed API, cloud-provider, profile, federation, or gateway authentication. Filekin did not extract or retain the configured credential value.");
}
