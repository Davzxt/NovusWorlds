# Novus Worlds Client

Client Godot .NET separado do site. Ele carrega mapas da API do Novus, monta partes 3D, usa `assets/r6/r6.gltf` e conecta ao servidor Godot via ENet.

Rodar local:

```powershell
powershell -ExecutionPolicy Bypass -File ..\tools\run-godot-client.ps1 1 http://localhost:3000
```

Argumentos:

- `--game 1`
- `--base-url http://localhost:3000`
- `--server 127.0.0.1`
- `--port 53640`
