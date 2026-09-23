using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zenith.Core.Registry;

namespace Zenith.App.Registry;

/// <summary>Loads catalog data only. Never called by policy startup or navigation.</summary>
internal sealed class JsonServiceRegistryLoader
{
    internal const int MaximumFileBytes = 256 * 1024;
    private const int MaximumCatalogBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
        Converters =
        {
            new JsonStringEnumConverter<ServiceStatus>(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
            new JsonStringEnumConverter<DependencyType>(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false),
            new JsonStringEnumConverter<PermissionApplicability>(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false),
            new JsonStringEnumConverter<RegistrySourceType>(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false),
            new InfrastructureDependencyConverter()
        }
    };

    public Task<RegistryLoadResult> LoadBundledAsync(CancellationToken cancellationToken = default) =>
        LoadDirectoryAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Registry"), cancellationToken);

    internal async Task<RegistryLoadResult> LoadDirectoryAsync(string directory, CancellationToken cancellationToken = default)
    {
        var currentFile = "registry.json";
        var totalBytes = 0;
        try
        {
            var root = Path.GetFullPath(directory);
            var manifest = await Read<RegistryManifestDocument>("registry.json");
            if (manifest.SchemaVersion != 1) throw new InvalidDataException("Unsupported registry schema version; expected 1.");
            if (manifest.DependencyCoverage != "primary_only") throw new InvalidDataException("Schema 1 requires primary_only dependency coverage.");
            if (manifest.Services.Length is 0 or > 1000) throw new InvalidDataException("The registry must list 1–1000 service files.");
            if (manifest.Services.Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Services.Length)
                throw new InvalidDataException("The manifest contains duplicate service file references.");
            foreach (var file in manifest.Services) ValidateServicePath(file);

            var capabilities = (await Read<CapabilitiesDocument>("capabilities.json")).Capabilities.Select(c => c.ToModel()).ToArray();
            var infrastructure = (await Read<InfrastructureDocument>("infrastructure.json")).Infrastructure.Select(i => i.ToModel()).ToArray();
            var services = new List<ServiceDefinition>();
            foreach (var file in manifest.Services.Order(StringComparer.Ordinal))
            {
                var service = (await Read<ServiceDocument>(file)).ToModel();
                if (Path.GetFileNameWithoutExtension(file) != service.Id)
                    throw new InvalidDataException("Service ID must match its filename.");
                services.Add(service);
            }

            var issues = RegistryValidator.Validate(services, capabilities, infrastructure);
            if (issues.Count > 0) return RegistryLoadResult.Failure(issues);
            return RegistryLoadResult.Success(new ServiceRegistry(manifest.Revision, services, capabilities, infrastructure));

            async Task<T> Read<T>(string relativePath)
            {
                currentFile = relativePath;
                cancellationToken.ThrowIfCancellationRequested();
                await using var stream = new FileStream(Path.Combine(root, relativePath), FileMode.Open, FileAccess.Read,
                    FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var bytes = new byte[MaximumFileBytes + 1];
                var length = await stream.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, cancellationToken);
                totalBytes = checked(totalBytes + length);
                if (length > MaximumFileBytes || totalBytes > MaximumCatalogBytes)
                    throw new InvalidDataException("Registry size limit exceeded.");
                using var json = JsonDocument.Parse(bytes.AsMemory(0, length), new JsonDocumentOptions { MaxDepth = 16 });
                ValidateJson(json.RootElement);
                return json.Deserialize<T>(Options) ?? throw new InvalidDataException("Registry documents cannot be null.");
            }
        }
        catch (OperationCanceledException)
        {
            return RegistryLoadResult.Failure([new(currentFile, "Registry loading was cancelled.")]);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException or NotSupportedException)
        {
            // Diagnostics identify catalog input, never browser URLs, policy state or credentials.
            var message = error is IOException or UnauthorizedAccessException
                ? "The registry file could not be read."
                : error.Message;
            return RegistryLoadResult.Failure([new(currentFile, message)]);
        }
    }

    private static void ValidateServicePath(string file)
    {
        var stem = Path.GetFileNameWithoutExtension(file);
        if (stem.Length == 0 || stem.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '_') ||
            file != $"services/{stem}.json")
            throw new InvalidDataException("Service files must be named services/<id>.json within the bundled registry.");
    }

    private static void ValidateJson(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null) throw new InvalidDataException("Registry fields cannot be null.");
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException($"Duplicate JSON property: {property.Name}.");
                ValidateJson(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) ValidateJson(item);
    }
}
