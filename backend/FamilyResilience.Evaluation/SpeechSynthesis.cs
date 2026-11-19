using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace FamilyResilience.Evaluation;

/// <summary>
/// Synthetic speech for the voice scenarios: OpenAI text-to-speech as raw PCM 16-bit mono at 24 kHz, cached in the
/// temporary folder by a hash of the text so reruns cost nothing. No real voice is ever used.
/// </summary>
internal sealed class SpeechSynthesis(HttpClient http, string apiKey)
{
    private const string SpeechAddress = "https://api.openai.com/v1/audio/speech";

    private static readonly string Cache = Path.Combine(Path.GetTempPath(), "azwah-evaluation", "speech");

    public async Task<byte[]> PcmAsync(string text, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Cache);
        var file = Path.Combine(Cache, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..24] + ".pcm");
        if (File.Exists(file))
        {
            return await File.ReadAllBytesAsync(file, cancellationToken);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, SpeechAddress)
        {
            Content = new StringContent(
                new JsonObject
                {
                    ["model"] = "gpt-4o-mini-tts",
                    ["voice"] = "onyx",
                    ["input"] = text,
                    ["response_format"] = "pcm",
                }.ToJsonString(),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var pcm = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        await File.WriteAllBytesAsync(file, pcm, cancellationToken);
        return pcm;
    }
}
