# Novus Worlds

Novus Worlds e uma plataforma retro inspirada em jogos sociais de 2008. Inclui site, auth, catalogo, avatar R6, Studio 3D, painel admin, SQLite e modo launcher para client Roblox antigo.

## Rodar localmente

```bash
npm install
npm run seed
npm start
```

Abra `http://localhost:3000`.

Conta admin criada automaticamente:

- Username: `NovusWorlds`
- Senha: `16477709d`

## Deploy no Render

1. Envie este projeto para um repositorio GitHub.
2. No Render, crie um **Web Service**.
3. Conecte o repositorio.
4. Configure:
   - Build Command: `npm install`
   - Start Command: `npm start`
   - Environment: `Node`
5. Adicione a variavel:
   - `SESSION_SECRET`: uma string longa e secreta.
   - `DATABASE_PATH`: `/var/data/novus.sqlite` se usar Render Disk.
6. Para nao perder contas, jogos, visitas e inventario em redeploy, crie um **Persistent Disk** no Render:
   - Mount Path: `/var/data`
   - Size: o menor disponivel ja serve para comecar.
7. Deploy.

Render usa a variavel `PORT` automaticamente; o servidor ja respeita `process.env.PORT`.

## Modo Client Roblox Antigo

O fluxo de jogo agora e launcher-based. `/game.html?id=X` cria um ticket e tenta abrir:

```text
novus://join?ticket=...&gameId=...&baseUrl=...
```

Endpoints para o launcher/client 2012:

```text
POST /api/legacy/tickets
GET  /api/legacy/join-script?ticket=...
GET  /api/legacy/avatar?ticket=...
GET  /api/legacy/avatar.xml?ticket=...
GET  /api/legacy/place/:id
GET  /api/legacy/assets/:id
```

Compatibilidade:

- Faces sao expostas como textura/decal.
- Shirts sao expostas como template de camisa.
- Pants sao expostas como template de calca.
- R6 e exposto no appearance endpoint para o launcher montar o personagem.
- Hats GLTF/GLB sao expostos como `modelUrl` + `hatTransform`, mas client Roblox 2008/2012 nao carrega GLTF diretamente. O launcher precisa converter para mesh/acessorio legado ou usar um formato ja compativel.

Render hospeda site/API. O client antigo e o game server precisam de launcher/ambiente Windows separado.

## Launcher local

Uma primeira versao do launcher fica em:

```text
launcher/
```

Ele registra:

```text
novus://join
novus-studio://edit
```

Uso:

```text
cd launcher
copy config.example.json config.json
install-protocols.bat
```

Por padrao o launcher vem em `dry-run`, entao ele baixa os arquivos de join/studio e imprime o que abriria. Configure `playerExe`, `studioExe` e mude `launchMode` quando tiver uma build 2012 real pronta.

## Aviso importante sobre Render gratuito

Render suporta WebSockets em Web Services, mas o plano gratuito tem limites importantes:

- O servico pode dormir depois de 15 minutos sem requests HTTP ou mensagens WebSocket.
- O filesystem local sem Persistent Disk e efemero. SQLite local e uploads locais podem ser perdidos em restart, redeploy ou spin down.
- Free nao escala alem de uma instancia.
- Muitos WebSockets consomem CPU, RAM e banda.

Para demo e beta pequeno, funciona. Para muitos players reais, use Render pago com:

- Instancia maior.
- Persistent Disk para SQLite e uploads, ou migracao para Postgres.
- Pelo menos uma instancia dedicada para WebSocket.
- CDN para assets.

## Como suportar muitos players

Mantenha salas pequenas, por exemplo 20 jogadores por instancia de jogo. Quando uma sala encher, crie outra sala. Para escala grande em Render, a arquitetura recomendada e:

```text
Render Web Service API
  auth, catalogo, site, studio

Render Web Service Realtime
  WebSocket de jogo
  varias instancias pagas

Postgres
  dados persistentes

Object Storage externo
  uploads, thumbnails, modelos
```

No plano gratuito, trate como alpha publico, nao como producao massiva.

## Persistencia no Render

Se voce nao configurar `DATABASE_PATH=/var/data/novus.sqlite` e um Persistent Disk montado em `/var/data`, o banco volta a nascer do zero em redeploy. Isso apaga contas, jogos, visitas, inventario e sessoes. Com Disk persistente, o SQLite fica salvo fora do pacote do deploy.

## Persistencia gratis com Supabase

Se voce nao pode usar Render Disk, use Supabase como backup do SQLite.

No Supabase SQL Editor, rode:

```sql
create table if not exists app_backups (
  id text primary key,
  data_base64 text not null,
  updated_at timestamptz default now()
);
```

No Render, adicione:

- `SUPABASE_URL`: URL do projeto Supabase.
- `SUPABASE_SERVICE_ROLE_KEY`: service role key do Supabase.
- `SUPABASE_BACKUP_INTERVAL_MS`: opcional, padrao `30000`.

O servidor restaura o banco salvo no Supabase quando `novus.sqlite` nao existe e faz backup automatico do SQLite para a tabela `app_backups`. Nao exponha a service role key no frontend.
