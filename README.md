# Novus Worlds

Novus Worlds e uma plataforma web retro inspirada em jogos sociais de 2008. Inclui site, auth, catalogo, avatar R6, Studio 3D, cliente de jogo Three.js, WebSocket multiplayer, painel admin e SQLite.

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
6. Deploy.

Render usa a variavel `PORT` automaticamente; o servidor ja respeita `process.env.PORT`.

## Aviso importante sobre Render gratuito

Render suporta WebSockets em Web Services, mas o plano gratuito tem limites importantes:

- O servico pode dormir depois de 15 minutos sem requests HTTP ou mensagens WebSocket.
- O filesystem local e efemero. SQLite local e uploads locais podem ser perdidos em restart, redeploy ou spin down.
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
