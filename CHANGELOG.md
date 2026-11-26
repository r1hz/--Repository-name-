# Changelog

## Unreleased — consolidação arquitetural

- Agrupados os códigos-fonte de benchmark em pastas por CPU, GPU, memória, armazenamento e stress; namespaces, algoritmos, ciclo de vida e métricas foram preservados.
- Movido o gerador de HTML/JSON para `Presentation/Services/Reports` e o diagnóstico de rede para `Presentation/Services/Network`, sem alterações de namespace ou lógica.
- Documentado o estado incremental das camadas, MVVM, DI, dependências e limitações em `ARCHITECTURE.md`.
- Removido o projeto WPF temporário versionado que continha caminhos absolutos de outra máquina.
- Ignorados os diretórios locais de cache `.dotnet-cli/` e `.nuget-packages/`.
- Não foram executados benchmarks, instaladores nem operações reais no Windows nesta etapa.
