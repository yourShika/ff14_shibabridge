// Einstiegspunkt des ShibaBridge-Webservers.
// Startet eine ASP.NET-Core-Anwendung und richtet alle benötigten Dienste ein.
using Microsoft.AspNetCore.RateLimiting;
using ShibaBridge.Server.Hubs;
using ShibaBridge.Server.Services;
using ShibaBridge.API.SignalR;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Einfache Konsolen-Logs aktivieren und Mindestloglevel setzen
builder.Logging.AddSimpleConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// Kern-Dienste wie Controller, SignalR-Hubs und Swagger registrieren
builder.Services.AddControllers();
builder.Services.AddSignalR().AddMessagePackProtocol();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Einfache In-Memory-Services für Demonstrationszwecke
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<PairingService>();
builder.Services.AddSingleton<FileTransferService>();

// Einfache Ratenbegrenzung pro Remote-IP konfigurieren
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            }));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRateLimiter();
app.MapControllers();
app.MapHub<ShibaBridgeHub>(IShibaBridgeHub.Path);

// Health-Endpoint für Administratoren oder Orchestrierungssysteme
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
