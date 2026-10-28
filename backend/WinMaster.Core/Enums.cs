namespace WinMaster.Core;

public enum UserRole { User = 0, Reseller = 1, Admin = 2, SuperAdmin = 3 }

public enum EngineType { Baileys = 0, CloudApi = 1 }

public enum LicenseType { User = 0, Reseller = 1, Admin = 2 }

public enum LicenseStatus { Active = 0, Revoked = 1, Expired = 2 }

public enum WaAccountStatus { Disconnected = 0, Connecting = 1, QrReady = 2, Connected = 3, Banned = 4, Error = 5 }

public enum WarmupState { None = 0, Warming = 1, Ready = 2 }

public enum ProxyType { HTTP = 0, HTTPS = 1, SOCKS5 = 2 }

public enum ContactSource { Import = 0, GroupScrape = 1, Manual = 2 }

public enum MediaType { None = 0, Image = 1, Document = 2, Video = 3, Audio = 4 }

public enum CampaignStatus { Draft = 0, Scheduled = 1, Running = 2, Paused = 3, Completed = 4, Stopped = 5, Failed = 6 }

public enum RotationMode { Sequential = 0, RoundRobin = 1 }

public enum RecipientStatus { Pending = 0, Sending = 1, Sent = 2, Delivered = 3, Read = 4, Failed = 5 }

public enum MatchType { Exact = 0, Wildcard = 1, Regex = 2 }

public enum AiProvider { OpenAI = 0, Anthropic = 1, Groq = 2, OpenRouter = 3, Gemini = 4, Local = 5 }

public enum MessageDirection { In = 0, Out = 1 }

public enum MessageStatus { Queued = 0, Sent = 1, Delivered = 2, Read = 3, Failed = 4 }
