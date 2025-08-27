// ConfigurationMigrator - part of ShibaBridge project.
﻿using ShibaBridge.WebAPI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ShibaBridge.ShibaBridgeConfiguration;

public class ConfigurationMigrator(ILogger<ConfigurationMigrator> logger) : IHostedService
{
    private readonly ILogger<ConfigurationMigrator> _logger = logger;

    public void Migrate()
    {
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Migrate();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
