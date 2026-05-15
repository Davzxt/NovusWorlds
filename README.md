# Novus Worlds

Novus Worlds e uma plataforma retro inspirada em jogos sociais classicos. Inclui site, auth, catalogo, avatar R6, painel admin, SQLite e uma nova engine nativa em C++/Direct3D9 para Client e Studio estilo 2011.

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
2. No Render, use **New +** > **Blueprint** e selecione este repositorio. O arquivo `render.yaml` ja cria o Web Service free.
3. Se preferir criar manualmente, use **Web Service** e configure:
   - Build Command: `npm install`
   - Start Command: `npm start`
   - Environment: `Node`
4. Adicione as variaveis:
   - `SESSION_SECRET`: uma string longa e secreta.
   - `SUPABASE_URL`: URL do projeto Supabase, se for usar backup gratis.
   - `SUPABASE_SERVICE_ROLE_KEY`: service role key do Supabase, se for usar backup gratis.
   - `DATABASE_PATH`: `/var/data/novus.sqlite` apenas se usar Render Disk.
5. Para nao perder contas, jogos, visitas e inventario em redeploy, use Supabase backup gratis ou crie um **Persistent Disk** no Render:
   - Mount Path: `/var/data`
   - Size: o menor disponivel ja serve para comecar.
6. Deploy.

Render usa a variavel `PORT` automaticamente; o servidor ja respeita `process.env.PORT`.

## Multiplayer pequeno no Render

O WebSocket do jogo roda no mesmo Web Service Express, em `/ws/game/:gameId`. Isso serve para alpha pequeno com uma unica instancia Render:

- salas em memoria no processo Node;
- limite atual de 20 jogadores por sala;
- broadcast de estado a cada 50ms;
- heartbeat WebSocket a cada 30s para limpar conexoes mortas;
- endpoint de debug: `/api/realtime/status`.

Como o estado das salas fica em memoria, nao use varias instancias Render free ao mesmo tempo. Para escalar de verdade, mova o realtime para um servico dedicado com estado compartilhado ou Durable Objects por sala.

## Entrar em um jogo pelo Client nativo

O fluxo automatico usa ticket temporario:

1. Baixe e extraia `NovusWorldsClient-Windows.zip`.
2. Baixe e rode `NovusLauncherSetup.exe`.
3. No instalador, selecione `NovusWorldsClient.exe` e `NovusWorldsStudio.exe`.
4. O instalador registra `novus://` e `novus-studio://` no Windows.
5. No site, abra um jogo e clique em `Jogar`.
6. O site cria um ticket em `/api/legacy/tickets` e abre o client com `gameId`, `baseUrl`, `serverHost`, `serverPort` e `ticket`.

Modo facil: baixe `NovusLauncherSetup.exe` em `/download.html`. Ele abre um assistente grafico, pergunta se deseja instalar, instala Client e Studio automaticamente em `%LOCALAPPDATA%\NovusWorlds`, registra os protocolos e cria atalhos na area de trabalho. O launcher de protocolo nao exige Node.js.

Variaveis opcionais para o ticket do client nativo:

- `NOVUS_REALTIME_HOST`: host mostrado no ticket de realtime.
- `NOVUS_REALTIME_PORT`: porta mostrada no ticket de realtime.

Importante: o caminho novo usa o WebSocket do proprio Express para multiplayer pequeno. Render Web Service gratuito serve para alpha pequeno, mas nao para muitos jogadores reais.

## Engine nativa C++/Direct3D9

O Client e o Studio principais agora ficam em `native/`:

```text
native/engine/  Biblioteca comum: DataModel, JSON, HTTP WinHTTP, renderer D3D9
native/client/  Player Windows x64, aberto por novus://
native/studio/  Studio Windows x64, aberto por novus-studio://
```

Instale o toolchain uma vez:

```text
npm run native:toolchain
```

Build e empacotamento:

```text
npm run native:build
npm run native:client
npm run native:studio
npm run native:package
```

O CMake preset oficial e:

```text
cmake --preset windows-msvc
cmake --build --preset windows-release
```

### Modelo R6 do client

O primeiro corte nativo ja renderiza um R6 classico por partes no renderer D3D9. O proximo passo da engine e carregar OBJ/GLTF externo para usar o R6 customizado do projeto e hats GLTF do catalogo sem fallback.

Os endpoints `/api/legacy/place/:id` e `/api/legacy/studio-project` continuam existindo como API de tickets e mapas JSON para os apps nativos.

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
