using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NatsWebSocketBridge.Historian.Services;

/// <summary>
/// Service for computing and verifying data integrity checksums
/// Uses SHA-256 for compliance-grade integrity verification
/// </summary>
public interface IChecksumService
{
    /// <summary>
    /// Compute a checksum for a data object
    /// </summary>
    string ComputeChecksum(object data, string? previousHash = null);

    /// <summary>
    /// Compute a checksum for raw bytes
    /// </summary>
    string ComputeChecksum(byte[] data, string? previousHash = null);

    /// <summary>
    /// Verify a checksum matches the expected value
    /// </summary>
    bool VerifyChecksum(object data, string expectedChecksum, string? previousHash = null);

    /// <summary>
    /// Compute a chained checksum (for audit log chains)
    /// </summary>
    string ComputeChainedChecksum(object data, string previousHash);
}

/// <summary>
/// SHA-256 based checksum service implementation
/// </summary>
public class Sha256ChecksumService : IChecksumService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public string ComputeChecksum(object data, string? previousHash = null)
    {
        var json = JsonSerializer.Serialize(data, _jsonOptions);
        var input = string.IsNullOrEmpty(previousHash) ? json : $"{previousHash}::{json}";
        return ComputeHash(input);
    }

    public string ComputeChecksum(byte[] data, string? previousHash = null)
    {
        if (string.IsNullOrEmpty(previousHash))
        {
            return ComputeHash(data);
        }

        var previousBytes = Encoding.UTF8.GetBytes(previousHash + "::");
        var combined = new byte[previousBytes.Length + data.Length];
        Buffer.BlockCopy(previousBytes, 0, combined, 0, previousBytes.Length);
        Buffer.BlockCopy(data, 0, combined, previousBytes.Length, data.Length);
        return ComputeHash(combined);
    }

    public bool VerifyChecksum(object data, string expectedChecksum, string? previousHash = null)
    {
        var computedChecksum = ComputeChecksum(data, previousHash);
        return string.Equals(computedChecksum, expectedChecksum, StringComparison.OrdinalIgnoreCase);
    }

    public string ComputeChainedChecksum(object data, string previousHash)
    {
        return ComputeChecksum(data, previousHash);
    }

    private static string ComputeHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        return ComputeHash(bytes);
    }

    private static string ComputeHash(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
