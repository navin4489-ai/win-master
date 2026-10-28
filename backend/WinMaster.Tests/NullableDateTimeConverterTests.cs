using System.Text.Json;
using WinMaster.API.Json;
using Xunit;

namespace WinMaster.Tests;

/// <summary>Guards the campaign-launch fix: empty-string dates must bind as null, not throw.</summary>
public class NullableDateTimeConverterTests
{
    private sealed record Holder(DateTime? ScheduledAt);

    private static readonly JsonSerializerOptions Opts = Build();
    private static JsonSerializerOptions Build()
    {
        var o = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        o.Converters.Add(new NullableDateTimeConverter());
        return o;
    }

    [Fact]
    public void EmptyString_BindsAsNull()
    {
        var h = JsonSerializer.Deserialize<Holder>("{\"scheduledAt\":\"\"}", Opts);
        Assert.NotNull(h);
        Assert.Null(h!.ScheduledAt);
    }

    [Fact]
    public void Whitespace_BindsAsNull()
    {
        var h = JsonSerializer.Deserialize<Holder>("{\"scheduledAt\":\"   \"}", Opts);
        Assert.Null(h!.ScheduledAt);
    }

    [Fact]
    public void Null_BindsAsNull()
    {
        var h = JsonSerializer.Deserialize<Holder>("{\"scheduledAt\":null}", Opts);
        Assert.Null(h!.ScheduledAt);
    }

    [Fact]
    public void ValidDate_BindsCorrectly()
    {
        var h = JsonSerializer.Deserialize<Holder>("{\"scheduledAt\":\"2026-11-01T10:30:00Z\"}", Opts);
        Assert.NotNull(h!.ScheduledAt);
        Assert.Equal(2026, h.ScheduledAt!.Value.Year);
        Assert.Equal(11, h.ScheduledAt.Value.Month);
    }

    [Fact]
    public void GarbageString_Throws()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Holder>("{\"scheduledAt\":\"not-a-date\"}", Opts));
    }
}
