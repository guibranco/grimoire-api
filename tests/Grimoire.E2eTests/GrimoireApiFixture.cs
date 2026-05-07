using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;

namespace Grimoire.E2eTests;

public sealed class GrimoireApiFixture : IAsyncLifetime
{
    public const string AdminKey = "e2e-admin-key-secure";
    public const string MasterKey = "e2e-master-key-32-chars-minimum!!";

    private IContainer? _container;
    private IFutureDockerImage? _builtImage;

    public HttpClient HttpClient { get; private set; } = null!;
    public string BaseUrl { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var imageName = Environment.GetEnvironmentVariable("GRIMOIRE_TEST_IMAGE");

        if (string.IsNullOrEmpty(imageName))
        {
            var tag = $"grimoire-api:e2e-{Guid.NewGuid():N}"[..30];
            _builtImage = new ImageFromDockerfileBuilder()
                .WithDockerfileDirectory(CommonDirectoryPath.GetSolutionDirectory(), string.Empty)
                .WithDockerfile("Dockerfile")
                .WithName(tag)
                .Build();
            await _builtImage.CreateAsync();
            imageName = _builtImage.FullName;
        }

        _container = new ContainerBuilder()
            .WithImage(imageName)
            .WithPortBinding(8080, true)
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
            .WithEnvironment("Management__AdminApiKey", AdminKey)
            .WithEnvironment("Encryption__MasterKey", MasterKey)
            .WithEnvironment("Cors__AllowedOrigins__0", "http://localhost:5173")
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(r => r.ForPath("/health").ForPort(8080))
            )
            .Build();

        await _container.StartAsync();

        BaseUrl = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(8080)}";
        HttpClient = new HttpClient { BaseAddress = new Uri(BaseUrl) };
    }

    public HttpClient CreateManagementClient()
    {
        var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {AdminKey}");
        return client;
    }

    public HttpClient CreateConsumerClient(string apiKey)
    {
        var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        return client;
    }

    public async Task DisposeAsync()
    {
        HttpClient.Dispose();
        if (_container is not null)
            await _container.DisposeAsync();
        if (_builtImage is not null)
            await _builtImage.DisposeAsync();
    }
}
