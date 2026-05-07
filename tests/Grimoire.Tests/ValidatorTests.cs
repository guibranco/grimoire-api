using FluentValidation;
using Grimoire.Api.DTOs.Management;
using Grimoire.Api.Validators.Management;
using Xunit;

namespace Grimoire.Tests;

public class ValidatorTests
{
    private static void AssertValid<T>(AbstractValidator<T> v, T model)
    {
        var result = v.Validate(model);
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    private static void AssertInvalidFor<T>(AbstractValidator<T> v, T model, string propertyName)
    {
        var result = v.Validate(model);
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            e => e.PropertyName.Equals(propertyName, StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public void CreateApplicationRequest_ValidInput_Passes() =>
        AssertValid(
            new CreateApplicationRequestValidator(),
            new CreateApplicationRequest("My App", "desc")
        );

    [Fact]
    public void CreateApplicationRequest_EmptyName_FailsOnName() =>
        AssertInvalidFor(
            new CreateApplicationRequestValidator(),
            new CreateApplicationRequest("", null),
            "Name"
        );

    [Fact]
    public void CreateApplicationRequest_NameTooLong_FailsOnName() =>
        AssertInvalidFor(
            new CreateApplicationRequestValidator(),
            new CreateApplicationRequest(new string('a', 201), null),
            "Name"
        );

    [Fact]
    public void UpdateApplicationRequest_ValidInput_Passes() =>
        AssertValid(
            new UpdateApplicationRequestValidator(),
            new UpdateApplicationRequest("New Name", "desc")
        );

    [Fact]
    public void UpdateApplicationRequest_EmptyName_Fails() =>
        AssertInvalidFor(
            new UpdateApplicationRequestValidator(),
            new UpdateApplicationRequest("", null),
            "Name"
        );

    [Fact]
    public void CreateEnvironmentRequest_EmptyName_FailsOnName() =>
        AssertInvalidFor(
            new CreateEnvironmentRequestValidator(),
            new CreateEnvironmentRequest(""),
            "Name"
        );

    [Fact]
    public void CreateEnvironmentRequest_ValidName_Passes() =>
        AssertValid(
            new CreateEnvironmentRequestValidator(),
            new CreateEnvironmentRequest("Production")
        );

    [Fact]
    public void CreateSecretRequest_EmptyName_FailsOnName() =>
        AssertInvalidFor(
            new CreateSecretRequestValidator(),
            new CreateSecretRequest("", null),
            "Name"
        );

    [Fact]
    public void SetSecretValueRequest_EmptyEnvironmentSlug_Fails() =>
        AssertInvalidFor(
            new SetSecretValueRequestValidator(),
            new SetSecretValueRequest("", "value"),
            "EnvironmentSlug"
        );

    [Fact]
    public void SetSecretValueRequest_EmptyValue_Fails() =>
        AssertInvalidFor(
            new SetSecretValueRequestValidator(),
            new SetSecretValueRequest("local", ""),
            "Value"
        );

    [Fact]
    public void SetSecretValueRequest_ValidInput_Passes() =>
        AssertValid(
            new SetSecretValueRequestValidator(),
            new SetSecretValueRequest("local", "val", true)
        );

    [Fact]
    public void SetSecretValueRequest_ExpiresBeforeNotBefore_FailsOnExpiresAt()
    {
        var now = DateTimeOffset.UtcNow;
        AssertInvalidFor(
            new SetSecretValueRequestValidator(),
            new SetSecretValueRequest(
                "local",
                "v",
                true,
                ExpiresAt: now.AddDays(-1),
                NotBefore: now
            ),
            "ExpiresAt"
        );
    }

    [Fact]
    public void CreateConfigurationRequest_EmptyKey_Fails() =>
        AssertInvalidFor(
            new CreateConfigurationRequestValidator(),
            new CreateConfigurationRequest("local", "", "value"),
            "Key"
        );

    [Fact]
    public void CreateConfigurationRequest_EmptyEnvironmentSlug_Fails() =>
        AssertInvalidFor(
            new CreateConfigurationRequestValidator(),
            new CreateConfigurationRequest("", "key", "value"),
            "EnvironmentSlug"
        );

    [Fact]
    public void CreateConfigurationRequest_ValidInput_Passes() =>
        AssertValid(
            new CreateConfigurationRequestValidator(),
            new CreateConfigurationRequest("local", "Feature:EnableX", "true")
        );

    [Fact]
    public void UpdateConfigurationRequest_ValidInput_Passes() =>
        AssertValid(
            new UpdateConfigurationRequestValidator(),
            new UpdateConfigurationRequest("new-value")
        );
}
