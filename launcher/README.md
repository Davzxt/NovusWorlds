# Novus Worlds Launcher

Ponte local para abrir o Novus Worlds Client e o Novus Worlds Studio pelo site.

## Uso facil

1. Abra `/download.html` no site.
2. Baixe `NovusLauncherSetup.exe`.
3. Rode o assistente grafico e confirme a instalacao.
4. O instalador baixa Client e Studio nativos, instala em `%LOCALAPPDATA%\NovusWorlds`, registra `novus://` e `novus-studio://`, e cria atalhos na area de trabalho.

Depois disso, clique em `Jogar` na pagina de um jogo. O site cria um ticket temporario e o Windows abre o Client automaticamente.

## Protocolos

- `novus://join?...` abre o Player com `gameId`, `baseUrl`, `server`, `port` e `ticket`.
- `novus-studio://edit?...` abre o Studio com o projeto do usuario.

## Multiplayer

O launcher instalado usa PowerShell nativo do Windows, nao exige Node.js, baixa o ticket do site e inicia o executavel correto.
