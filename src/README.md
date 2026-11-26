# Projetos arquiteturais

Este diretório contém os projetos Domain, Application e Infrastructure. O projeto WPF ainda está em `ResetExplode.csproj`, na raiz, para preservar os recursos e o pipeline atual.

A documentação canônica de dependências, registros DI, módulos migrados e débitos arquiteturais está em [../ARCHITECTURE.md](../ARCHITECTURE.md). O padrão piloto do DiskAnalyzer e as migrações graduais também estão descritos ali.

Os contratos de Application não devem depender da Presentation nem de implementações de Infrastructure. Novas implementações de acesso externo pertencem a Infrastructure e entram pelo ponto de composição WPF. A migração das classes estáticas existentes deve ser feita por funcionalidade e coberta com substitutos antes de conectar operações que alterem o Windows.
