using System.Security.Cryptography;

namespace ZaldoPrinter.Common.System;

public static class TokenGenerator
{
    public static string Generate(int bytes = 32)
    {
        var data = RandomNumberGenerator.GetBytes(bytes);
        return Convert.ToBase64String(data)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
