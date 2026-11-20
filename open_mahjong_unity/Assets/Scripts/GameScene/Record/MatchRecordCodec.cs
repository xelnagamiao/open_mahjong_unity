using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>Canonical integrity checks shared with the explicit V1 migration tool.</summary>
public static class MatchRecordCodec {
    public static JObject Parse(string json) {
        using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None })
            return JObject.Load(reader);
    }
    static readonly string[] SmallIntegers = Enumerable.Range(0, 256).Select(n => n.ToString(CultureInfo.InvariantCulture)).ToArray();
    static readonly Encoding Utf8 = new UTF8Encoding(false);

    public static string Canonical(JToken token) {
        using var text = new StringWriter(CultureInfo.InvariantCulture);
        WriteCanonical(text, token);
        return text.ToString();
    }

    static void WriteCanonical(TextWriter writer, JToken token) {
        switch (token.Type) {
            case JTokenType.Object:
                writer.Write('{');
                bool first = true;
                foreach (var property in ((JObject)token).Properties().OrderBy(p => p.Name, StringComparer.Ordinal)) {
                    if (!first) writer.Write(',');
                    first = false;
                    WriteString(writer, property.Name);
                    writer.Write(':');
                    WriteCanonical(writer, property.Value);
                }
                writer.Write('}');
                break;
            case JTokenType.Array:
                writer.Write('[');
                var array = (JArray)token;
                for (int i = 0; i < array.Count; i++) {
                    if (i != 0) writer.Write(',');
                    WriteCanonical(writer, array[i]);
                }
                writer.Write(']');
                break;
            case JTokenType.String:
                WriteString(writer, (string)token);
                break;
            case JTokenType.Integer:
                var value = ((JValue)token).Value;
                if (value is long n && n >= 0 && n < SmallIntegers.Length)
                    writer.Write(SmallIntegers[(int)n]);
                else
                    writer.Write(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
            case JTokenType.Float:
                writer.Write(CanonicalFloat((double)token));
                break;
            case JTokenType.Boolean:
                writer.Write((bool)token ? "true" : "false");
                break;
            case JTokenType.Null:
                writer.Write("null");
                break;
            default:
                writer.Write(token.ToString(Formatting.None));
                break;
        }
    }

    static string CanonicalFloat(double number) {
        if (double.IsNaN(number) || double.IsInfinity(number))
            throw new FormatException("Non-finite number");
        if (number == Math.Truncate(number))
            return new System.Numerics.BigInteger(number).ToString(CultureInfo.InvariantCulture);
        string raw = number.ToString("G17", CultureInfo.InvariantCulture);
        bool negative = raw.StartsWith("-", StringComparison.Ordinal);
        if (negative) raw = raw.Substring(1);
        string[] exponent = raw.ToLowerInvariant().Split('e');
        int point = exponent[0].IndexOf('.');
        if (point < 0) point = exponent[0].Length;
        if (exponent.Length == 2) point += int.Parse(exponent[1], CultureInfo.InvariantCulture);
        string digits = exponent[0].Replace(".", "");
        string value = point <= 0 ? "0." + new string('0', -point) + digits
            : point >= digits.Length ? digits + new string('0', point - digits.Length)
            : digits.Insert(point, ".");
        if (value.Contains(".")) value = value.TrimEnd('0').TrimEnd('.');
        return value == "0" ? "0" : (negative ? "-" : "") + value;
    }

    static void WriteString(TextWriter writer, string value) {
        writer.Write('"');
        foreach (char c in value) {
            switch (c) {
                case '"': writer.Write("\\\""); break;
                case '\\': writer.Write("\\\\"); break;
                case '\b': writer.Write("\\b"); break;
                case '\f': writer.Write("\\f"); break;
                case '\n': writer.Write("\\n"); break;
                case '\r': writer.Write("\\r"); break;
                case '\t': writer.Write("\\t"); break;
                default:
                    if (c < 32) {
                        writer.Write("\\u");
                        writer.Write(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    } else writer.Write(c);
                    break;
            }
        }
        writer.Write('"');
    }

    public static string Digest(JToken token) {
        using var sha = SHA256.Create();
        // Stream directly into the hash. A player's library can contain millions of action operands.
        using (var crypto = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write))
        // This overload is preserved in the existing Android AOT baseline.
        using (var writer = new StreamWriter(crypto, Utf8, 4096, leaveOpen: false))
            WriteCanonical(writer, token);
        return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
    }

    public static bool IsUnified(JObject record) =>
        (string)record?["format"] == "open_mahjong.match_record" && (int?)record["schema_version"] == 2;

    public static string GameId(JObject record) {
        if ((string)record["match_format"] != "paired_tables_2v2")
            return (string)record["tables"]?["main"]?["table_record_id"];
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes((string)record["match_id"]));
        var result = new StringBuilder("P");
        int bits = 0, buffer = 0;
        foreach (byte b in hash) {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5) {
                bits -= 5;
                result.Append(alphabet[(buffer >> bits) & 31]);
                if (result.Length == 16) return result.ToString();
            }
        }
        throw new InvalidOperationException("Invalid SHA-256 hash");
    }

    public static string Identity(params object[] parts) {
        // Python uuid.uuid5 uses the namespace in network byte order.
        byte[] ns = { 0xc2,0xc8,0x4d,0x54,0x32,0x2a,0x4f,0xb4,0x9a,0xd0,0x54,0xae,0xe5,0x9b,0x1c,0xe3 };
        using var sha = SHA1.Create();
        byte[] hash = sha.ComputeHash(ns.Concat(Encoding.UTF8.GetBytes(Canonical(JArray.FromObject(parts)))).ToArray());
        hash[6] = (byte)((hash[6] & 15) | 0x50);
        hash[8] = (byte)((hash[8] & 63) | 0x80);
        return Guid.ParseExact(BitConverter.ToString(hash, 0, 16).Replace("-", ""), "N").ToString();
    }
}
