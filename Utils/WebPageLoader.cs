/// <summary>
/// Provides simple HTTP GET and form-encoded POST helpers for web search providers.
/// </summary>
public static class WebPageLoader
{
    /// <summary>
    /// Sends a form-encoded HTTP POST request and returns the response body.
    /// </summary>
    /// <param name="url">The request URL.</param>
    /// <param name="timeout">The request timeout.</param>
    /// <param name="postData">The form fields to submit.</param>
    /// <returns>The response body, or the HTTP exception message when the request fails.</returns>
    public static async Task<string> Post(string url, TimeSpan timeout, Dictionary<string, string> postData)
    {
        using HttpClient client = new() { Timeout = timeout };

        using var content = new FormUrlEncodedContent(postData);

        try
        {
            var response = await client.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }

            throw new Exception($"Request to {url} failed with status code: {response.StatusCode}");
        }
        catch(HttpRequestException ex)
        {
            return ex.Message;
        }
    }
    /// <summary>
    /// Sends an HTTP GET request with optional headers and returns the response body.
    /// </summary>
    /// <param name="url">The request URL.</param>
    /// <param name="timeout">The request timeout.</param>
    /// <param name="headers">Optional HTTP headers.</param>
    /// <returns>The response body, or the HTTP exception message when the request fails.</returns>
    public static async Task<string> Get(string url, TimeSpan timeout, Dictionary<string,string?>? headers = null)
    {
        using HttpClient client = new() { Timeout = timeout };

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);

            if (headers != null)
                foreach (var item in headers)
                {
                    request.Headers.Add(item.Key, item.Value);
                }

            var response = await client.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }

            throw new Exception($"Request to {url} failed with status code: {response.StatusCode}");
        }
        catch (HttpRequestException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Represents a normalized web-search result.
    /// </summary>
    public class SearchResultItem
    {
        /// <summary>
        /// Gets or sets the result title.
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// Gets or sets the result URL.
        /// </summary>
        public string? Link { get; set; }

        /// <summary>
        /// Gets or sets the result snippet or extracted content.
        /// </summary>
        public string? Content { get; set; }
    }
}
