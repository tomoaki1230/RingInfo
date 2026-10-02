using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RingInfo.Core.Models;

namespace RingInfo.Core.Api;

/// <summary>
/// Oura API v2 のクライアント。
/// ページング（next_token）と、401 時のトークン更新・再試行を内部で処理する。
/// </summary>
public sealed class OuraApiClient : IOuraDataSource
{
    public static readonly Uri DefaultBaseAddress = new("https://api.ouraring.com/");

    private const string CollectionPath = "v2/usercollection/";

    /// <summary>ページングの上限（無限ループ防止）</summary>
    private const int MaxPages = 100;

    /// <summary>
    /// 同時に送るリクエストの上限。
    /// 1 回の読み込みで必要な分（約 10 件）は一度に送る（4 件に絞ると 3 回に分かれて待ち時間が約 3 倍になった）。
    /// 連打による大量送信は、画面側の間引き（期間の切り替えから 0.4 秒待って読み込む）で防ぐ。
    /// </summary>
    public const int MaxConcurrentRequests = 10;

    /// <summary>429 のときに待ってから再試行する待ち時間の上限（これより長い場合はエラーにする）</summary>
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _concurrency = new(MaxConcurrentRequests, MaxConcurrentRequests);

    private static readonly IReadOnlyDictionary<string, string> NoQuery = new Dictionary<string, string>();

    private readonly HttpClient _httpClient;
    private readonly IAccessTokenProvider _tokenProvider;
    private readonly Uri _baseAddress;

    public OuraApiClient(HttpClient httpClient, IAccessTokenProvider tokenProvider, Uri? baseAddress = null)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _baseAddress = baseAddress ?? httpClient.BaseAddress ?? DefaultBaseAddress;
    }

    public async Task<PersonalInfo?> GetPersonalInfoAsync(CancellationToken cancellationToken = default)
        => await GetJsonAsync<PersonalInfo>(CollectionPath + "personal_info", NoQuery, cancellationToken).ConfigureAwait(false);

    public Task<IReadOnlyList<DailySleep>> GetDailySleepAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
        => GetDailyCollectionAsync<DailySleep>("daily_sleep", start, end, cancellationToken);

    public Task<IReadOnlyList<DailyReadiness>> GetDailyReadinessAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
        => GetDailyCollectionAsync<DailyReadiness>("daily_readiness", start, end, cancellationToken);

    public Task<IReadOnlyList<DailyActivity>> GetDailyActivityAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
        => GetDailyCollectionAsync<DailyActivity>("daily_activity", start, end, cancellationToken);

    public Task<IReadOnlyList<SleepPeriod>> GetSleepPeriodsAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
        => GetDailyCollectionAsync<SleepPeriod>("sleep", start, end, cancellationToken);

    public Task<IReadOnlyList<DailySpO2>> GetDailySpO2Async(DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
        => GetDailyCollectionAsync<DailySpO2>("daily_spo2", start, end, cancellationToken);

    public Task<IReadOnlyList<DailyStress>> GetDailyStressAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
        => GetDailyCollectionAsync<DailyStress>("daily_stress", start, end, cancellationToken);

    public Task<IReadOnlyList<Workout>> GetWorkoutsAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
        => GetDailyCollectionAsync<Workout>("workout", start, end, cancellationToken);

    public async Task<IReadOnlyList<HeartRateSample>> GetHeartRateAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default)
    {
        if (end <= start)
        {
            throw new ArgumentException("終了日時は開始日時より後である必要があります。", nameof(end));
        }

        var query = new Dictionary<string, string>
        {
            ["start_datetime"] = FormatDateTime(start),
            ["end_datetime"] = FormatDateTime(end),
        };
        var items = await GetCollectionAsync<HeartRateSample>(CollectionPath + "heartrate", query, cancellationToken).ConfigureAwait(false);
        return items
            .Where(x => x.Timestamp >= start && x.Timestamp < end)
            .OrderBy(x => x.Timestamp)
            .ToList();
    }

    public Task<IReadOnlyList<RingConfiguration>> GetRingConfigurationsAsync(CancellationToken cancellationToken = default)
        => GetCollectionAsync<RingConfiguration>(CollectionPath + "ring_configuration", NoQuery, cancellationToken);

    public async Task<RingBatteryLevel?> GetLatestBatteryLevelAsync(CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string> { ["latest"] = "true" };
        var items = await GetCollectionAsync<RingBatteryLevel>(CollectionPath + "ring_battery_level", query, cancellationToken).ConfigureAwait(false);
        return items.MaxBy(x => x.Timestamp);
    }

    /// <summary>
    /// 日単位のコレクションを取得する。
    /// API の end_date の扱い（含む／含まない）に依存しないよう、1 日多く要求してから day で絞り込む。
    /// </summary>
    private async Task<IReadOnlyList<T>> GetDailyCollectionAsync<T>(string name, DateOnly start, DateOnly end, CancellationToken cancellationToken)
        where T : IDailyDocument
    {
        if (end < start)
        {
            throw new ArgumentException("終了日は開始日以降である必要があります。", nameof(end));
        }

        var query = new Dictionary<string, string>
        {
            ["start_date"] = FormatDate(start),
            ["end_date"] = FormatDate(end.AddDays(1)),
        };
        var items = await GetCollectionAsync<T>(CollectionPath + name, query, cancellationToken).ConfigureAwait(false);
        return items
            .Where(x => x.Day >= start && x.Day <= end)
            .OrderBy(x => x.Day)
            .ToList();
    }

    /// <summary>next_token を辿って全ページを取得する</summary>
    private async Task<IReadOnlyList<T>> GetCollectionAsync<T>(string path, IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken)
    {
        var results = new List<T>();
        var seenTokens = new HashSet<string>(StringComparer.Ordinal);
        string? nextToken = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var pageQuery = new Dictionary<string, string>(query);
            if (nextToken is not null)
            {
                pageQuery["next_token"] = nextToken;
            }

            var response = await GetJsonAsync<OuraCollectionResponse<T>>(path, pageQuery, cancellationToken).ConfigureAwait(false);
            if (response?.Data is { } data)
            {
                results.AddRange(data);
            }

            var token = response?.NextToken;

            // 同じ next_token が返り続ける場合は打ち切る（無限ループ・無駄なリクエストの防止）
            if (string.IsNullOrEmpty(token) || !seenTokens.Add(token))
            {
                break;
            }

            nextToken = token;
        }

        return results;
    }

    private async Task<T?> GetJsonAsync<T>(string path, IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken)
    {
        var uri = BuildUri(_baseAddress, path, query);
        string? rejectedToken = null;
        var authRetried = false;
        var rateLimitRetried = false;

        while (true)
        {
            var token = await _tokenProvider.GetAccessTokenAsync(rejectedToken, cancellationToken).ConfigureAwait(false);
            using var response = await SendAsync(uri, token, cancellationToken).ConfigureAwait(false);

            // 401: トークンを更新して 1 回だけ再試行する（スコープ不足は更新しても解決しないため再試行しない）
            if (response.StatusCode == HttpStatusCode.Unauthorized && !authRetried)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (OuraApiException.IsScopeErrorBody(body))
                {
                    throw await OuraApiException.FromResponseAsync(response, cancellationToken).ConfigureAwait(false);
                }

                authRetried = true;
                rejectedToken = token;
                continue;
            }

            // 429: 待ち時間が短ければ、指定された時間だけ待って 1 回だけ再試行する
            if (response.StatusCode == HttpStatusCode.TooManyRequests && !rateLimitRetried
                && GetRetryDelay(response) is { } delay && delay <= MaxRetryAfter)
            {
                rateLimitRetried = true;
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await OuraApiException.FromResponseAsync(response, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                return await JsonSerializer.DeserializeAsync<T>(stream, OuraJson.Options, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                throw new OuraApiException($"Oura API の応答を解析できませんでした（{path}）。", response.StatusCode, innerException: ex);
            }
        }
    }

    /// <summary>リクエストを送る（同時に送る数を制限し、通信エラーは利用者向けの例外にする）</summary>
    private async Task<HttpResponseMessage> SendAsync(Uri uri, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new OuraApiException("Oura API に接続できませんでした。ネットワーク接続を確認してください。", innerException: ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OuraApiException("Oura API からの応答がタイムアウトしました。", innerException: ex);
        }
        finally
        {
            _concurrency.Release();
        }
    }

    /// <summary>Retry-After（秒数または日時）から待ち時間を求める</summary>
    private static TimeSpan? GetRetryDelay(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }

        return null;
    }

    internal static Uri BuildUri(Uri baseAddress, string path, IReadOnlyDictionary<string, string> query)
    {
        var builder = new StringBuilder(path);
        var first = true;
        foreach (var (key, value) in query)
        {
            builder.Append(first ? '?' : '&');
            builder.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
            first = false;
        }

        return new Uri(baseAddress, builder.ToString());
    }

    internal static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal static string FormatDateTime(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
}
