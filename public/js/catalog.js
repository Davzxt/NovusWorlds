import { api, itemCard, toast, esc } from './main.js';

async function catalogPage() {
  const host = document.querySelector('[data-catalog-grid]');
  if (!host) return;
  const form = document.getElementById('filters');
  async function load() {
    const params = new URLSearchParams(new FormData(form));
    const { items } = await api(`/api/catalog?${params}`);
    host.innerHTML = items.map(itemCard).join('');
  }
  form.addEventListener('input', load);
  await load();
}

async function itemPage() {
  const host = document.querySelector('[data-item]');
  if (!host) return;
  const id = new URLSearchParams(location.search).get('id');
  const { item, owners } = await api(`/api/catalog/${id}`);
  host.innerHTML = `<div class="panel"><h1>${esc(item.name)}</h1><div class="grid"><div><div id="itemPreview" style="height:360px;background:#ddd"></div></div><div><p>${esc(item.description || '')}</p><p>Tipo: ${esc(item.type)}</p><p>Criador: ${esc(item.creator)}</p><p class="novux">Preco: Ƀ ${item.price}</p><button id="buy">Comprar por Ƀ ${item.price}</button><button class="secondary" id="try">Experimentar</button></div></div><h3>Tambem possuido por</h3><p>${owners.map((o) => esc(o.username)).join(', ') || 'Ninguem ainda'}</p><h3>Comentarios</h3><textarea placeholder="Escreva um comentario"></textarea></div>`;
  document.getElementById('buy').onclick = async () => { try { await api(`/api/catalog/${id}/buy`, { method: 'POST' }); toast('Item comprado.'); } catch (e) { toast(e.message); } };
  import('./r6-viewer.js').then((m) => m.createR6Viewer(document.getElementById('itemPreview'), { item }));
}

catalogPage();
itemPage();
