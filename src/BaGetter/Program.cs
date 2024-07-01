using System;
using System.Threading;
using System.Threading.Tasks;
using BaGetter;
using BaGetter.Core;
using BaGetter.Web;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


        var host = CreateHostBuilder(args).Build();
        if (!host.ValidateStartupOptions())
        {
            return;
}
await host.RunAsync();

static IHostBuilder CreateHostBuilder(string[] args)
{
    return Host
        .CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((ctx, config) =>
        {
            var root = Environment.GetEnvironmentVariable("BAGET_CONFIG_ROOT");

            if (!string.IsNullOrEmpty(root))
            {
                config.SetBasePath(root);
            }

            // Optionally load secrets from files in the conventional path
            config.AddKeyPerFile("/run/secrets", optional: true);
        })
        .ConfigureWebHostDefaults(web =>
        {
            web.ConfigureKestrel(options =>
            {
                // Remove the upload limit from Kestrel. If needed, an upload limit can
                // be enforced by a reverse proxy server, like IIS.
                options.Limits.MaxRequestBodySize = null;
            });

            web.UseStartup<Startup>();
        });
}
