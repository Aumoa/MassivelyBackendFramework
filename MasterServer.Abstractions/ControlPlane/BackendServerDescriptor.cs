using System;
using System.Security.Cryptography;
using System.Text;

namespace MasterServer.ControlPlane;

public sealed class BackendServerDescriptor
{
    public const int MaxDescriptorJsonUtf8Bytes = 16 * 1024;
    public const int MaxDescriptorJsonDepth = 64;
    public const int MaxDescriptorVersionLength = 128;
    public const int DescriptorHashHexLength = 64;

    private static readonly string[] s_ForbiddenPropertyNameFragments =
    {
        "credential",
        "connectionstring",
        "endpoint",
        "hostname",
        "internaladdress",
        "internalip",
        "internalport",
        "ipaddress",
        "password",
        "private",
        "privateaddress",
        "privateip",
        "privateport",
        "secret",
        "token"
    };

    public BackendServerDescriptor(
        BackendNodeState state,
        string descriptorVersion,
        string descriptorHash,
        string descriptorJson)
    {
        ValidateState(state);
        DescriptorVersion = NormalizeDescriptorVersion(descriptorVersion);
        DescriptorJson = NormalizeDescriptorJson(descriptorJson);
        var expectedHash = ComputeDescriptorHash(DescriptorJson);
        if (!string.Equals(expectedHash, descriptorHash, StringComparison.Ordinal))
        {
            throw new ArgumentException("Descriptor hash does not match descriptor JSON.", nameof(descriptorHash));
        }

        State = state;
        DescriptorHash = descriptorHash;
    }

    public BackendNodeState State { get; }

    public string DescriptorVersion { get; }

    public string DescriptorHash { get; }

    public string DescriptorJson { get; }

    public static BackendServerDescriptor DefaultOpen { get; } = Create(
        BackendNodeState.Open,
        "1",
        "{}");

    public static BackendServerDescriptor Create(
        BackendNodeState state,
        string descriptorVersion,
        string descriptorJson)
    {
        ValidateState(state);
        var normalizedVersion = NormalizeDescriptorVersion(descriptorVersion);
        var normalizedJson = NormalizeDescriptorJson(descriptorJson);
        return new BackendServerDescriptor(
            state,
            normalizedVersion,
            ComputeDescriptorHash(normalizedJson),
            normalizedJson);
    }

    public static string ComputeDescriptorHash(string descriptorJson)
    {
        var normalizedJson = NormalizeDescriptorJson(descriptorJson);
        using var sha256 = SHA256.Create();
        return ToUpperHex(sha256.ComputeHash(Encoding.UTF8.GetBytes(normalizedJson)));
    }

    private static string ToUpperHex(byte[] bytes)
    {
        var chars = new char[bytes.Length * 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            var value = bytes[i];
            chars[i * 2] = GetHexChar(value >> 4);
            chars[(i * 2) + 1] = GetHexChar(value & 0xF);
        }

        return new string(chars);
    }

    private static char GetHexChar(int value)
    {
        return (char)(value < 10
            ? '0' + value
            : 'A' + value - 10);
    }

    private static string NormalizeDescriptorVersion(string descriptorVersion)
    {
        if (string.IsNullOrWhiteSpace(descriptorVersion))
        {
            throw new ArgumentException("Descriptor version is required.", nameof(descriptorVersion));
        }

        var normalized = descriptorVersion.Trim();
        if (normalized.Length > MaxDescriptorVersionLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(descriptorVersion),
                $"Descriptor version must be {MaxDescriptorVersionLength} characters or shorter.");
        }

        return normalized;
    }

    private static string NormalizeDescriptorJson(string descriptorJson)
    {
        if (descriptorJson == null)
        {
            throw new ArgumentNullException(nameof(descriptorJson));
        }

        var normalized = descriptorJson.Trim();
        if (normalized.Length == 0)
        {
            normalized = "{}";
        }

        var utf8Bytes = Encoding.UTF8.GetBytes(normalized);
        if (utf8Bytes.Length > MaxDescriptorJsonUtf8Bytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(descriptorJson),
                $"Descriptor JSON must be {MaxDescriptorJsonUtf8Bytes} UTF-8 bytes or smaller.");
        }

        new DescriptorJsonParser(normalized).ValidateRootObject();
        return normalized;
    }

    private sealed class DescriptorJsonParser
    {
        private readonly string m_Text;
        private int m_Index;

        public DescriptorJsonParser(string text)
        {
            m_Text = text;
        }

        public void ValidateRootObject()
        {
            SkipWhitespace();
            ReadObject(1);
            SkipWhitespace();
            if (m_Index != m_Text.Length)
            {
                ThrowInvalidJson();
            }
        }

        private void ReadValue(int depth)
        {
            RequireDepth(depth);
            SkipWhitespace();
            if (m_Index >= m_Text.Length)
            {
                ThrowInvalidJson();
            }

            var current = m_Text[m_Index];
            if (current == '{')
            {
                ReadObject(depth);
            }
            else if (current == '[')
            {
                ReadArray(depth);
            }
            else if (current == '"')
            {
                ReadString();
            }
            else if (current == 't')
            {
                ReadLiteral("true");
            }
            else if (current == 'f')
            {
                ReadLiteral("false");
            }
            else if (current == 'n')
            {
                ReadLiteral("null");
            }
            else
            {
                ReadNumber();
            }
        }

        private void ReadObject(int depth)
        {
            RequireDepth(depth);
            ReadRequired('{');
            SkipWhitespace();
            if (TryRead('}'))
            {
                return;
            }

            while (true)
            {
                SkipWhitespace();
                var propertyName = ReadString();
                if (ContainsForbiddenPropertyNameFragment(propertyName))
                {
                    throw new ArgumentException(
                        $"Descriptor JSON property '{propertyName}' looks like private infrastructure or credential data.",
                        "descriptorJson");
                }

                SkipWhitespace();
                ReadRequired(':');
                ReadValue(depth + 1);
                SkipWhitespace();

                if (TryRead('}'))
                {
                    return;
                }

                ReadRequired(',');
            }
        }

        private void ReadArray(int depth)
        {
            RequireDepth(depth);
            ReadRequired('[');
            SkipWhitespace();
            if (TryRead(']'))
            {
                return;
            }

            while (true)
            {
                ReadValue(depth + 1);
                SkipWhitespace();
                if (TryRead(']'))
                {
                    return;
                }

                ReadRequired(',');
            }
        }

        private string ReadString()
        {
            ReadRequired('"');
            var start = m_Index;
            StringBuilder? builder = null;

            while (m_Index < m_Text.Length)
            {
                var current = m_Text[m_Index++];
                if (current == '"')
                {
                    return builder == null
                        ? m_Text.Substring(start, m_Index - start - 1)
                        : builder.ToString();
                }

                if (current < ' ')
                {
                    ThrowInvalidJson();
                }

                if (current != '\\')
                {
                    builder?.Append(current);
                    continue;
                }

                builder ??= new StringBuilder(m_Text.Substring(start, m_Index - start - 1));
                if (m_Index >= m_Text.Length)
                {
                    ThrowInvalidJson();
                }

                var escaped = m_Text[m_Index++];
                if (escaped == '"' ||
                    escaped == '\\' ||
                    escaped == '/')
                {
                    builder.Append(escaped);
                }
                else if (escaped == 'b')
                {
                    builder.Append('\b');
                }
                else if (escaped == 'f')
                {
                    builder.Append('\f');
                }
                else if (escaped == 'n')
                {
                    builder.Append('\n');
                }
                else if (escaped == 'r')
                {
                    builder.Append('\r');
                }
                else if (escaped == 't')
                {
                    builder.Append('\t');
                }
                else if (escaped == 'u')
                {
                    builder.Append(ReadUnicodeEscape());
                }
                else
                {
                    ThrowInvalidJson();
                }
            }

            ThrowInvalidJson();
            return string.Empty;
        }

        private char ReadUnicodeEscape()
        {
            if (m_Index + 4 > m_Text.Length)
            {
                ThrowInvalidJson();
            }

            var value = 0;
            for (var i = 0; i < 4; i++)
            {
                value = (value << 4) + ReadHexValue(m_Text[m_Index++]);
            }

            return (char)value;
        }

        private void ReadNumber()
        {
            if (TryRead('-') &&
                m_Index >= m_Text.Length)
            {
                ThrowInvalidJson();
            }

            if (TryRead('0'))
            {
                if (m_Index < m_Text.Length &&
                    IsDigit(m_Text[m_Index]))
                {
                    ThrowInvalidJson();
                }
            }
            else
            {
                ReadDigits();
            }

            if (TryRead('.'))
            {
                ReadDigits();
            }

            if (m_Index < m_Text.Length &&
                (m_Text[m_Index] == 'e' || m_Text[m_Index] == 'E'))
            {
                m_Index++;
                if (m_Index < m_Text.Length &&
                    (m_Text[m_Index] == '+' || m_Text[m_Index] == '-'))
                {
                    m_Index++;
                }

                ReadDigits();
            }
        }

        private void ReadDigits()
        {
            var start = m_Index;
            while (m_Index < m_Text.Length &&
                   IsDigit(m_Text[m_Index]))
            {
                m_Index++;
            }

            if (m_Index == start)
            {
                ThrowInvalidJson();
            }
        }

        private void ReadLiteral(string literal)
        {
            for (var i = 0; i < literal.Length; i++)
            {
                if (m_Index + i >= m_Text.Length ||
                    m_Text[m_Index + i] != literal[i])
                {
                    ThrowInvalidJson();
                }
            }

            m_Index += literal.Length;
        }

        private void ReadRequired(char expected)
        {
            if (!TryRead(expected))
            {
                ThrowInvalidJson();
            }
        }

        private bool TryRead(char expected)
        {
            if (m_Index >= m_Text.Length ||
                m_Text[m_Index] != expected)
            {
                return false;
            }

            m_Index++;
            return true;
        }

        private void SkipWhitespace()
        {
            while (m_Index < m_Text.Length)
            {
                var current = m_Text[m_Index];
                if (current != ' ' &&
                    current != '\t' &&
                    current != '\r' &&
                    current != '\n')
                {
                    return;
                }

                m_Index++;
            }
        }

        private static void RequireDepth(int depth)
        {
            if (depth > MaxDescriptorJsonDepth)
            {
                throw new ArgumentException(
                    $"Descriptor JSON can nest at most {MaxDescriptorJsonDepth} levels.",
                    "descriptorJson");
            }
        }

        private static int ReadHexValue(char value)
        {
            if (value >= '0' && value <= '9')
            {
                return value - '0';
            }

            if (value >= 'a' && value <= 'f')
            {
                return value - 'a' + 10;
            }

            if (value >= 'A' && value <= 'F')
            {
                return value - 'A' + 10;
            }

            ThrowInvalidJson();
            return 0;
        }

        private static bool IsDigit(char value)
        {
            return value >= '0' && value <= '9';
        }

        private static void ThrowInvalidJson()
        {
            throw new ArgumentException("Descriptor JSON must be valid JSON with an object root.", "descriptorJson");
        }
    }

    private static bool ContainsForbiddenPropertyNameFragment(string propertyName)
    {
        foreach (var fragment in s_ForbiddenPropertyNameFragments)
        {
            if (propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateState(BackendNodeState state)
    {
        if (!Enum.IsDefined(typeof(BackendNodeState), state))
        {
            throw new ArgumentOutOfRangeException(nameof(state), "Unknown Backend node state.");
        }
    }
}
