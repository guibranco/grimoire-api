namespace Grimoire.Api.Controllers;

/// <summary>Problem <c>detail</c> texts shared by the controllers, so clients see one wording.</summary>
internal static class ProblemMessages
{
    public const string ApplicationNotFound = "Application not found.";
    public const string EnvironmentNotFound = "Environment not found.";
    public const string SecretNotFound = "Secret not found.";
    public const string GlobalSecretNotFound = "Global secret not found.";
    public const string ConfigurationNotFound = "Configuration not found.";
    public const string GlobalConfigurationNotFound = "Global configuration not found.";
    public const string SecretVersionNotFound = "Secret version not found.";
    public const string RevisionNotFound = "Revision not found.";
    public const string FeatureFlagNotFound = "Feature flag not found.";
    public const string FeatureFlagExists = "A feature flag with this key already exists.";

    public static string RevisionIsDelete(int revision) =>
        $"Revision {revision} deleted the key, so it has no value to restore.";
}
