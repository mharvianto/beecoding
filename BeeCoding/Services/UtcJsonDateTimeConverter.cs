using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeeCoding.Services;

/// <summary>Every time the app stores is UTC, but SQLite hands it back with kind "unspecified" and the default JSON writer then leaves off
/// the trailing Z. A browser reads "2026-11-23T06:28:37" as LOCAL time, so a page that did <c>new Date(x).toLocaleString()</c> showed UTC
/// as if it were the viewer's clock (WIB looked 7 hours behind). Writing unspecified as UTC makes every such place right at once; Postgres
/// already returns UTC (<c>UtcDateTimeConverter</c> in AppDbContext) so nothing changes there. Reading is untouched.</summary>
public sealed class UtcJsonDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDateTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value);
}
