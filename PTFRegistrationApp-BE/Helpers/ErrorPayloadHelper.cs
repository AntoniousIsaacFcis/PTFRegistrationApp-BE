using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PTFRegistrationApp_BE.Helpers;

public static class ErrorPayloadHelper
{
    public static string Sanitize(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return JsonConvert.SerializeObject(new { Message = "Request failed.", Type = "Error" });
        }

        try
        {
            var token = JToken.Parse(responseBody);
            if (token is JObject obj)
            {
                var sanitized = new JObject();
                CopyIfExists(obj, sanitized, "Message");
                CopyIfExists(obj, sanitized, "Type");
                CopyIfExists(obj, sanitized, "ResultData");

                if (!sanitized.HasValues)
                {
                    sanitized["Message"] = "Request failed.";
                    sanitized["Type"] = "Error";
                }

                return sanitized.ToString(Formatting.None);
            }

            return JsonConvert.SerializeObject(new { Message = "Request failed.", Type = "Error" });
        }
        catch
        {
            return JsonConvert.SerializeObject(new { Message = "Request failed.", Type = "Error" });
        }
    }

    private static void CopyIfExists(JObject from, JObject to, string key)
    {
        if (from.TryGetValue(key, out var token))
        {
            to[key] = token;
        }
    }
}
