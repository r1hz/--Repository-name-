# AutorizaÃ§Ã£o do Geolab TI Tools pelo Active Directory

A validaÃ§Ã£o de associaÃ§Ã£o ao grupo `G_TI_Helpdesk` foi desativada no fluxo de inicializaÃ§Ã£o. O app nÃ£o bloqueia mais a abertura com base no usuÃ¡rio ou grupo do Active Directory.

A classe `ActiveDirectoryAuthorization` permanece no projeto, mas nÃ£o Ã© chamada pelo aplicativo. Para reativar o controle, restaure a chamada `AuthorizeCurrentUser()` em `App.OnStartup` e confirme com o administrador que `AuthorizedGroupSid` corresponde ao SID vigente do grupo.