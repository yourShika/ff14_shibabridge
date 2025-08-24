﻿namespace MareSynchronos.Services;

public record MareProfileData(bool IsFlagged, bool IsNSFW, string Base64ProfilePicture, string Description)
{
    public Lazy<byte[]> ImageData { get; } = new Lazy<byte[]>(Convert.FromBase64String(Base64ProfilePicture));
}
