using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using WinMaster.Infrastructure.Data;
using WinMaster.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWinMasterInfrastructure(builder.Configuration);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

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
    p.WithOrigins("http://localhost:4200", "http://localhost:8080", "http://localhost:5173")
     .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Apply migrations / create schema + seed.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try { if (db.Database.IsRelational() && db.Database.GetPendingMigrations().Any()) db.Database.Migrate(); else db.Database.EnsureCreated(); }
    catch { db.Database.EnsureCreated(); }
    DbSeeder.Seed(db);
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

app.MapControllers();
app.MapGet("/healthz", async (AppDbContext db) =>
{
    try { await db.Database.CanConnectAsync(); return Results.Ok(new { status = "healthy" }); }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// SPA fallback: serve the Angular index.html for non-API routes.
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
