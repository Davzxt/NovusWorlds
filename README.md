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

## Conta Admin

- **Username:** NovusWorlds
- **Password:** admin2008

## Deploy no Render (Gratuito)

1. Cria conta no [Render](https://render.com)
2. Conecta seu repositório GitHub
3. Configure:
   - Build Command: `npm install`
   - Start Command: `npm start`
4. Variáveis de Ambiente (opcional):
   - `STRIPE_SECRET_KEY` = tua chave do Stripe (para doações)
   - `SESSION_SECRET` = qualquer texto aleatório
5. Deploy automático

## Stack Técnica

- **Backend:** Node.js + Express.js
- **Database:** sql.js (SQLite em WebAssembly)
- **3D Engine:** Three.js
- **Multiplayer:** WebSocket (ws)
- **Auth:** Sessions + bcrypt

## Doação (Stripe)

Configure a variável `STRIPE_SECRET_KEY` no Render para ativar doações.

## Funcionalidades

- Sistema de autenticação
- Catálogo de itens
- Editor de avatar
- Cliente de jogo 3D
- Multiplayer em tempo real
- Studio (editor de mapas)
- Painel Admin
- Sistema de economia (Novux)
- Chat global

## Licença

MIT