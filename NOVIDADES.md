# Novidades da v0.17

## Correcoes e melhorias
- A Visao Geral e o GEOLAB BENCH mostram o clock dos nucleos ativos da CPU.
- O registro do stress test pode ser redimensionado e mostra CPU, GPU e RAM.
- Sistema mostra a data de instalacao do Windows e mais dados do computador.
- Discos inclui teste de setores defeituosos, em modo rapido ou completo, somente leitura e cancelavel.
- A janela maximizada respeita a area util do monitor e nao cobre a barra de tarefas.
- Incluidos SECURITY.md e hashes SHA-256 nas releases.
- Releases experimentais tambem incluem executavel para Windows 32 bits.

## Novidades da v0.16

### Inicializacao do Windows
Lista programas que iniciam com o Windows, permite ativar/desativar entradas e remove-las com opcao de restauracao.

### Instalar apps
Instala Steam, Chrome, Discord, Lightshot, 7-Zip, WinRAR e VLC pelo winget, um por vez, e mostra o resultado.

### GEOLAB MAKER
- Pagina propria para limpar dispositivos seriais fantasmas e liberar numeros COM presos.
- Diagnostico, reinstalacao e gestao de versoes para CH340/CH341, CP210x, FTDI, Prolific e Arduino.
- Drivers oficiais incluidos no app para instalacao sem internet.
- Instalacao/atualizacao da Arduino IDE pelo winget e limpeza de cache de indices.

### Benchmark de ray tracing
Teste Direct3D 12 com DXR 1.1, 4 mil objetos, presets e resolucoes diferentes. Usa aquecimento e tres passadas; o resultado e a mediana e informa a variacao.

### Abertura mais rapida
A janela aparece antes da leitura do hardware. Sistema, sensores e GPU carregam em paralelo em segundo plano.
