namespace GeolabTITools.Application.Features.Windows.DTOs;

public sealed record AuthenticodeVerificationResult(bool SignaturePresent, bool Trusted, string? Signer, int TrustStatus, string? FailureReason);
