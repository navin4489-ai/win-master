namespace WinMaster.Core.Dtos;

// ---------- Common ----------
public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public record PageQuery(int Page = 1, int PageSize = 50);

// ---------- Auth ----------
public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
public record LogoutRequest(string RefreshToken);
public record AuthResponse(string AccessToken, string RefreshToken, int ExpiresIn, UserDto User);
public record UserDto(int Id, string Email, string DisplayName, string Role, int? ParentUserId,
    bool IsActive, string? ResellerCode, LicenseDto? License);

// ---------- Users ----------
public record CreateUserRequest(string Email, string Password, string DisplayName, string Role, int? ParentUserId);
public record UpdateUserRequest(string DisplayName, string? Role, bool? IsActive);
public record UserTreeNode(int Id, string Email, string DisplayName, string Role, List<UserTreeNode> Children);

// ---------- Licenses ----------
public record LicenseDto(int Id, string Key, string LicenseType, int IssuedById, int? UserId, string? UserEmail,
    string[] Features, DateTime StartsAt, DateTime? ExpiresAt, int MaxActivations, string Status,
    DateTime CreatedAt, int ActivationCount);
public record CreateLicenseRequest(string LicenseType, string[] Features, DateTime StartsAt,
    DateTime? ExpiresAt, int MaxActivations, int? UserId);
public record ActivationDto(int Id, int LicenseId, string Hwid, string? DeviceName, DateTime ActivatedAt,
    DateTime? LastVerifiedAt, DateTime? RevokedAt);
public record ActivateRequest(string Key, string Hwid, string DeviceName, string? ProductVersion);
public record ActivateResponse(LicenseDto License, string Token, int VerifyIntervalHours, int OfflineGraceDays);
public record VerifyRequest(string Key, string Hwid, string Token);
public record VerifyResponse(bool Valid, string? Reason, DateTime? ExpiresAt);
public record DeactivateRequest(string Key, string Hwid);

// ---------- WhatsApp accounts ----------
public record WaAccountDto(int Id, string EngineType, string? PhoneNumber, string DisplayName, string Status,
    int? ProxyId, string? ProxyLabel, string WarmupState, int RotationWeight, int DailyLimit, int SentToday,
    DateTime? LastSeenAt, string? SessionId);
public record CreateWaAccountRequest(string EngineType, string? PhoneNumber, string DisplayName,
    int? ProxyId, CloudApiCredentials? Credentials);
public record UpdateWaAccountRequest(string DisplayName, int? ProxyId, int? RotationWeight, int? DailyLimit);
public record CloudApiCredentials(string PhoneNumberId, string AccessToken, string WabaId);
public record QrResponse(string? QrData, string? QrPngBase64, DateTime? ExpiresAt);
public record AccountsStatusDto(int Connected, int Connecting, int Total);

// ---------- Proxies ----------
public record ProxyDto(int Id, string Type, string Host, int Port, string? Username, bool IsHealthy);
public record CreateProxyRequest(string Type, string Host, int Port, string? Username, string? Password);
public record ProxyTestResult(bool Ok, int? LatencyMs, string? Error);

// ---------- Contacts ----------
public record ContactGroupDto(int Id, string Name, string Source, int TotalContacts, int ValidContacts,
    int InvalidContacts, string[] Tags, DateTime CreatedAt, DateTime? UpdatedAt);
public record CreateGroupRequest(string Name, string[]? Tags);
public record ContactDto(int Id, int GroupId, string Phone, string? Name, bool? WaRegistered,
    Dictionary<string, string> CustomFields, string[] Tags, DateTime? LastMessagedAt);
public record CreateContactRequest(string Phone, string? Name, Dictionary<string, string>? CustomFields, string[]? Tags);
public record ImportResult(int Imported, int Invalid, int Duplicates, List<string> Errors);
public record ValidateRequest(List<string> Phones);
public record ValidateResult(string Phone, bool Valid, bool? WaRegistered, string? Reason);
public record TagDto(int Id, string Name, string Color);
public record CreateTagRequest(string Name, string Color);
public record ScrapeRequest(string Source, string? GroupId, string? AccountUrl, int AccountId);
public record ScrapeJobDto(string JobId, string Status, int Extracted, string? Message);

// ---------- Templates ----------
public record TemplateButton(string Type, string Id, string Title, string? Payload);
public record TemplateListRow(string Id, string Title, string? Description);
public record TemplateListSection(string Title, List<TemplateListRow> Rows);
public record TemplateListMenu(string Title, List<TemplateListSection> Sections);
public record TemplateDto(int Id, string Name, string Body, string MediaType, string? MediaUrl,
    List<TemplateButton>? Buttons, TemplateListMenu? ListMenu, string Category, DateTime CreatedAt, DateTime? UpdatedAt);
public record CreateTemplateRequest(string Name, string Body, string MediaType, string? MediaUrl,
    List<TemplateButton>? Buttons, TemplateListMenu? ListMenu, string? Category);

// ---------- Campaigns ----------
public record PacingDto(int MinDelaySec, int MaxDelaySec, int BatchSize, int CooldownMinutes);
public record CampaignDto(int Id, string Name, int? TemplateId, string? TemplateName, int GroupId, string? GroupName,
    string EngineType, int? AccountId, string RotationMode, string Status, DateTime? ScheduledAt,
    string? WindowStart, string? WindowEnd, bool SpintaxEnabled, bool PersonalizeMessage, PacingDto Pacing,
    int TotalRecipients, int ProcessedCount, int FailedCount, int ProgressPercent,
    DateTime? StartedAt, DateTime? CompletedAt, DateTime CreatedAt);
public record CreateCampaignRequest(string Name, int? TemplateId, string? Body, int GroupId, string EngineType,
    int? AccountId, string RotationMode, DateTime? ScheduledAt, string? WindowStart, string? WindowEnd,
    bool SpintaxEnabled, bool PersonalizeMessage, PacingDto Pacing);
public record ScheduleRequest(DateTime ScheduledAt, string? WindowStart, string? WindowEnd);
public record CampaignProgress(int CampaignId, string Status, int Total, int Processed, int Sent, int Delivered,
    int Read, int Failed, int? CurrentRecipientIndex, int? EstimatedSecondsRemaining, DateTime? LastCheckpointAt);
public record CampaignRecipientDto(int Id, string PhoneMasked, string? Name, string Status, string? VariantText,
    string? Error, DateTime? SentAt, DateTime? DeliveredAt, DateTime? ReadAt);

// ---------- Anti-ban ----------
public record AntiBanDto(int MinDelaySec, int MaxDelaySec, int BatchSize, int CooldownMinutes, int DailyCap);

// ---------- Auto-reply ----------
public record AutoReplyRuleDto(int Id, int AccountId, string MatchType, string Pattern, int Priority,
    string? ResponseText, int? ResponseTemplateId, bool UseAi, bool IsActive);
public record CreateAutoReplyRuleRequest(int AccountId, string MatchType, string Pattern, int Priority,
    string? ResponseText, int? ResponseTemplateId, bool UseAi, bool IsActive);

// ---------- AI ----------
public record AiSettingsDto(string? Provider, string Model, string SystemPrompt, bool IsActive,
    int DailyTokenBudget, string? MaskedApiKey);
public record UpdateAiSettingsRequest(string? Provider, string? ApiKey, string Model, string SystemPrompt,
    bool IsActive, int DailyTokenBudget);
public record AiTestRequest(string Message);
public record AiTestResponse(string Reply, int TokensUsed, int LatencyMs);
public record AiGenerateRequest(string Prompt, string? Tone, string? Goal, bool IncludeSpintax, bool IncludeVariables, string? Refine = null, string? Previous = null);
public record AiGenerateResponse(string Message, int TokensUsed, string Model);
public record AiProviderInfo(string Id, string Name, bool Free, bool NeedsKey, string[] Models, string? SignupUrl);
public record AiProviderCatalog(List<AiProviderInfo> Providers);

// ---------- Omnichannel / leads ----------
public record ChannelAccountDto(int Id, string Channel, string DisplayName, string? ExternalAccountId,
    string? EmailAddress, string? SmtpHost, int? SmtpPort, bool IsActive, DateTime? LastSyncAt);
public record CreateChannelAccountRequest(string Channel, string DisplayName, string? ExternalAccountId,
    string? EmailAddress, string? SmtpHost, int? SmtpPort, string? ImapHost, int? ImapPort,
    string? AccessToken, string? Username, string? Password, bool IsActive);
public record SendChannelMessageRequest(int? AccountId, string Channel, string Recipient, string Body,
    string? Subject, string? ThreadId);
public record ChannelWebhookRequest(string Channel, string ExternalAccountId, string Sender,
    string Body, string? Subject, string? ExternalMessageId, DateTime? Timestamp);
public record LeadDto(int Id, string DisplayName, string? Email, string? Phone, string Channel,
    string Status, decimal Score, bool IsConverted, DateTime? ConvertedAt, decimal? ConversionValue,
    string AiSummary, DateTime CreatedAt);
public record UpdateLeadRequest(string Status, bool? IsConverted, decimal? ConversionValue, string[]? Tags);
public record LeadAnalytics(int TotalLeads, int NewLeads, int QualifiedLeads, int ConvertedLeads,
    int LostLeads, decimal ConversionRate, decimal TotalConversionValue,
    List<ChannelAnalytics> ByChannel, List<LeadDto> RecentConversions);
public record ChannelAnalytics(string Channel, int Leads, int Converted, decimal ConversionRate, decimal Value);
public record AiConversationAnalysis(string Summary, string[] IntentSignals, string RecommendedNextAction,
    decimal LeadScore, string SuggestedStatus);

// ---------- AI lead discovery ----------
public record LeadSearchRequest(string Pincode, int LeadCount, string BusinessType, string? Keyword, string? CountryCode);
public record LeadSearchResultDto(string ExternalId, string BusinessName, string? OwnerName, string Phone,
    string? Address, string Source, string? SourceUrl, decimal Confidence, bool Selected);
public record LeadSearchResponse(string Provider, string Query, List<LeadSearchResultDto> Results, string? Message);
public record LeadImportRequest(List<LeadSearchResultDto> Leads);
public record LeadImportResult(int Imported, int Duplicates, int Invalid, List<string> Errors);

// ---------- Inbox ----------
public record InboxThreadDto(int Id, int AccountId, string AccountLabel, string ContactPhone, string ContactPhoneMasked,
    string? ContactName, DateTime LastMessageAt, int UnreadCount, bool IsBotEnabled, string? Preview);
public record InboxMessageDto(int Id, int ThreadId, string Direction, string Body, string? MediaUrl, string? MediaType,
    string? Status, string? WaMessageId, DateTime SentAt, DateTime CreatedAt);
public record SendMessageRequest(string Body, string? MediaUrl);
public record SendToRequest(string Phone, string Body, int? AccountId, string? MediaUrl);
public record BotToggleRequest(bool IsBotEnabled);

// ---------- Reports / dashboard ----------
public record DeliverySeriesPoint(DateTime Date, int Sent, int Delivered, int Read, int Failed);
public record DeliveryReport(int Sent, int Delivered, int Read, int Failed, double DeliveryRate, double ReadRate,
    List<DeliverySeriesPoint> Series);
public record KpiReport(double DeliveryRate, double? CrashFreeSessions, double SuspensionRate, int TotalCampaigns,
    int TotalMessages, int ActiveAccounts, int ActiveLicenses);
public record DashboardStats(
    CampaignStatsDto Campaigns, MessageStatsDto Messages, AccountStatsDto Accounts,
    ContactStatsDto Contacts, LicenseStatsDto Licenses, InboxStatsDto Inbox);
public record CampaignStatsDto(int Total, int Running, int Scheduled);
public record MessageStatsDto(int SentToday, int DeliveredToday, int FailedToday);
public record AccountStatsDto(int Connected, int Total);
public record ContactStatsDto(int Total, int Groups);
public record LicenseStatsDto(int Active, int ExpiringSoon);
public record InboxStatsDto(int Unread, int Threads);

// ---------- Integrations ----------
public record BridgeEvent(string Type, int AccountId, System.Text.Json.JsonElement Payload);
public record PushTokenRequest(string Platform, string Token, string? DeviceId);
public record AuditLogDto(int Id, int? UserId, string Action, string? Entity, string? EntityId, string? Ip,
    string? MaskedDetail, DateTime CreatedAt);
