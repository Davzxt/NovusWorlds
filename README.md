# Novus Worlds

Plataforma de jogos online estilo Roblox 2008, construída inteiramente para o navegador.

## Requisitos

- Node.js 18+
- npm 9+

## Instalação

```bash
npm install
```

## Executar

```bash
npm start
```

Servidor inicia em http://localhost:3000

##Conta Admin

- **Username:** NovusWorlds
- **Password:** admin2008

## Deploy no Render

1. Crie uma conta no [Render](https://render.com)
2. Conecte seu repositório GitHub
3. Configure:
   - Build Command: `npm install`
   - Start Command: `npm start`
4. Deploy automático

## Variáveis de Ambiente (opcional)

```env
PORT=3000
SESSION_SECRET=sua-chave-secreta-aqui
STRIPE_SECRET_KEY=sk_live_...
NODE_ENV=production
```

## Estrutura

```
novus-worlds/
├── server/           # Backend Node.js
│   ├── routes/      # API routes
│   ├── websocket/   # WebSocket servers
│   └── middleware/  # Auth middleware
├── public/          # Frontend
│   ├── css/        # Stylesheets
│   ├── js/         # JavaScript
│   └── assets/     # Assets (R6 model, textures)
└── novus.db        # SQLite database (criado automaticamente)
```

## Stack Técnica

- **Backend:** Node.js + Express.js
- **Database:** SQLite (better-sqlite3)
- **3D Engine:** Three.js
- **Multiplayer:** WebSocket (ws)
- **Auth:** Sessions + bcrypt

## Doação (Stripe)

Configure a variável `STRIPE_SECRET_KEY` para ativar doações.

## Funcionalidades

- [x] Sistema de autenticação
- [x] Catálogo de itens
- [x] Editor de avatar
- [x] Cliente de jogo 3D
- [x] Multiplayer em tempo real
- [x] Studio (editor de mapas)
- [x] Painel Admin
- [x] Sistema de economia (Novux)
- [x] Chat global
- [x] Amigos

## Licença

MIT