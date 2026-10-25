using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Flowarr.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Flowarr.Infrastructure.Data;
using Flowarr.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var dataProtectionPath = builder.Configuration["Flowarr:Security:DataProtectionKeysPath"] ?? "/var/lib/flowarr/keys";
Directory.CreateDirectory(dataProtectionPath);

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "flowarr.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        }

        await context.HttpContext.Response.WriteAsJsonAsync(
            new { message = "Too many sign-in attempts. Wait a minute and try again." },
            cancellationToken);
    };
    // Partitioned by client address, which UseForwardedHeaders resolves from the proxy's
    // X-Forwarded-For, so each client behind the reverse proxy gets its own budget.
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath))
    .SetApplicationName("Flowarr");
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // The reverse proxy (nginx, Cloudflare Tunnel, etc.) sits on a network we can't know in
    // advance, so trust forwarded headers from any address rather than only loopback.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddFlowarrInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseForwardedHeaders();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Frontend");
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        if (context.File.Name == "sw.js")
        {
            context.Context.Response.Headers.CacheControl = "no-cache";
        }
    }
});
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllers();
app.MapFallbackToFile("index.html").AllowAnonymous();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<FlowarrDbContext>();
    dbContext.Database.Migrate();

    // Hosted services only start in app.Run(), so nothing is running yet: any run still marked
    // Running was left behind by a previous process and would otherwise look in-progress forever.
    var interrupted = await scope.ServiceProvider.GetRequiredService<IJobRunService>().FailInterruptedRunsAsync();
    if (interrupted > 0)
    {
        app.Logger.LogWarning("Marked {Count} job run(s) left Running by a previous process as interrupted", interrupted);
    }
}

app.Run();
