using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UniversityTodos.Models;

namespace UniversityTodos.Services;

public interface IElectricityApi
{
    Task<List<ElectricityRoom>?> GetBoundRoomsAsync(string account, CancellationToken ct = default);
}

public sealed class ElectricityApiClient : IElectricityApi
{
    private const string Url = "https://xqh5.17wanxiao.com/smartWaterAndElectricityService/SWAEServlet";

    public string? LastError { get; private set; }

    private static readonly Dictionary<string, string> InitialCookies = new()
    {
        { "acw_tc", "0a45645e17625760507803047ec74cd7857cf8506787198497f848f5ac54ba" },
        { "SERVERID", "7abd666da76fadf6bd7f0a8acd3e2ff1|1725968099|1725968080" }
    };

    public async Task<List<ElectricityRoom>?> GetBoundRoomsAsync(
        string account, CancellationToken ct = default)
    {
        using var httpClient = CreateHttpClient();
        var paramJson = JsonSerializer.Serialize(new ElectricityBindRoomRequest
        {
            Cmd = "getbindroom",
            Account = account,
            Timestamp = DateTime.Now.ToString("yyyyMMddHHmmssfff")
        }, AppJsonContext.Default.ElectricityBindRoomRequest);
        var formData = new Dictionary<string, string>
        {
            { "param", paramJson },
            { "customercode", "1399" },
            { "method", "getbindroom" },
            { "command", "JBSWaterElecService" }
        };

        LastError = null;
        using var content = new FormUrlEncodedContent(formData);
        using var response = await httpClient.PostAsync(Url, content, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            LastError = $"HTTP {(int)response.StatusCode} {response.StatusCode}：{Preview(body)}";
            return null;
        }
        var rooms = ElectricityJsonParser.ParseRoomList(body);
        if (rooms is null) LastError = $"接口响应无法解析：{Preview(body)}";
        return rooms;
    }

    private static string Preview(string body)
    {
        var text = body.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= 240 ? text : text[..240] + "…";
    }

    private static HttpClient CreateHttpClient()
    {
        var cookieContainer = new CookieContainer();
        var uri = new Uri(Url);
        foreach (var pair in InitialCookies)
            cookieContainer.Add(uri, new Cookie(pair.Key, pair.Value));

        var client = new HttpClient(new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        client.DefaultRequestHeaders.Referrer = new Uri("https://xqh5.17wanxiao.com/userwaterelecmini/index.html");
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Linux; Android 14; REP-AN00 Build/HONORREP-AN00; wv) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/127.0.6533.103 " +
            "Mobile Safari/537.36 Wanxiao/5.8.6");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Requested-With", "com.newcapec.mobile.ncp");
        return client;
    }
}

public static class ElectricityJsonParser
{
    public static List<ElectricityRoom>? ParseRoomList(string jsonResponse)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonResponse);
            var root = doc.RootElement;
            if (!root.TryGetProperty("code_", out var codeElement)
                || !TryGetInt(codeElement, out var code) || code != 0)
                return null;
            if (!root.TryGetProperty("body", out var bodyElement)
                || bodyElement.ValueKind != JsonValueKind.String)
                return null;

            using var innerDoc = JsonDocument.Parse(bodyElement.GetString()!);
            var innerRoot = innerDoc.RootElement;
            if (innerRoot.TryGetProperty("roomlist", out var roomList)
                && roomList.ValueKind == JsonValueKind.Array && roomList.GetArrayLength() > 0)
            {
                var rooms = new List<ElectricityRoom>();
                foreach (var room in roomList.EnumerateArray())
                {
                    var info = ParseRoom(room, requireDetailList: true);
                    if (info is not null) rooms.Add(info);
                }
                return rooms.Count > 0 ? rooms : null;
            }

            if (innerRoot.TryGetProperty("roomfullname", out _)
                && innerRoot.TryGetProperty("roomverify", out _))
            {
                var info = ParseRoom(innerRoot, requireDetailList: false);
                if (info is not null) return [info];
            }
            return null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static ElectricityRoom? ParseRoom(JsonElement room, bool requireDetailList)
    {
        var name = GetString(room, "roomfullname", "未知房间");
        var verify = GetString(room, "roomverify", string.Empty);
        if (string.IsNullOrWhiteSpace(verify)) return null;

        var hasDetails = room.TryGetProperty("detaillist", out var details)
            && details.ValueKind == JsonValueKind.Array && details.GetArrayLength() > 0;
        if (requireDetailList && !hasDetails) return null;
        var source = hasDetails ? details[0] : room;
        if (!TryGetDouble(source, "odd", out var odd)
            || !TryGetDouble(source, "use", out var use)
            || !TryGetInt(source, "status", out var status))
            return null;

        return new ElectricityRoom
        {
            RoomName = name,
            RoomVerify = verify,
            Odd = odd,
            Use = use,
            Status = status
        };
    }

    private static string GetString(JsonElement element, string property, string fallback)
    {
        return element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private static bool TryGetDouble(JsonElement element, string property, out double value)
    {
        value = 0;
        if (!element.TryGetProperty(property, out var item)) return false;
        var parsed = item.ValueKind switch
        {
            JsonValueKind.String => double.TryParse(
                item.GetString()?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value),
            JsonValueKind.Number => item.TryGetDouble(out value),
            _ => false
        };
        return parsed && double.IsFinite(value);
    }

    private static bool TryGetInt(JsonElement element, string property, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(property, out var item)) return false;
        return item.ValueKind switch
        {
            JsonValueKind.String => int.TryParse(
                item.GetString()?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value),
            JsonValueKind.Number => item.TryGetInt32(out value),
            _ => false
        };
    }

    private static bool TryGetInt(JsonElement element, out int value)
    {
        value = 0;
        return element.ValueKind switch
        {
            JsonValueKind.String => int.TryParse(
                element.GetString()?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value),
            JsonValueKind.Number => element.TryGetInt32(out value),
            _ => false
        };
    }
}
