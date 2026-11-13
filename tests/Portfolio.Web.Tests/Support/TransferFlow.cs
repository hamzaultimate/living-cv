using System.Net;
using System.Text;
using System.Text.Json;

namespace Portfolio.Web.Tests.Support;

/// <summary>Drives the export and import over HTTP the way a browser would.</summary>
public static class TransferFlow
{
    public const string Page = "/admin/transfer";
    public const string Export = "/admin/transfer/export";

    /// <summary>The export file as the owner downloads it.</summary>
    public static async Task<string> ExportTextAsync(HttpClient client)
    {
        var response = await client.GetAsync(Export);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    public static async Task<JsonDocument> ExportJsonAsync(HttpClient client) => JsonDocument.Parse(await ExportTextAsync(client));

    public static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);
}
