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
- baixa texturas/modelos do avatar para o cache local
- gera `.rbxlx` do mapa
- gera `avatar-appearance.lua` com `Hat`, `Decal`, `Shirt` e `Pants`
- chama `playerExe`

`novus-studio://edit`:

- baixa projeto do Studio
- gera `.rbxlx` temporario
- chama `studioExe`

## Observacao sobre assets 2012

O launcher gera objetos compativeis com o modelo de instancias do Roblox 2012:

- `Hat` com `Handle`, `SpecialMesh`, `MeshId`, `TextureId` e `Weld`
- `Decal` chamado `face` no `Head`
- `Shirt.ShirtTemplate`
- `Pants.PantsTemplate`
- cores R6 aplicadas em `Head`, `Torso`, `Left Arm`, `Right Arm`, `Left Leg` e `Right Leg`

Para hats em GLTF/GLB, o client 2012 real nao le esse formato nativamente. O upload fica aceito no site/admin, mas para renderizar exatamente no executavel antigo ele precisa estar em um formato que a build consiga carregar como `SpecialMesh.MeshId`, ou passar por conversao externa para mesh legado antes do uso.
