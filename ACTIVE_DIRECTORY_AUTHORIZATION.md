# Autorização do Geolab TI Tools pelo Active Directory

O aplicativo autoriza o usuário Windows atual pelo SID do grupo `G_TI_Helpdesk`. A validação ocorre em `App.OnStartup`, antes da criação de `MainWindow`. Não há tela de login nem coleta de credenciais.

## Configuração obrigatória

O SID configurado em `ActiveDirectoryAuthorization.cs` (`AuthorizedGroupSid`) foi fornecido pelo administrador da Geolab. Confirme que ele corresponde ao grupo `G_TI_Helpdesk` no Active Directory antes de distribuir o aplicativo. Não substitua por um SID estimado, pelo nome do grupo ou por uma lista local de usuários.

Um administrador com acesso ao Active Directory pode obter o SID do grupo por meio das ferramentas administrativas autorizadas da empresa, por exemplo:

```powershell
Get-ADGroup -Identity 'G_TI_Helpdesk' | Select-Object -ExpandProperty SID
```

Se o SID do grupo mudar, atualize `AuthorizedGroupSid` com o valor confirmado, mantendo-o como uma string SID (`S-1-5-21-...`). Não inclua credenciais no código. Revise e distribua a alteração pelo processo de controle de código da Geolab.

## Comportamento

- `WindowsIdentity.GetCurrent()` obtém a sessão Windows corrente.
- Uma identidade não autenticada é negada.
- `WindowsPrincipal.IsInRole(SecurityIdentifier)` verifica associação usando somente o SID configurado.
- SID ausente ou inválido, erro de identidade ou falha de validação resultam em negação (fail closed).
- A mensagem ao usuário não expõe detalhes internos da falha.

Essa checagem local não impede a modificação do executável. Para restringir a execução no ambiente corporativo, planeje também uma regra AppLocker implantada por GPO para o grupo `G_TI_Helpdesk`; esta implementação não altera políticas de domínio.

## Verificação posterior

Depois de configurar o SID, o desenvolvedor pode validar manualmente, sem alterar a configuração de produção, os cenários com uma conta membro do grupo, uma conta não membro, identidade não autenticada e falha na validação. O processo de build e teste fica a cargo do desenvolvedor.
