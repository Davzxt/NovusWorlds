# Novus Worlds

Plataforma de jogos online estilo Roblox 2008.

## Requisitos
- Node.js 18+

## Install
```bash
npm install
```

## Run
```bash
npm start
```

## Deploy no Render
1. Cria conta no Render.com
2. Conecta GitHub
3. Build: `npm install`
4. Start: `npm start`
5. Variáveis (opcional):
   - `STRIPE_SECRET_KEY` = tua chave Stripe
   - `SESSION_SECRET` = texto aleatório

## Login Admin
- **Username:** NovusWorlds
- **Password:** admin2008

## Links
- Homepage: /
- Games: /games.html
- Catalog: /catalog.html
- Studio: /studio.html
- Avatar: /avatar.html
- Profile: /profile.html?user=username
- Admin: /admin/

## Stack
- Express.js + sql.js + WebSocket + Three.js + bcrypt