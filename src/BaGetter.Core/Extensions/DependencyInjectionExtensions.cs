using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using BaGetter.Core.Statistics;
using BaGetter.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BaGetter.Core;

public static partial class DependencyInjectionExtensions
{
    public static IServiceCollection AddBaGetterApplication(
        this IServiceCollection services,
        Action<BaGetterApplication> configureAction)
    {
        var app = new BaGetterApplication(services);

        services.AddConfiguration();
        services.AddBaGetServices();
        services.AddDefaultProviders();

        configureAction(app);

        services.AddFallbackServices();

        return services;
    }

    /// <summary>
    /// Configures and validates options.
    /// </summary>
    /// <typeparam name="TOptions">The options type that should be added.</typeparam>
    /// <param name="services">The dependency injection container to add options.</param>
    /// <param name="key">
    /// The configuration key that should be used when configuring the options.
    /// If null, the root configuration will be used to configure the options.
    /// </param>
    /// <returns>The dependency injection container.</returns>
    public static IServiceCollection AddBaGetterOptions<TOptions>(
        this IServiceCollection services,
        string key = null)
        where TOptions : class
    {
        services.AddSingleton<IValidateOptions<TOptions>>(new ValidateBaGetterOptions<TOptions>(key));
        services.AddSingleton<IConfigureOptions<TOptions>>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            if (key != null)
            {
                config = config.GetSection(key);
            }

            return new BindOptions<TOptions>(config);
        });

        return services;
    }

    private static void AddConfiguration(this IServiceCollection services)
    {
        services.AddBaGetterOptions<BaGetterOptions>();
        services.AddBaGetterOptions<DatabaseOptions>(nameof(BaGetterOptions.Database));
        services.AddBaGetterOptions<FileSystemStorageOptions>(nameof(BaGetterOptions.Storage));
        services.AddBaGetterOptions<MirrorOptions>(nameof(BaGetterOptions.Mirror));
        services.AddBaGetterOptions<SearchOptions>(nameof(BaGetterOptions.Search));
        services.AddBaGetterOptions<StorageOptions>(nameof(BaGetterOptions.Storage));
        services.AddBaGetterOptions<StatisticsOptions>(nameof(BaGetterOptions.Statistics));
    }

    private static void AddBaGetServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IFrameworkCompatibilityService, FrameworkCompatibilityService>();
        services.TryAddSingleton<IPackageDownloadsSource, PackageDownloadsJsonSource>();

        services.TryAddSingleton<ISearchResponseBuilder, SearchResponseBuilder>();
        services.TryAddSingleton<NuGetClient>();
        services.TryAddSingleton<NullSearchIndexer>();
        services.TryAddSingleton<NullSearchService>();
        services.TryAddSingleton<RegistrationBuilder>();
        services.TryAddSingleton<SystemTime>();
        services.TryAddSingleton<ValidateStartupOptions>();

        services.TryAddSingleton(HttpClientFactory);
        services.TryAddSingleton(NuGetClientFactoryFactory);

        services.TryAddScoped<DownloadsImporter>();

        services.TryAddTransient<IAuthenticationService, ApiKeyAuthenticationService>();
        services.TryAddTransient<IPackageContentService, DefaultPackageContentService>();
        services.TryAddTransient<IPackageDeletionService, PackageDeletionService>();
        services.TryAddTransient<IPackageIndexingService, PackageIndexingService>();
        services.TryAddTransient<IPackageMetadataService, DefaultPackageMetadataService>();
        services.TryAddTransient<IPackageService, PackageService>();
        services.TryAddTransient<IPackageStorageService, PackageStorageService>();
        services.TryAddTransient<IServiceIndexService, BaGetterServiceIndex>();
        services.TryAddTransient<ISymbolIndexingService, SymbolIndexingService>();
        services.TryAddTransient<ISymbolStorageService, SymbolStorageService>();
        services.TryAddTransient<IStatisticsService, StatisticsService>();

        services.TryAddTransient<DatabaseSearchService>();
        services.TryAddTransient<FileStorageService>();
        services.TryAddTransient<PackageService>();
        services.TryAddTransient<V2UpstreamClient>();
        services.TryAddTransient<V3UpstreamClient>();
        services.TryAddTransient<DisabledUpstreamClient>();
        services.TryAddSingleton<NullStorageService>();
        services.TryAddTransient<PackageDatabase>();

        services.TryAddTransient(UpstreamClientFactory);
    }

    private static void AddDefaultProviders(this IServiceCollection services)
    {
        services.AddProvider((provider, configuration) =>
        {
            if (!configuration.HasSearchType("null")) return null;

            return provider.GetRequiredService<NullSearchService>();
        });

        services.AddProvider((provider, configuration) =>
        {
            if (!configuration.HasSearchType("null")) return null;

            return provider.GetRequiredService<NullSearchIndexer>();
        });

        services.AddProvider<IStorageService>((provider, configuration) =>
        {
            if (configuration.HasStorageType("filesystem"))
            {
                return provider.GetRequiredService<FileStorageService>();
            }

            if (configuration.HasStorageType("null"))
            {
                return provider.GetRequiredService<NullStorageService>();
            }

            return null;
        });
    }

    private static void AddFallbackServices(this IServiceCollection services)
    {
        services.TryAddScoped<IContext, NullContext>();

        // BaGetter's services have multiple implementations that live side-by-side.
        // The application will choose the implementation using one of two ways:
        //
        // 1. Using the first implementation that was registered in the dependency injection
        //    container. This is the strategy used by applications that embed BaGetter.
        // 2. Using "providers". The providers will examine the application's configuration to
        //    determine whether its service implementation is active. Thsi is the strategy used
        //    by the default BaGetter application.
        //
        // BaGetter has database and search services, but the database services are special
        // in that they may also act as search services. If an application registers the
        // database service first and the search service second, the application should
        // use the search service even though it wasn't registered first. Furthermore,
        // if an application registers a database service without a search service, the
        // database service should be used for search. This effect is achieved by deferring
        // the database search service's registration until the very end.
        services.TryAddTransient<ISearchIndexer>(provider => provider.GetRequiredService<NullSearchIndexer>());
        services.TryAddTransient<ISearchService>(provider => provider.GetRequiredService<DatabaseSearchService>());
    }

    private static HttpClient HttpClientFactory(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptions<MirrorOptions>>().Value;

        var assembly = Assembly.GetEntryAssembly();
        var assemblyName = assembly.GetName().Name;
        var assemblyVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        HttpClient httpClient;

        if (!string.IsNullOrEmpty(options.Username) && !string.IsNullOrEmpty(options.Password))
        {
            var credentials = new NetworkCredential(options.Username, options.Password);
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                Credentials = credentials,
            };
            httpClient = new HttpClient(handler);
        }
        else
        {
            httpClient = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            });
        }

        httpClient.DefaultRequestHeaders.Add("User-Agent", $"{assemblyName}/{assemblyVersion}");
        httpClient.Timeout = TimeSpan.FromSeconds(options.PackageDownloadTimeoutSeconds);

        return httpClient;
    }

    private static NuGetClientFactory NuGetClientFactoryFactory(IServiceProvider provider)
    {
        var httpClient = provider.GetRequiredService<HttpClient>();
        var options = provider.GetRequiredService<IOptions<MirrorOptions>>();

        return new NuGetClientFactory(
            httpClient,
            options.Value.PackageSource.ToString());
    }

    private static IUpstreamClient UpstreamClientFactory(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptionsSnapshot<MirrorOptions>>();

        return options.Value.Enabled switch
        {
            false => provider.GetRequiredService<DisabledUpstreamClient>(),
            true when options.Value.Legacy => provider.GetRequiredService<V2UpstreamClient>(),
            _ => provider.GetRequiredService<V3UpstreamClient>()
        };
    }
}
