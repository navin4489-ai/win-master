using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using WinMaster.Infrastructure.Data;
using WinMaster.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// Render (and most PaaS) inject the port to listen on via $PORT. Honor it, else default 10000.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddWinMasterInfrastructure(builder.Configuration);

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        // Tolerate empty-string dates (e.g. an unset datetime-local field) by treating them as null.
        o.JsonSerializerOptions.Converters.Add(new WinMaster.API.Json.NullableDateTimeConverter());
    });

builder.Services.AddHttpContextAccessor();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Win Master API", Version = "v1", Description = "WhatsApp automation, bulk messaging, mini-CRM and licensing platform." });
    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
        In = ParameterLocation.Header, Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };
    c.AddSecurityDefinition("Bearer", scheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = Array.Empty<string>() });
});

var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "winmaster-dev-super-secret-key-change-me-please-123456";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "WinMaster",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "WinMasterClients",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            RoleClaimType = ClaimTypes.Role,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddCors(o => o.AddPolicy("web", p =>
    p.WithOrigins(
        "http://localhost:4200", "http://localhost:8080", "http://localhost:5173",
        "https://winmaster.wintechinov.com", "https://win-master.onrender.com")
     .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Apply migrations / create schema + seed — resilient so a cold/slow DB never prevents the
// web server from starting (Render marks the deploy failed if no port opens).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    // Migrations are generated for PostgreSQL. Apply them only on a Postgres provider;
    // for the SQLite dev fallback, create the schema directly from the model.
    var usePostgres = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
    for (var attempt = 1; attempt <= 10; attempt++)
    {
        try
        {
            if (usePostgres)
                db.Database.Migrate();
            else
                db.Database.EnsureCreated();
            DbSeeder.Seed(db);
            logger.LogInformation("Database ready (attempt {Attempt}).", attempt);
            break;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DB init attempt {Attempt}/10 failed; retrying…", attempt);
            if (attempt == 10) logger.LogError(ex, "DB init gave up; API will still start.");
            else Thread.Sleep(3000);
        }
    }
}

app.UseMiddleware<WinMaster.API.ErrorHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("web");

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<WinMaster.API.ActivityAuditMiddleware>();

app.MapControllers();
// Liveness: return 200 as soon as the web server is up so Render marks the deploy healthy.
// DB connectivity is reported as a sub-status but never fails the health check.
app.MapGet("/healthz", async (AppDbContext db) =>
{
    bool dbOk;
    try { dbOk = await db.Database.CanConnectAsync(); } catch { dbOk = false; }
    return Results.Ok(new { status = "healthy", database = dbOk ? "connected" : "connecting" });
});

// SPA fallback: serve the Angular index.html for non-API routes.
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
