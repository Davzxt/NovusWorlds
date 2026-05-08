export async function api(path, options = {}) {
  const res = await fetch(path, { headers: { 'Content-Type': 'application/json', ...(options.headers || {}) }, credentials: 'same-origin', ...options });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(data.error || 'Erro no servidor.');
  return data;
}

export function toast(message) {
  const el = document.createElement('div');
  el.className = 'toast';
  el.textContent = message;
  document.body.appendChild(el);
  setTimeout(() => el.remove(), 3000);
}

export async function currentUser() {
  return (await api('/api/auth/me')).user;
}

export async function renderHeader() {
  const host = document.querySelector('[data-header]');
  if (!host) return;
  const user = await currentUser();
  host.innerHTML = `
    <div class="topbar"><div class="wrap nav">
      <a class="logo" href="/">Novus Worlds</a>
      <div class="links"><a href="/">Inicio</a><a href="/games.html">Jogos</a><a href="/catalog.html">Catalogo</a><a href="/forum.html">Forum</a><a href="/about.html">Sobre</a></div>
      <div class="userbox">${user ? `<span class="novux">N$ ${user.novux}</span><a class="btn secondary" href="/profile.html?user=${encodeURIComponent(user.username)}">${user.username}</a><a class="btn" href="/studio-dashboard.html">Studio</a>${user.is_admin ? '<a class="btn secondary" href="/admin/index.html">Admin</a>' : ''}<button id="logoutBtn" class="danger">Sair</button>` : '<a class="btn secondary" href="/login.html">Entrar</a><a class="btn" href="/register.html">Registrar</a>'}</div>
    </div></div>`;
  document.getElementById('logoutBtn')?.addEventListener('click', async () => { await api('/api/auth/logout', { method: 'POST' }); location.href = '/'; });
}

export async function loadCards() {
  const gamesHost = document.querySelector('[data-games]');
  if (gamesHost) {
    const { games } = await api('/api/games?sort=featured');
    gamesHost.innerHTML = games.slice(0, 8).map(gameCard).join('');
  }
  const itemsHost = document.querySelector('[data-items]');
  if (itemsHost) {
    const { items } = await api('/api/catalog?sort=popular');
    itemsHost.innerHTML = items.slice(0, 8).map(itemCard).join('');
  }
}

export function gameCard(g) {
  return `<div class="card"><div class="thumb"><img src="${g.thumbnail_url || '/assets/textures/game-default.svg'}" alt=""></div><h3>${esc(g.title)}</h3><p class="muted">Criador: ${esc(g.creator || 'NovusWorlds')}</p><p>${g.visit_count || 0} visitas</p><a class="btn secondary" href="/game-info.html?id=${g.id}">Detalhes</a> <a class="btn" href="/game.html?id=${g.id}">Jogar</a></div>`;
}

export function itemCard(i) {
  return `<div class="card"><div class="thumb"><img src="${i.thumbnail_url || i.asset_url || '/assets/textures/item-default.svg'}" alt=""></div><h3>${esc(i.name)}</h3><p class="muted">${esc(i.type)}</p><p class="novux">N$ ${i.price}</p><a class="btn secondary" href="/item.html?id=${i.id}">Ver Item</a></div>`;
}

export function esc(v) {
  return String(v ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

renderHeader().then(async () => {
  loadCards().catch(() => {});
});
