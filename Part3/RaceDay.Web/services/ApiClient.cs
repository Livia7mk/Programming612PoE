using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RaceDay.Web.Models;

namespace RaceDay.Web.Services;

// Names of the values kept in the user's server-side session.
public static class SessionKeys
{
    public const string ApiCookie = "ApiCookie";
    public const string UserId = "UserId";
    public const string Role = "Role";
    public const string FullName = "FullName";
}

// Used when an API call returns no body (for example 204 No Content).
public class NoData { }

public class ApiResult<T>
{
    public bool Success { get; set; }
    public int StatusCode { get; set; }
    public T? Data { get; set; }
    public string? Error { get; set; }
    public bool IsUnauthorized => StatusCode == 401;
}

// The only way the MVC app talks to the outside world: every call goes to the Part 2 API.
public class ApiClient
{
    // The name of the cookie the Part 2 API uses for its login session.
    public const string ApiCookieName = "RaceDay.Session";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly IHttpContextAccessor _accessor;

    public ApiClient(HttpClient http, IHttpContextAccessor accessor)
    {
        _http = http;
        _accessor = accessor;
    }

    public async Task<ApiResult<T>> GetAsync<T>(string url) =>
        await ToResultAsync<T>(await SendAsync(HttpMethod.Get, url, null));

    public async Task<ApiResult<T>> PostAsync<T>(string url, object? body = null) =>
        await ToResultAsync<T>(await SendAsync(HttpMethod.Post, url, ToJson(body)));

    public async Task<ApiResult<T>> PutAsync<T>(string url, object? body) =>
        await ToResultAsync<T>(await SendAsync(HttpMethod.Put, url, ToJson(body)));

    public async Task<ApiResult<NoData>> DeleteAsync(string url) =>
        await ToResultAsync<NoData>(await SendAsync(HttpMethod.Delete, url, null));

    // Logs in through the API and also returns the API's session cookie, so we can send it on later calls.
    public async Task<(ApiResult<UserDto> Result, string? ApiCookie)> LoginAsync(string email, string password)
    {
        var response = await SendAsync(HttpMethod.Post, "api/auth/login", ToJson(new { email, password }));

        string? apiCookie = null;
        if (response is not null && response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            var prefix = ApiCookieName + "=";
            apiCookie = values
                .Select(v => v.Split(';')[0])
                .FirstOrDefault(v => v.StartsWith(prefix))?[prefix.Length..];
        }

        return (await ToResultAsync<UserDto>(response), apiCookie);
    }

    private static HttpContent? ToJson(object? body) =>
        body is null ? null : JsonContent.Create(body, body.GetType(), null, JsonOptions);

    // Sends the request, attaching the logged-in user's API cookie from their session.
    // Returns null when the API cannot be reached at all.
    private async Task<HttpResponseMessage?> SendAsync(HttpMethod method, string url, HttpContent? content)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };

        var apiCookie = _accessor.HttpContext?.Session.GetString(SessionKeys.ApiCookie);
        if (!string.IsNullOrEmpty(apiCookie))
            request.Headers.Add("Cookie", $"{ApiCookieName}={apiCookie}");

        try
        {
            return await _http.SendAsync(request);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static async Task<ApiResult<T>> ToResultAsync<T>(HttpResponseMessage? response)
    {
        if (response is null)
        {
            return new ApiResult<T>
            {
                StatusCode = 503,
                Error = "The RaceDay service is not reachable right now. Please try again shortly."
            };
        }

        using (response)
        {
            var result = new ApiResult<T>
            {
                StatusCode = (int)response.StatusCode,
                Success = response.IsSuccessStatusCode
            };

            if (!response.IsSuccessStatusCode)
                result.Error = await ReadErrorAsync(response);
            else if (response.StatusCode != HttpStatusCode.NoContent && typeof(T) != typeof(NoData))
                result.Data = await response.Content.ReadFromJsonAsync<T>(JsonOptions);

            return result;
        }
    }

    // Turns an API error into a short, friendly sentence. Users never see raw JSON or stack traces.
    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

            if (json.ValueKind == JsonValueKind.Object)
            {
                if (json.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                    return message.GetString()!;

                if (json.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                {
                    var messages = errors.EnumerateObject()
                        .SelectMany(p => p.Value.EnumerateArray())
                        .Select(v => v.GetString())
                        .Where(s => !string.IsNullOrWhiteSpace(s));

                    var joined = string.Join(" ", messages);
                    if (joined.Length > 0) return joined;
                }
            }
        }
        catch (Exception)
        {
            // The response had no readable body, so fall back to a standard message.
        }

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Please log in to continue.",
            HttpStatusCode.Forbidden => "You do not have permission to do that.",
            HttpStatusCode.NotFound => "That item could not be found.",
            _ => "Something went wrong. Please try again."
        };
    }
}