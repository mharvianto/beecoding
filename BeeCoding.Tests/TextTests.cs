using BeeCoding.Models;
using BeeCoding.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace BeeCoding.Tests;

public class TextTests
{
    [Fact]
    public void Language_comes_from_the_saved_preference_then_the_header_then_English()
    {
        Assert.Equal("id", Text.LangOf("""{"beecoding.docLang":"id"}"""));
        Assert.Equal("en", Text.LangOf("""{"beecoding.docLang":"en"}"""));
        Assert.Equal("en", Text.LangOf(null));
        Assert.Equal("en", Text.LangOf("not json"));

        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Lang"] = "id";
        Assert.Equal("id", Text.LangOf(null, ctx.Request));
        Assert.Equal("en", Text.LangOf("""{"beecoding.docLang":"en"}""", ctx.Request));   // the saved choice wins over the header
    }

    [Fact]
    public void A_text_without_an_Indonesian_entry_stays_English_and_arguments_are_filled()
    {
        Assert.Equal("Hello Ana", Text.T("id", "Hello {0}", "Ana"));
        Assert.Equal("Test notification: push works on this device.", Text.T("en", "Test notification: push works on this device."));
        Assert.Equal("Notifikasi uji: push berfungsi di perangkat ini.", Text.T("id", "Test notification: push works on this device."));
    }

    [Fact]
    public void Notification_sentence_follows_the_language_and_every_kind_has_an_Indonesian_text()
    {
        var kinds = new[] { "reaction", "comment", "report", "report-update", "proposal", "proposal-update", "practice-review", "review-submitted", "org-invite", "group-opens", "group-closing", "problem-new", "lti-sync-failed", "security" };
        foreach (var kind in kinds)
        {
            var d = new NotificationDto(1, kind, "b", "Board", 1, "Soal", 1, "Ana", false, "✅", "x", 1, DateTime.UtcNow, false);
            var en = NotificationService.EmailText(d, "en");
            var id = NotificationService.EmailText(d, "id");
            Assert.NotEqual(en, id);
            Assert.DoesNotContain("{", id);
        }
    }
}
