using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Syntera.Application.Interfaces;
using Syntera.Application.Interfaces.Services;
using Syntera.Application.Services;
using Syntera.Application.Validators;
using Syntera.Infrastructure.Data;
using Syntera.Infrastructure.Identity;
using Syntera.Infrastructure.Repositories;
using System.Text;

namespace Syntera.Api.Extensions;

/// <summary>
/// All cross-cutting DI registrations. Splitting by concern keeps
/// Program.cs tiny and the wiring auditable. Each method maps to
/// a logical layer (Persistence, Identity, Application, OpenAPI,
/// Security) and is idempotent.
/// </summary>
public static class ServiceCollectionExtensions
{
    // ─── Persistence ─────────────────────────────────────────────
    public static IServiceCollection AddSynteraPersistence(
        this IServiceCollection services, IConfiguration cfg)
    {
        var conn = cfg.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default missing.");

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlServer(conn, sql =>
            {
                sql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(8),
                    errorNumbersToAdd: null);
                sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
            });
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
            if (cfg.GetValue("EFCore:EnableSensitiveDataLogging", false))
                options.EnableSensitiveDataLogging();
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ISupplierRepository, SupplierRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ISaleRepository, SaleRepository>();

        return services;
    }

    // ─── Identity + JWT ──────────────────────────────────────────
    public static IServiceCollection AddSynteraIdentity(
        this IServiceCollection services, IConfiguration cfg)
    {
        services.AddIdentity<IdentityUser, IdentityRole>(options =>
        {
            options.Password.RequiredLength = 8;
            options.Password.RequireDigit = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.User.RequireUniqueEmail = true;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.MaxFailedAccessAttempts = 5;
        })
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

        var jwtKey = cfg["Jwt:SigningKey"]
            ?? throw new InvalidOperationException(
                "Jwt:SigningKey missing. Configure via User Secrets (dev) or env var (prod).");
        if (jwtKey.Length < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be ≥ 32 chars (HS256).");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = !cfg.GetValue("DevMode", false);
                options.SaveToken = true;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new()
                {
                    ValidateIssuer = true,
                    ValidIssuer = cfg["Jwt:Issuer"],
                    ValidateAudience = true,
                    ValidAudience = cfg["Jwt:Audience"],
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtKey)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddScoped<ICurrentUserService, CurrentUserService>();
        return services;
    }

    // ─── Application services ───────────────────────────────────
    public static IServiceCollection AddSynteraApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<ISaleService, SaleService>();
        services.AddScoped<IDashboardService, DashboardService>();

        services.AddScoped<LoginRequestValidator>();
        services.AddScoped<CategoryUpsertValidator>();
        services.AddScoped<SupplierUpsertValidator>();
        services.AddScoped<CustomerUpsertValidator>();
        services.AddScoped<ProductUpsertValidator>();
        services.AddScoped<SaleCreateValidator>();

        services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();
        return services;
    }

    // ─── OpenAPI / Swagger ───────────────────────────────────────
    public static IServiceCollection AddSynteraOpenApi(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new()
            {
                Title = "Syntera Pharmaceutical API",
                Version = "v1",
                Description = "Kalbe-affiliated pharmaceutical commerce + inventory API built on .NET 10.",
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

    // ─── Security: CORS + Rate Limiting ──────────────────────────
    public static IServiceCollection AddSynteraSecurity(this IServiceCollection services, IConfiguration cfg)
    {
        services.AddCors(o => o.AddDefaultPolicy(p =>
        {
            var origins = cfg.GetSection("Cors:Origins").Get<string[]>() ?? [];
            if (origins.Length == 0)
                p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
            else
                p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }));

        services.AddRateLimiter(o =>
        {
            // Default per-process limit: 500 req/min total. The handler below
            // emits a uniform JSON envelope so the React client can react
            // consistently to 429s.
            o.AddFixedWindowLimiter("default", opt =>
            {
                opt.PermitLimit = 500;
                opt.Window = TimeSpan.FromMinutes(1);
            });
            o.AddFixedWindowLimiter("strict", opt =>
            {
                opt.PermitLimit = 60;
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

        return services;
    }
}
