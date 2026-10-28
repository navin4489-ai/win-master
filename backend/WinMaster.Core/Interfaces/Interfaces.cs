using WinMaster.Core.Dtos;

namespace WinMaster.Core.Interfaces;

public interface ICurrentUser
{
    int? UserId { get; }
    string? Email { get; }
    WinMaster.Core.UserRole? Role { get; }
}

public interface IEncryptionService
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
    string Mask(string? value);
}

public interface ILicenseSigner
{
    string GenerateKey(WinMaster.Core.LicenseType type);
    string Sign(string key, string hwid, DateTime? expiresAt);
    bool Verify(string key, string hwid, DateTime? expiresAt, string token);
}

/// <summary>Outbound message result from a WhatsApp channel.</summary>
public record SendResult(bool Success, string? MessageId, string? Error);

public record OutboundMessage(string ToPhone, string Body, string? MediaUrl, WinMaster.Core.MediaType MediaType,
    string? ButtonsJson, string? ListMenuJson);

public interface IWhatsAppChannel
{
    WinMaster.Core.EngineType Engine { get; }
    Task<SendResult> SendAsync(int accountId, OutboundMessage message, CancellationToken ct = default);
}

public interface IWhatsAppChannelFactory
{
    IWhatsAppChannel For(WinMaster.Core.EngineType engine);
}

public interface IPushService
{
    Task SendAsync(int userId, string title, string body, CancellationToken ct = default);
}
