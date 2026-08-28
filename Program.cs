// Program.cs

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using PayrollSystem.API.Data;
using PayrollSystem.API.Services;

var builder = WebApplication.CreateBuilder(args);

// ============================================================
// SERVICES
// ============================================================

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// ============================================================
// SWAGGER
// ============================================================

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Payroll System API",
        Version = "v1"
    });

    // JWT Bearer Authentication
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,

        Description =
            "Enter your JWT token. " +
            "Example: eyJhbGciOiJIUzI1NiIs..."
    });

    // Apply JWT authentication to Swagger endpoints
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ============================================================
// DATABASE
// ============================================================

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// ============================================================
// MEMORY CACHE
// ============================================================

builder.Services.AddMemoryCache();

// ============================================================
// JWT AUTHENTICATION
// ============================================================

var secret = builder.Configuration["JwtSettings:SecretKey"]
    ?? throw new InvalidOperationException(
        "JwtSettings:SecretKey is not configured. " +
        "Set it via environment variable or secrets.");

if (Encoding.UTF8.GetByteCount(secret) < 32)
{
    throw new InvalidOperationException(
        "JwtSettings:SecretKey must be at least 32 bytes long.");
}

var key = Encoding.UTF8.GetBytes(secret);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme =
        JwtBearerDefaults.AuthenticationScheme;

    options.DefaultChallengeScheme =
        JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;

    options.TokenValidationParameters = new TokenValidationParameters
    {
        // Validate signing key
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),

        // Validate issuer
        ValidateIssuer = true,
        ValidIssuer =
            builder.Configuration["JwtSettings:Issuer"]
            ?? "your-issuer",

        // Validate audience
        ValidateAudience = true,
        ValidAudience =
            builder.Configuration["JwtSettings:Audience"]
            ?? "your-audience",

        // Validate token expiration
        ValidateLifetime = true,

        // Allow a small clock difference
        ClockSkew = TimeSpan.FromMinutes(1)
    };
});

// ============================================================
// SERVICES
// ============================================================

builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<IOTPService, OTPService>();

// ============================================================
// HEALTH CHECKS
// ============================================================

builder.Services.AddHealthChecks();

// ============================================================
// BUILD APPLICATION
// ============================================================

var app = builder.Build();

// ============================================================
// SWAGGER
// ============================================================

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();

    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json",
            "Payroll System API v1");

        c.RoutePrefix = "swagger";
    });
}

// ============================================================
// HTTPS
// ============================================================

app.UseHttpsRedirection();

// ============================================================
// CUSTOM CORS MIDDLEWARE
// ============================================================

app.Use(async (context, next) =>
{
    // Allow requests from any origin
    context.Response.Headers["Access-Control-Allow-Origin"] = "*";

    // Allowed HTTP methods
    context.Response.Headers["Access-Control-Allow-Methods"] =
        "GET, POST, PUT, DELETE, OPTIONS";

    // Allowed request headers
    context.Response.Headers["Access-Control-Allow-Headers"] =
        "Content-Type, Authorization, ngrok-skip-browser-warning";

    // Handle OPTIONS preflight requests
    if (context.Request.Method == "OPTIONS")
    {
        context.Response.StatusCode = 200;
        await context.Response.CompleteAsync();
        return;
    }

    try
    {
        await next();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");

        context.Response.StatusCode = 500;

        await context.Response.WriteAsync(
            $"An error occurred: {ex.Message}");
    }
});

// ============================================================
// AUTHENTICATION & AUTHORIZATION
// ============================================================

app.UseAuthentication();
app.UseAuthorization();

// ============================================================
// CONTROLLERS
// ============================================================

app.MapControllers();

// ============================================================
// HEALTH CHECK
// ============================================================

app.MapHealthChecks("/health");

// ============================================================
// RUN APPLICATION
// ============================================================

app.Run();