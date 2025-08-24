﻿using MessagePack;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;


namespace MareSynchronos.API.Data;

[MessagePackObject(keyAsPropertyName: true)]
public class FileReplacementData
{
    public FileReplacementData()
    {
        DataHash = new(() =>
        {
            var json = JsonSerializer.Serialize(this);
#pragma warning disable SYSLIB0021 // Type or member is obsolete
            using SHA256CryptoServiceProvider cryptoProvider = new();
#pragma warning restore SYSLIB0021 // Type or member is obsolete
            return BitConverter.ToString(cryptoProvider.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-", "", StringComparison.Ordinal);
        });
    }

    [JsonIgnore]
    public Lazy<string> DataHash { get; }
    public string[] GamePaths { get; set; } = Array.Empty<string>();
    public string Hash { get; set; } = string.Empty;
    public string FileSwapPath { get; set; } = string.Empty;
}