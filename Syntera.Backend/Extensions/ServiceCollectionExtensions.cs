using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Syntera.Backend.Services;
using Syntera.Backend.Services;
using Syntera.Backend.Data;
using Syntera.Backend.Services;
using System.Text;
using System.Text.Json.Serialization;

namespace Syntera.Backend.Extensions;

/// <summary>
/// All cross-cutting DI registrations. Split by concern: MVC, OpenAPI,
/// Security (CORS + rate limit + JWT). Persistence, Identity, and
/// Application services are now registered directly in Program.cs
/// because the registration graph is small enough to read in one place.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSynteraMvc(this IServiceCollection services)
    {
        services.AddControllers()
            .ConfigureApiBehaviorOptions(options =>
            {
                options.SuppressModelStateInvalidFilter = false;
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
                options.JsonSerializerOptions.WriteIndented = false;
                options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.Converters.Add(
                    new System.Text.Json.Serialization.JsonStringEnumConverter());
            });
        return services;
    }

    public static IServiceCollection AddSynteraOpenApi(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new()
            {
                Title = "Syntera IAM API",
                Version = "v1",
                Description = "Multi-tenant Identity & Access Management platform for Syntera-affiliated pharmaceutical sites.",
            });
            options.AddSecurityDefinition("Bearer", new()
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Paste the JWT returned by POST /api/auth/login.",
            });
            options.AddSecurityRequirement(new()
            {
                {
                    new() { Reference = new() { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
                    Array.Empty<string>()
                }
            });
        });
        return services;
    }

    public static IServiceCollection AddSynteraSecurity(this IServiceCollection services, IConfiguration cfg)
    {
        // CORS — fail-closed: if no origins configured, REJECT all cross-origin requests.
        // The original code allowed AllowAnyOrigin() as fallback, which is unsafe.
        services.AddCors(o => o.AddDefaultPolicy(p =>
        {
            var origins = cfg.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            var devOrigins = cfg.GetSection("Cors:DevOrigins").Get<string[]>() ?? [];
            var allOrigins = origins.Concat(devOrigins).Distinct().ToArray();
            if (allOrigins.Length == 0)
            {
                // No origins → block all CORS in Production. In Development, allow localhost.
                if (cfg["ASPNETCORE_ENVIRONMENT"] == "Development")
                {
                    p.SetIsOriginAllowed(s => s.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase))
                     .AllowAnyHeader().AllowAnyMethod().AllowCredentials();
                }
                else
                {
                    // Empty policy — no origin will match.
                    p.SetIsOriginAllowed(_ => false)
                     .AllowAnyHeader().AllowAnyMethod();
                }
            }
            else
            {
                p.WithOrigins(allOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
            }
        }));

        // Rate limit: default 500/min; login endpoint gets stricter limit.
        services.AddRateLimiter(o =>
        {
            o.AddFixedWindowLimiter("default", opt =>
            {
                opt.PermitLimit = 500;
                opt.Window = TimeSpan.FromMinutes(1);
            });
            o.AddFixedWindowLimiter("auth", opt =>
            {
                opt.PermitLimit = 20; // 20 attempts per minute per IP
                opt.Window = TimeSpan.FromMinutes(1);
            });
            o.OnRejected = async (ctx, ct) =>
            {
                ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                ctx.HttpContext.Response.ContentType = "application/json";
                await ctx.HttpContext.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    errorCode = "RATE_LIMITED",
                    message = "Too many requests. Please slow down.",
                }, ct);
            };
        });

        // JWT authentication.
        var jwtKey = cfg["Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(jwtKey))
            throw new InvalidOperationException("Jwt:SigningKey is required.");
        if (jwtKey.Length < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be ≥ 32 chars.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = cfg["ASPNETCORE_ENVIRONMENT"] != "Development";
                options.SaveToken = false;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new()
                {
                    ValidateIssuer = true,
                    ValidIssuer = "syntera",
                    ValidateAudience = true,
                    ValidAudience = "syntera-api",
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtKey)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorization(options =>
        {
            options.DefaultPolicy = new Microsoft.AspNetCore.Authorization
                .AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }
}
