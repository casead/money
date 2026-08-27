using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using MoneyRecord.API.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using MoneyRecord.API.Middleware;
using MoneyRecord.Application;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Infrastructure;
using MoneyRecord.Infrastructure.Persistence;
using MoneyRecord.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

// Disable config file watching on Render free tier (inotify limit=128).
// Config changes require a redeploy anyway.
builder.Configuration.Sources.Clear();
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables();

// Serilog bootstrap (ARCH-006 §18): console + rolling file, traceId enrichment
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/moneyrecord-.log", rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30));

// Layer registrations (Clean Architecture composition)
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ---- JWT Authentication (ARCH-006 §13) ----
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt configuration section missing.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = "roleId",
            NameClaimType = "unique_name"
        };
    });
builder.Services.AddAuthorization();

// Permission-based policies (Module 3 RBAC registry): [Authorize(Policy = "user.manage")] etc.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

// Context accessors for handlers (ICurrentUser from JWT claims; IRequestContext from HTTP)
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IRequestContext, RequestContext>();

// Rate limiting (SEC-006):
//   auth-login   5/min per IP       → credential-stuffing guard
//   txn-create   30/min per user    → transaction spam/abuse guard (API-007 TXN)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth-login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1)
            }));
    options.AddPolicy("txn-create", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            // Requires auth to run BEFORE the limiter (see middleware order below).
            partitionKey: context.User.FindFirst("sub")?.Value
                          ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1)
            }));
});

// Health checks (M1 acceptance criterion):
//   /health       → liveness only (process up, no dependencies)
//   /health/ready → readiness incl. SQL Server connectivity ping
builder.Services.AddHealthChecks()
    .AddDbContextCheck<MoneyRecordDbContext>(
        "database",
        tags: ["ready"]);

var app = builder.Build();

// TraceId into response + log scope for end-to-end correlation
app.Use(async (context, next) =>
{
    var traceId = context.TraceIdentifier;
    context.Items["TraceId"] = traceId;
    context.Response.Headers["X-Trace-Id"] = traceId;
    using (Serilog.Context.LogContext.PushProperty("TraceId", traceId))
    {
        await next();
    }
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseErrorHandling();

app.UseHttpsRedirection();

// Authentication must precede the limiter so the txn-create policy can partition by user id.
app.UseAuthentication();

app.UseRateLimiter();

app.UseAuthorization();

app.MapControllers();

// Liveness: /health (liveness-only) — readiness incl. DB: /health/ready (tag: ready)
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

// Apply migrations + bootstrap admin account (dev/local only; production uses migration bundles)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await MoneyRecord.Infrastructure.Persistence.Seeding.AdminSeeder.SeedAsync(scope.ServiceProvider);
}

app.Run();

/// <summary>Exposed for WebApplicationFactory integration tests.</summary>
public partial class Program { }
