# Novus Legacy Launcher

Este launcher e a ponte local para client/studio Roblox 2012.

## Instalar

1. Instale Node.js no Windows do jogador.
2. Copie `config.example.json` para `config.json`.
3. Configure:

```json
{
  "playerExe": "C:\\Novus2012\\RobloxPlayerBeta.exe",
  "studioExe": "C:\\Novus2012\\RobloxStudioBeta.exe",
  "cacheDir": "%LOCALAPPDATA%\\NovusWorlds\\Cache",
  "launchMode": "dry-run"
}
```

4. Rode `install-protocols.bat`.
5. Para testar sem abrir executavel, mantenha `"launchMode": "dry-run"`.

## Fluxo

`novus://join`:

- baixa join script
- baixa avatar appearance
- baixa place JSON
- chama `playerExe`

`novus-studio://edit`:

- baixa projeto do Studio
- chama `studioExe`

## Pendente para client 2012 real

O launcher ainda precisa de adaptadores especificos para:

- converter place JSON para `.rbxl` ou script de criação de Parts;
- converter hats GLTF/GLB para mesh/acessorio legado;
- aplicar shirts/pants/faces no formato esperado pelo client;
- iniciar o Roblox 2012 com os argumentos reais da build escolhida.
