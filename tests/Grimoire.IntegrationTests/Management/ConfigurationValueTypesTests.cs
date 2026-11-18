using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Grimoire.IntegrationTests.Management;

public class ConfigurationValueTypesTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    private HttpClient Client => factory.CreateManagementClient();

    [Fact]
    public async Task Create_WithoutValueType_DefaultsToString()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);

        var response = await Client.PostAsJsonAsync(
            $"/management/applications/{slug}/configurations",
            new
            {
                environmentSlug = "local",
                key = "Plain",
                value = "[not parsed]",
            }
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal("string", json["valueType"]!.GetValue<string>());
        Assert.Equal("[not parsed]", json["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task Create_Array_FromLines_StoresJsonArray_AndConsumerSeesType()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(Client);

        var response = await Client.PostAsJsonAsync(
            $"/management/applications/{slug}/configurations",
            new
            {
                environmentSlug = "local",
                key = "AllowedIps",
                value = "10.0.0.1\n10.0.0.2\n",
                valueType = "array",
            }
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal("array", created["valueType"]!.GetValue<string>());
        Assert.Equal("[\"10.0.0.1\",\"10.0.0.2\"]", created["value"]!.GetValue<string>());

        var consumer = factory.CreateConsumerClient(apiKey);
        var item = JsonNode.Parse(
            await consumer.GetStringAsync("/consumer/configurations/AllowedIps?environment=local")
        )!;
        Assert.Equal("array", item["valueType"]!.GetValue<string>());
        Assert.Equal("[\"10.0.0.1\",\"10.0.0.2\"]", item["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task Create_InvalidJson_Returns400()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);

        var response = await Client.PostAsJsonAsync(
            $"/management/applications/{slug}/configurations",
            new
            {
                environmentSlug = "local",
                key = "Smtp",
                value = "{ host: ",
                valueType = "json",
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("JSON", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Create_UnknownValueType_Returns400()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);

        var response = await Client.PostAsJsonAsync(
            $"/management/applications/{slug}/configurations",
            new
            {
                environmentSlug = "local",
                key = "K",
                value = "v",
                valueType = "yaml",
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithoutValueType_KeepsTypeAndValidatesAgainstIt()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        await Client.PostAsJsonAsync(
            $"/management/applications/{slug}/configurations",
            new
            {
                environmentSlug = "local",
                key = "Smtp",
                value = "{\"host\":\"a\"}",
                valueType = "json",
            }
        );

        var invalid = await Client.PutAsJsonAsync(
            $"/management/applications/{slug}/configurations/local/Smtp",
            new { value = "not json" }
        );
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var valid = await Client.PutAsJsonAsync(
            $"/management/applications/{slug}/configurations/local/Smtp",
            new { value = "{ \"host\": \"b\" }" }
        );
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        var json = JsonNode.Parse(await valid.Content.ReadAsStringAsync())!;
        Assert.Equal("json", json["valueType"]!.GetValue<string>());
        Assert.Equal("{\"host\":\"b\"}", json["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task Update_CanChangeValueType()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        await TestHelpers.CreateConfigurationAsync(Client, slug, "local", "Banner", "one");

        var response = await Client.PutAsJsonAsync(
            $"/management/applications/{slug}/configurations/local/Banner",
            new { value = "one\r\ntwo", valueType = "multiline" }
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal("multiline", json["valueType"]!.GetValue<string>());
        Assert.Equal("one\ntwo", json["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task GlobalArray_IsInherited_WithValueType_InEffectiveAndConsumerViews()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(Client);
        var key = TestHelpers.UniqueName("GlobalIps");

        var create = await Client.PostAsJsonAsync(
            "/management/global/configurations",
            new
            {
                key,
                value = "[\"1.1.1.1\"]",
                valueType = "array",
            }
        );
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(
            "array",
            JsonNode.Parse(await create.Content.ReadAsStringAsync())![
                "valueType"
            ]!.GetValue<string>()
        );

        var effective = JsonNode
            .Parse(
                await Client.GetStringAsync(
                    $"/management/applications/{slug}/configurations/effective?environment=local"
                )
            )!
            .AsArray();
        var entry = Assert.Single(effective, e => e!["key"]!.GetValue<string>() == key)!;
        Assert.Equal("array", entry["valueType"]!.GetValue<string>());

        var consumer = factory.CreateConsumerClient(apiKey);
        var items = JsonNode.Parse(
            await consumer.GetStringAsync("/consumer/configurations?environment=local")
        )!["items"]!.AsArray();
        var item = Assert.Single(items, i => i!["key"]!.GetValue<string>() == key)!;
        Assert.Equal("array", item["valueType"]!.GetValue<string>());

        await Client.DeleteAsync($"/management/global/configurations/*/{key}");
    }

    [Fact]
    public async Task GlobalUpdate_InvalidArray_Returns400()
    {
        var key = TestHelpers.UniqueName("GlobalArr");
        await Client.PostAsJsonAsync(
            "/management/global/configurations",
            new
            {
                key,
                value = "a",
                valueType = "array",
            }
        );

        var response = await Client.PutAsJsonAsync(
            $"/management/global/configurations/*/{key}",
            new { value = "[1, 2]" }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await Client.DeleteAsync($"/management/global/configurations/*/{key}");
    }
}
