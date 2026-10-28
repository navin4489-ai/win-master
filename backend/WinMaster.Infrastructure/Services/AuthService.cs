using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using WinMaster.Core;
using WinMaster.Core.Dtos;
using WinMaster.Core.Entities;
using WinMaster.Infrastructure.Data;

namespace WinMaster.Infrastructure.Services;

public class JwtOptions
{
    public string Issuer { get; set; } = "WinMaster";
    public string Audience { get; set; } = "WinMasterClients";
    public string Secret { get; set; } = "";
    public int AccessMinutes { get; set; } = 15;
    public int RefreshDays { get; set; } = 30;
}

public class TokenService
{
    private readonly JwtOptions _opt;
    public TokenService(IConfiguration cfg)
    {
        _opt = new JwtOptions
        {
            Issuer = cfg["Jwt:Issuer"] ?? "WinMaster",
            Audience = cfg["Jwt:Audience"] ?? "WinMasterClients",
            Secret = cfg["Jwt:Secret"] ?? "winmaster-dev-super-secret-key-change-me-please-123456",
            AccessMinutes = int.TryParse(cfg["Jwt:AccessMinutes"], out var a) ? a : 15,
            RefreshDays = int.TryParse(cfg["Jwt:RefreshDays"], out var r) ? r : 30,
        };
    }

    public JwtOptions Options => _opt;

    public (string token, int expiresIn) CreateAccessToken(User u)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, u.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, u.Email),
            new Claim(ClaimTypes.Name, u.DisplayName),
            new Claim(ClaimTypes.Role, u.Role.ToString()),
            new Claim("uid", u.Id.ToString()),
        };
        var expires = DateTime.UtcNow.AddMinutes(_opt.AccessMinutes);
        var token = new JwtSecurityToken(_opt.Issuer, _opt.Audience, claims, expires: expires, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(token), _opt.AccessMinutes * 60);
    }

    public string CreateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static string HashToken(string token)
    {
        using var sha = SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(token)));
    }
}

public class AuthService
{
    private readonly AppDbContext _db;
    private readonly TokenService _tokens;
    public AuthService(AppDbContext db, TokenService tokens) { _db = db; _tokens = tokens; }

    public async Task<AuthResponse> LoginAsync(LoginRequest req)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email.Trim().ToLower())
            ?? await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email.Trim());
        if (user is null || !user.IsActive || !PasswordHasher.Verify(req.Password, user.PasswordHash))
            throw new ForbiddenException("Invalid credentials.");
        return await IssueAsync(user);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest req)
    {
        var hash = TokenService.HashToken(req.RefreshToken);
        var rt = await _db.RefreshTokens.Include(x => x.User).FirstOrDefaultAsync(x => x.TokenHash == hash);
        if (rt is null || !rt.IsActive || rt.User is null) throw new ForbiddenException("Invalid refresh token.");
        rt.RevokedAt = DateTime.UtcNow; // rotate
        await _db.SaveChangesAsync();
        return await IssueAsync(rt.User);
    }

    public async Task LogoutAsync(RefreshRequest req)
    {
        var hash = TokenService.HashToken(req.RefreshToken);
        var rt = await _db.RefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == hash);
        if (rt is not null) { rt.RevokedAt = DateTime.UtcNow; await _db.SaveChangesAsync(); }
    }

    private async Task<AuthResponse> IssueAsync(User user)
    {
        var (access, expiresIn) = _tokens.CreateAccessToken(user);
        var refresh = _tokens.CreateRefreshToken();
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = TokenService.HashToken(refresh),
            ExpiresAt = DateTime.UtcNow.AddDays(_tokens.Options.RefreshDays)
        });
        await _db.SaveChangesAsync();

        var license = await _db.Licenses.Include(l => l.Activations).Include(l => l.User)
            .FirstOrDefaultAsync(l => l.UserId == user.Id);
        return new AuthResponse(access, refresh, expiresIn, user.ToDto(license?.ToDto()));
    }

    public async Task<UserDto> MeAsync(int userId)
    {
        var user = await _db.Users.FindAsync(userId) ?? throw new NotFoundException("User not found.");
        var license = await _db.Licenses.Include(l => l.Activations).Include(l => l.User)
            .FirstOrDefaultAsync(l => l.UserId == user.Id);
        return user.ToDto(license?.ToDto());
    }
}
