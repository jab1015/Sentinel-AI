using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Sends user-initiated reports about AI-generated content to the Sentinel gateway.
    /// Reports intentionally exclude machine diagnostics and other unrelated system evidence.
    /// </summary>
    public sealed class AiContentReportService
    {
        private const string ProductionEndpoint =
            "https://sentinel-ai-gateway-49908265995.us-central1.run.app/v1/report-ai-content";

        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
        private readonly HttpClient _httpClient = new() { Timeout = RequestTimeout };

        public async Task<AiContentReportResult> SubmitAsync(
            string category,
            string comments,
            string responseText,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(responseText))
                return new(false, "There is no AI response available to report.");

            string safeCategory = NormalizeCategory(category);
            string safeComments = Limit((comments ?? string.Empty).Trim(), 1000);
            string safeResponse = Limit(responseText.Trim(), 2500);
            string responseId = CreateResponseId(responseText);

            var payload = new
            {
                schemaVersion = 1,
                responseId,
                category = safeCategory,
                comments = safeComments,
                responseText = safeResponse,
                reportedAtUtc = DateTimeOffset.UtcNow
            };

            try
            {
                using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
                    ProductionEndpoint,
                    payload,
                    cancellationToken).ConfigureAwait(false);

                return response.IsSuccessStatusCode
                    ? new(true, "Thank you. Your report was submitted for review.")
                    : new(false, "Sentinel could not submit the report right now. Please try again later.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new(false, "Sentinel could not submit the report right now. Please try again later.");
            }
            catch
            {
                return new(false, "Sentinel could not submit the report right now. Please try again later.");
            }
        }

        private static string NormalizeCategory(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            return normalized switch
            {
                "Inappropriate or offensive" => normalized,
                "Unsafe or harmful" => normalized,
                "Incorrect or misleading" => normalized,
                _ => "Other"
            };
        }

        private static string Limit(string value, int maxLength) =>
            value.Length <= maxLength ? value : value[..maxLength];

        private static string CreateResponseId(string responseText)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(responseText));
            return Convert.ToHexString(hash)[..16].ToLowerInvariant();
        }
    }

    public sealed record AiContentReportResult(bool Succeeded, string Message);
}
