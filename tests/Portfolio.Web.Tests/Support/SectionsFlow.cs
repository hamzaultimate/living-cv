using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data.Entities;

namespace Portfolio.Web.Tests.Support;

/// <summary>Drives the sections page the way a browser would: read the rows the page draws, change some, post them back with the stamp the page carries.</summary>
public static class SectionsFlow
{
    public const string Path = "/admin/sections";

    public sealed record Row(string Key, string Label, bool Enabled);

    private static readonly Regex KeyPattern = new(@"name=""Form\.Rows\[(\d+)\]\.Key"" value=""([^""]*)""", RegexOptions.Compiled);

    /// <summary>The rows as the page draws them, in order: key, the name typed (empty for the template's own) and whether the box is ticked (a section that cannot be switched off counts as on).</summary>
    public static async Task<List<Row>> RowsAsync(HttpClient client)
    {
        var html = await client.GetStringAsync(Path);
        var rows = new List<Row>();
        foreach (Match m in KeyPattern.Matches(html))
        {
            var i = m.Groups[1].Value;
            var label = Regex.Match(html, $@"<input[^>]*id=""Form_Rows_{i}__Label""[^>]*>").Value;
            var typed = Regex.Match(label, @"\svalue=""([^""]*)""");
            var box = Regex.Match(html, $@"<input[^>]*id=""Form_Rows_{i}__Enabled""[^>]*>").Value;
            rows.Add(new Row(m.Groups[2].Value, typed.Success ? WebUtility.HtmlDecode(typed.Groups[1].Value) : "", box.Length == 0 || box.Contains("checked")));
        }
        return rows;
    }

    public static Dictionary<string, string> Fields(IEnumerable<Row> rows)
    {
        var fields = new Dictionary<string, string>();
        var i = 0;
        foreach (var r in rows)
        {
            fields[$"Form.Rows[{i}].Key"] = r.Key;
            fields[$"Form.Rows[{i}].Label"] = r.Label;
            fields[$"Form.Rows[{i}].Enabled"] = r.Enabled ? "true" : "false";
            i++;
        }
        return fields;
    }

    /// <summary>Reads the page, applies <paramref name="change"/> to the rows and posts them with the stamp of that read.</summary>
    public static async Task<HttpResponseMessage> SaveAsync(HttpClient client, Func<List<Row>, List<Row>> change, string? stamp = null) =>
        await ProfileFlow.PostAsync(client, Path, Fields(change(await RowsAsync(client))), stamp);

    public static Func<List<Row>, List<Row>> Set(string key, bool? enabled = null, string? label = null) => rows =>
        rows.Select(r => r.Key == key ? r with { Enabled = enabled ?? r.Enabled, Label = label ?? r.Label } : r).ToList();

    public static Func<List<Row>, List<Row>> MoveTo(string key, int position) => rows =>
    {
        var list = rows.ToList();
        var row = list.First(r => r.Key == key);
        list.Remove(row);
        list.Insert(position, row);
        return list;
    };

    public static async Task<List<SectionSetting>> StoredAsync(AdminApp app) => (await ProfileFlow.ProfileAsync(app)).Sections;

    public static string NavOf(string html)
    {
        var nav = Regex.Match(html, @"<nav class=""hidden items-center gap-6 md:flex"">(.*?)</nav>", RegexOptions.Singleline).Groups[1].Value;
        return string.Join(",", Regex.Matches(nav, @"href=""([^""]*)""[^>]*>([^<]*)<").Select(m => m.Groups[1].Value + "=" + m.Groups[2].Value.Trim()));
    }

    public static List<string> SitemapPaths(string xml) =>
        Regex.Matches(xml, @"<loc>https?://[^/]+(/[^<]*)</loc>").Select(m => m.Groups[1].Value).ToList();
}
