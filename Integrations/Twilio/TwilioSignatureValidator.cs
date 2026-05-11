using System.Security.Cryptography;
using System.Text;

namespace ThoughtBuffer.Integrations.Twilio;

public sealed class TwilioSignatureValidator(TwilioOptions options)
{
    public bool IsValid(Uri requestUri, IReadOnlyDictionary<string, IReadOnlyList<string>> form, string? signatureHeader)
    {
        if (!options.ValidateSignatures)
            return true;

        if (string.IsNullOrWhiteSpace(options.AuthToken) || string.IsNullOrWhiteSpace(signatureHeader))
            return false;

        var payloadBuilder = new StringBuilder(requestUri.ToString());

        foreach (var pair in form.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            foreach (var value in pair.Value.OrderBy(value => value, StringComparer.Ordinal))
            {
                payloadBuilder.Append(pair.Key);
                payloadBuilder.Append(value);
            }
        }

        var payload = payloadBuilder.ToString();
        var key = Encoding.UTF8.GetBytes(options.AuthToken);
        var data = Encoding.UTF8.GetBytes(payload);

        using var hmac = new HMACSHA1(key);
        var computed = Convert.ToBase64String(hmac.ComputeHash(data));

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(signatureHeader));
    }
}
