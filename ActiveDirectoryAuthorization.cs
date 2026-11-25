using System.Security.Principal;

namespace ResetExplode;

/// <summary>Autoriza o usuário Windows atual exclusivamente pelo SID do grupo corporativo permitido.</summary>
public static class ActiveDirectoryAuthorization
{
    // SID de G_TI_Helpdesk fornecido pelo administrador da Geolab.
    private const string AuthorizedGroupSid = "S-1-5-21-3018626299-1696653-297931992-26130";

    public sealed record AuthorizationResult(bool IsAuthorized, string UserName, string FailureReason);

    /// <summary>Retorna a identidade Windows atual para exibição ou diagnóstico local; nunca é usada para autorizar.</summary>
    public static string GetCurrentUser()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return identity.Name ?? "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>Verifica se a identidade Windows atual está autenticada.</summary>
    public static bool IsCurrentIdentityAuthenticated()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return identity.IsAuthenticated;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Verifica a associação pelo SID configurado, sem usar nome de usuário ou senha.</summary>
    public static bool IsCurrentUserInAuthorizedGroup()
    {
        try
        {
            if (!TryGetConfiguredGroupSid(out SecurityIdentifier groupSid)) return false;
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            if (!identity.IsAuthenticated) return false;
            return new WindowsPrincipal(identity).IsInRole(groupSid);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Executa a validação completa uma única vez e nega acesso em qualquer falha.</summary>
    public static AuthorizationResult AuthorizeCurrentUser()
    {
        string userName = "";
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            userName = identity.Name ?? "";
            if (!identity.IsAuthenticated)
                return Denied(userName, "A identidade Windows não está autenticada.");

            if (!TryGetConfiguredGroupSid(out SecurityIdentifier groupSid))
                return Denied(userName, "O SID real de G_TI_Helpdesk ainda não foi configurado.");

            bool isMember = new WindowsPrincipal(identity).IsInRole(groupSid);
            return isMember
                ? new AuthorizationResult(true, userName, "")
                : Denied(userName, "A conta não pertence ao grupo autorizado.");
        }
        catch
        {
            return Denied(userName, "Não foi possível validar a identidade ou o grupo autorizado.");
        }
    }

    private static bool TryGetConfiguredGroupSid(out SecurityIdentifier groupSid)
    {
        groupSid = null!;
        if (string.IsNullOrWhiteSpace(AuthorizedGroupSid)) return false;

        try
        {
            groupSid = new SecurityIdentifier(AuthorizedGroupSid);
            return true;
        }
        catch
        {
            groupSid = null!;
            return false;
        }
    }

    private static AuthorizationResult Denied(string userName, string reason) =>
        new(false, userName, reason);
}
