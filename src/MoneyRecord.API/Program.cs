using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Context;
using MoneyRecord.API.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using MoneyRecord.API.Middleware;
using MoneyRecord.Application;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Infrastructure;
using MoneyRecord.Infrastructure.Persistence;
using MoneyRecord.Infrastructure.Persistence.Seeding;
using MoneyRecord.Infrastructure.Security;
using MongoDB.Driver;

// Build config WITHOUT file watchers (avoids inotify crash on Render free tier).
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json",
        optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((_, config) =>
    {
        config.Sources.Clear();
        config.AddConfiguration(configuration);
    })
    .UseSerilog((context, services, loggerConfig) =>
    {
        loggerConfig
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console();
        // File logging only outside Vercel (no persistent storage in containers)
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VERCEL")))
        {
            loggerConfig.WriteTo.File("logs/moneyrecord-.log",
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30);
        }
    })
    .ConfigureWebHostDefaults(webBuilder =>
    {
        webBuilder.ConfigureKestrel(options =>
        {
            var port = int.Parse(Environment.GetEnvironmentVariable("PORT") ?? "8080");
            options.ListenAnyIP(port, o => o.Protocols = HttpProtocols.Http1AndHttp2);
        });
        webBuilder.ConfigureServices(services =>
        {
            // Layer registrations (Clean Architecture composition)
            services.AddApplication();
            services.AddInfrastructure(configuration);
            services.AddControllers();
            services.AddEndpointsApiExplorer();
            if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
                services.AddSwaggerGen();

            // ---- JWT Authentication (ARCH-006 §13) ----
            var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
                ?? throw new InvalidOperationException("Jwt configuration section missing.");

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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
            services.AddAuthorization();

            // Permission-based policies (Module 3 RBAC registry)
            services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
            services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

            // Context accessors for handlers
            services.AddHttpContextAccessor();
            services.AddScoped<ICurrentUser, CurrentUser>();
            services.AddScoped<IRequestContext, RequestContext>();

            // Rate limiting (SEC-006)
            services.AddRateLimiter(options =>
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
                        partitionKey: context.User.FindFirst("sub")?.Value
                                      ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 30,
                            Window = TimeSpan.FromMinutes(1)
                        }));
            });

            // Health checks
            services.AddHealthChecks();
        });
        webBuilder.Configure(app =>
        {
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

            var env = app.ApplicationServices.GetRequiredService<IHostEnvironment>();
            if (env.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseErrorHandling();
            app.UseHttpsRedirection();
            app.UseRouting();
            app.UseAuthentication();
            app.UseRateLimiter();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHealthChecks("/health", new HealthCheckOptions
                {
                    Predicate = _ => false
                });
                endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
                {
                    Predicate = _ => false
                });
            });

            // Create MongoDB indexes in background (non-blocking).
            _ = Task.Run(async () =>
            {
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        using var scope = ((IApplicationBuilder)app).ApplicationServices.CreateScope();
                        var mongoClient = scope.ServiceProvider.GetRequiredService<IMongoClient>();
                        var database = mongoClient.GetDatabase(
                            configuration.GetValue<string>("MongoDb:DatabaseName") ?? "moneyrecord");
                        await MongoIndexInitializer.InitializeAsync(database);
                        return;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[MongoDB Indexes] Attempt {attempt}/3 failed: {ex.Message}");
                        if (attempt < 3) await Task.Delay(500);
                    }
                }
            });

            // Seed reference data first (Roles, Permissions, WalletProviders, etc.)
            // THEN seed admin user (needs Roles to exist for login roleCode lookup)
            _ = Task.Run(async () =>
            {
                using var scope = ((IApplicationBuilder)app).ApplicationServices.CreateScope();
                await ReferenceDataSeeder.SeedAsync(scope.ServiceProvider);
                await AdminSeeder.SeedAsync(scope.ServiceProvider);
            });
        });
    })
    .Build();

await host.RunAsync();

/// <summary>Exposed for WebApplicationFactory integration tests.</summary>
public partial class Program { }
