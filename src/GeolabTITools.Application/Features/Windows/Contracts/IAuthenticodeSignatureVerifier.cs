using GeolabTITools.Application.Features.Windows.DTOs;

namespace GeolabTITools.Application.Features.Windows.Contracts;

public interface IAuthenticodeSignatureVerifier
{
    AuthenticodeVerificationResult Verify(string filePath);
}
