# Arquitetura do GEOLAB TI TOOLS

## Estado atual

A modernização está em andamento e usa quatro camadas lógicas, mas ainda não tem quatro projetos executáveis independentes. O projeto WPF `ResetExplode.csproj` continua na raiz e funciona como Presentation, ponto de entrada e composition root. `Domain`, `Application` e `Infrastructure` são projetos sob `src/`. Essa disposição preserva a inicialização WPF, o manifesto, os recursos e a publicação single-file existentes.

```text
GeolabTITools.Domain
        ↑
GeolabTITools.Application
        ↑                         ↑
GeolabTITools.Infrastructure ← GeolabTITools (WPF / Presentation)
```

Regras de referência: Domain não referencia outros projetos; Application pode referenciar Domain; Infrastructure implementa contratos de Application; o WPF referencia Application e Infrastructure para composição. A referência Infrastructure → Domain existe no projeto, embora Domain ainda não contenha regras de negócio extraídas.

## Projetos e responsabilidades

| Projeto | Responsabilidade real hoje | Observações |
| --- | --- | --- |
| `src/GeolabTITools.Domain` | Núcleo de regras puras | Projeto vazio; não inventar entidades até regras de negócio serem extraídas. |
| `src/GeolabTITools.Application` | Contratos, DTOs e serviços puros por feature | Inclui contratos/DTOs de discos, informações do sistema e fronteiras Windows. |
| `src/GeolabTITools.Infrastructure` | Adaptadores Windows para serviços migrados | Inclui filesystem/discos, WMI de informações do sistema, processo e Registro. |
| `ResetExplode.csproj` (raiz) | Aplicação WPF, Presentation e composition root | Ainda contém a maior parte das funcionalidades, serviços legados, ViewModels e code-behind. |

`Assets/` e `Drivers/` permanecem na raiz e são incluídos como recursos do aplicativo. Não mover `Drivers/` sem rever a extração dos pacotes serial e as regras de publicação.

## Organização de funcionalidades

- `src/GeolabTITools.Application/Features/Disks`: contratos, DTOs e consulta/ordenação do DiskAnalyzer.
- `src/GeolabTITools.Application/Features/SystemInfo`: contrato e DTO da árvore de informações.
- `src/GeolabTITools.Application/Features/Windows`: contratos e DTOs para execução de processos e acesso ao Registro.
- `src/GeolabTITools.Infrastructure/FileSystem`: discos, filesystem e operações com arquivos/pastas.
- `src/GeolabTITools.Infrastructure/Hardware/SystemInfo`: consultas WMI do inventário do sistema.
- `src/GeolabTITools.Infrastructure/Windows`: adaptadores de processos e Registro.
- `Presentation/Views` e `Presentation/ViewModels`: telas migradas; atualmente DiskAnalyzer e a tela Sistema.
- `Benchmarks/`: algoritmos agrupados fisicamente por CPU, GPU, memória, armazenamento e stress. Mantêm os namespaces originais e continuam compilados no projeto WPF.
- `Presentation/Services/Reports` e `Presentation/Services/Network`: organização física; os serviços ainda não foram migrados para projetos/camadas independentes.

Os benchmarks de GPU têm implementações distintas para D3D11 e D3D12/DXR. Seus ciclos de vida de janela, renderização, recursos Vortice/COM, cancelamento e métricas não foram combinados nem alterados. O agrupamento em pastas não constitui validação de execução de benchmark.

## Dependências e injeção

`App.OnStartup` preserva o mutex de instância única, registra `AddGeolabInfrastructure`, registra serviços de Presentation/Application, constrói o `ServiceProvider` com `ValidateOnBuild` e resolve `MainWindow`. `MainWindow` permanece singleton; ViewModels de tela são transient.

Contratos atualmente registrados em Infrastructure:

| Contrato | Adaptador |
| --- | --- |
| `IDiskInfoService` | `DiskInfoService` |
| `IDiskScannerService` | `DiskScannerService` |
| `IDiskFileOperations` | `DiskFileOperations` |
| `ISystemInfoService` | `SystemInfoCollector` |
| `IProcessExecutionService` | `ProcessExecutionService` |
| `IRegistryAccess` | `RegistryAccess` |

`IDiskFileQueryService`, `IDiskClipboardService`, `IDriveMapReader`, `SystemViewModel` e `DiskAnalyzerViewModel` são registrados no composition root WPF. O registro desses serviços não executa processos nem altera o Registro.

Para novos módulos: defina contratos/DTOs de aplicação sem referências à Presentation; implemente adaptadores externos em Infrastructure; registre-os em DI; injete contratos no ViewModel; mantenha binding e comandos no XAML; e adicione testes isolados antes de ligar fluxos que alterem o sistema operacional.

## MVVM e limites ainda pendentes

`Presentation/ViewModels/DiskAnalyzer` e `Presentation/ViewModels/System` contêm os ViewModels migrados. A tela Sistema recebe o ViewModel como DataContext e usa bindings para categorias, seleção, detalhes e unidades. DiskAnalyzer usa comandos e contratos.

As outras telas ainda dependem de eventos e controles nomeados em `MainWindow.xaml.cs` e nos arquivos parciais. A janela ainda concentra dashboard, sensores, relatórios, energia/rede, drivers, Maker, ajustes, perfis de usuário, benchmarks, timers e navegação. Isso não é MVVM consistente em todo o aplicativo.

Pendências arquiteturais conhecidas:

- Mover o projeto WPF para `src/GeolabTITools.Presentation` requer uma etapa própria para recursos, XAML, manifesto, assets, drivers e publicação.
- `DriverManager`, `MakerDrivers`, `WindowsTweaks`, `Optimizer`, `StartupManager` e `UsbCleaner` ainda contêm chamadas diretas ao Windows e não consomem os novos contratos de processo/Registro.
- `DiskHealth`, `DriveMap`, `BatteryInfo`, `GpuInfoCollector` e `SensorMonitor` ainda não foram totalmente extraídos.
- `AppSettings` combina serialização JSON, migração de dados e estado de apresentação/recuperação.
- ReportBuilder, rede e histórico de benchmarks ainda dependem da aplicação WPF; não há repositório/serviço de persistência isolado.
- Ainda não há abstração central de logging.
- Não existe projeto de testes automatizados no repositório.

## Build, teste e publicação

Build local da aplicação:

```powershell
dotnet build ResetExplode.csproj -c Release
```

Publicação usada pelo GitHub Actions e baseada no .NET 8:

```powershell
dotnet publish ResetExplode.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o dist
```

O workflow `.github/workflows/publicar.yml` publica x64 e x86, empacota EXEs e hashes e cria/atualiza releases. `publicar.bat` também executa publish local, mas fecha primeiro qualquer `GeolabTITools.exe` e apaga `obj/Release`; leia o script antes de executá-lo. Esses procedimentos não fazem parte do build de validação.

O arquivo `ResetExplode_tcnmuhut_wpftmp.csproj` era um artefato WPF versionado com caminhos absolutos de uma máquina antiga e foi removido. Builds devem apontar para `ResetExplode.csproj` ou para os projetos em `src/`.
