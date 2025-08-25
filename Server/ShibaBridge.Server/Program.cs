using Microsoft.AspNetCore.RateLimiting;
using ShibaBridge.Server.Hubs;
using ShibaBridge.Server.Services;
using ShibaBridge.API.SignalR;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddSimpleConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// Core services
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Simple in-memory services for demo purposes
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<PairingService>();
builder.Services.AddSingleton<FileTransferService>();

// Basic rate limiting per remote IP
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
app.MapHub<SyncHub>(IShibaBridgeHub.Path);

// Health endpoint used by admins or orchestrators
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
