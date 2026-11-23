namespace Grimoire.Core.Reports;

public static class DependencyNodeTypes
{
    public const string Application = "application";
    public const string Secret = "secret";
    public const string GlobalSecret = "global-secret";
    public const string ConfigKey = "config-key";
    public const string GlobalConfigKey = "global-config-key";
}

public static class DependencyEdgeKinds
{
    /// <summary>The application owns the secret or configuration key.</summary>
    public const string Owns = "owns";

    /// <summary>The application read the secret through the Consumer API.</summary>
    public const string Reads = "reads";

    /// <summary>An application entry shadows the global entry with the same name.</summary>
    public const string Overrides = "overrides";

    /// <summary>The application receives the global configuration key (it doesn't override it).</summary>
    public const string Inherits = "inherits";
}

/// <param name="Application">Owning application slug; <c>null</c> for applications and globals.</param>
/// <param name="Environments">Environment slugs the entry exists in (configuration keys only).</param>
public sealed record DependencyNode(
    string Id,
    string Type,
    string Label,
    string? Application,
    IReadOnlyList<string> Environments
);

/// <param name="Environments">For <c>reads</c>: the reading environments.</param>
public sealed record DependencyEdge(
    string Source,
    string Target,
    string Kind,
    IReadOnlyList<string> Environments,
    DateTimeOffset? LastReadAt,
    long? ReadCount
);

public sealed record DependencyGraph(
    IReadOnlyList<DependencyNode> Nodes,
    IReadOnlyList<DependencyEdge> Edges
);

public sealed record GraphApplication(Guid Id, string Slug, string Name, bool InheritGlobals);

public sealed record GraphSecret(Guid Id, Guid? ApplicationId, string Name);

/// <param name="ApplicationId"><c>null</c> for a global key.</param>
/// <param name="Environment">Environment slug ("*" for a global key that applies to all).</param>
public sealed record GraphConfigEntry(Guid? ApplicationId, string Key, string Environment);

/// <summary>Inputs for <see cref="DependencyGraphBuilder"/>; secrets with no application are global.</summary>
public sealed record DependencyGraphInput(
    IReadOnlyList<GraphApplication> Applications,
    IReadOnlyList<GraphSecret> Secrets,
    IReadOnlyList<GraphConfigEntry> Configuration,
    IReadOnlyList<SecretReadSummary> Reads
);

public static class DependencyGraphBuilder
{
    public static string ApplicationId(string slug) => $"app:{slug}";

    public static string SecretId(string slug, string name) => $"secret:{slug}/{name}";

    public static string GlobalSecretId(string name) => $"global-secret:{name}";

    public static string ConfigId(string slug, string key) => $"config:{slug}/{key}";

    public static string GlobalConfigId(string key) => $"global-config:{key}";

    /// <summary>
    /// Builds the graph: applications own their secrets and keys; application entries override
    /// globals of the same name when the application inherits globals; inheriting applications
    /// receive every global key they don't override; reads come from the read rollup.
    /// Nodes and edges are sorted for stable output.
    /// </summary>
    public static DependencyGraph Build(DependencyGraphInput input)
    {
        var apps = input.Applications.ToDictionary(a => a.Id);
        var nodes = new Dictionary<string, DependencyNode>(StringComparer.Ordinal);
        var edges = new List<DependencyEdge>();

        foreach (var app in input.Applications)
            nodes[ApplicationId(app.Slug)] = Node(
                ApplicationId(app.Slug),
                DependencyNodeTypes.Application,
                app.Name,
                null,
                []
            );

        var globalSecretNames = new HashSet<string>(StringComparer.Ordinal);
        var secretNodeIds = new Dictionary<(bool, Guid), string>();
        foreach (var secret in input.Secrets)
        {
            if (secret.ApplicationId is null)
            {
                var id = GlobalSecretId(secret.Name);
                nodes[id] = Node(id, DependencyNodeTypes.GlobalSecret, secret.Name, null, []);
                globalSecretNames.Add(secret.Name);
                secretNodeIds[(true, secret.Id)] = id;
            }
            else if (apps.TryGetValue(secret.ApplicationId.Value, out var owner))
            {
                var id = SecretId(owner.Slug, secret.Name);
                nodes[id] = Node(id, DependencyNodeTypes.Secret, secret.Name, owner.Slug, []);
                secretNodeIds[(false, secret.Id)] = id;
                edges.Add(Edge(ApplicationId(owner.Slug), id, DependencyEdgeKinds.Owns));
            }
        }

        foreach (var secret in input.Secrets)
            if (
                secret.ApplicationId is { } appId
                && apps.TryGetValue(appId, out var owner)
                && owner.InheritGlobals
                && globalSecretNames.Contains(secret.Name)
            )
                edges.Add(
                    Edge(
                        SecretId(owner.Slug, secret.Name),
                        GlobalSecretId(secret.Name),
                        DependencyEdgeKinds.Overrides
                    )
                );

        AddConfiguration(input.Configuration, apps, nodes, edges);
        AddReads(input.Reads, secretNodeIds, nodes, edges);

        return new DependencyGraph(
            nodes.Values.OrderBy(n => n.Id, StringComparer.Ordinal).ToList(),
            edges
                .OrderBy(e => e.Source, StringComparer.Ordinal)
                .ThenBy(e => e.Target, StringComparer.Ordinal)
                .ThenBy(e => e.Kind, StringComparer.Ordinal)
                .ToList()
        );
    }

    private static void AddConfiguration(
        IReadOnlyList<GraphConfigEntry> configuration,
        Dictionary<Guid, GraphApplication> apps,
        Dictionary<string, DependencyNode> nodes,
        List<DependencyEdge> edges
    )
    {
        var globalKeys = configuration
            .Where(c => c.ApplicationId is null)
            .GroupBy(c => c.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => Sorted(g.Select(c => c.Environment)));
        foreach (var (key, envs) in globalKeys)
            nodes[GlobalConfigId(key)] = Node(
                GlobalConfigId(key),
                DependencyNodeTypes.GlobalConfigKey,
                key,
                null,
                envs
            );

        var appKeys = configuration
            .Where(c => c.ApplicationId is { } id && apps.ContainsKey(id))
            .GroupBy(c => (c.ApplicationId!.Value, c.Key));
        var overridden = new HashSet<(Guid, string)>();
        foreach (var group in appKeys)
        {
            var (appId, key) = group.Key;
            var app = apps[appId];
            var id = ConfigId(app.Slug, key);
            nodes[id] = Node(
                id,
                DependencyNodeTypes.ConfigKey,
                key,
                app.Slug,
                Sorted(group.Select(c => c.Environment))
            );
            edges.Add(Edge(ApplicationId(app.Slug), id, DependencyEdgeKinds.Owns));
            if (app.InheritGlobals && globalKeys.ContainsKey(key))
            {
                edges.Add(Edge(id, GlobalConfigId(key), DependencyEdgeKinds.Overrides));
                overridden.Add((appId, key));
            }
        }

        foreach (var app in apps.Values.Where(a => a.InheritGlobals))
        foreach (var key in globalKeys.Keys.Where(k => !overridden.Contains((app.Id, k))))
            edges.Add(
                Edge(ApplicationId(app.Slug), GlobalConfigId(key), DependencyEdgeKinds.Inherits)
            );
    }

    private static void AddReads(
        IReadOnlyList<SecretReadSummary> reads,
        Dictionary<(bool, Guid), string> secretNodeIds,
        Dictionary<string, DependencyNode> nodes,
        List<DependencyEdge> edges
    )
    {
        var grouped = reads
            .Where(r => secretNodeIds.ContainsKey((r.IsGlobal, r.SecretId)))
            .GroupBy(r => (r.ApplicationSlug, Target: secretNodeIds[(r.IsGlobal, r.SecretId)]))
            .Where(g => nodes.ContainsKey(ApplicationId(g.Key.ApplicationSlug)));
        foreach (var group in grouped)
            edges.Add(
                new DependencyEdge(
                    ApplicationId(group.Key.ApplicationSlug),
                    group.Key.Target,
                    DependencyEdgeKinds.Reads,
                    Sorted(group.Select(r => r.Environment)),
                    group.Max(r => r.LastReadAt),
                    group.Sum(r => r.ReadCount)
                )
            );
    }

    private static DependencyNode Node(
        string id,
        string type,
        string label,
        string? application,
        IReadOnlyList<string> environments
    ) => new(id, type, label, application, environments);

    private static DependencyEdge Edge(string source, string target, string kind) =>
        new(source, target, kind, [], null, null);

    private static List<string> Sorted(IEnumerable<string> values) =>
        values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
}
