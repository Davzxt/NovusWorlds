let currentUser = null;

async function handleLogin(e) {
  e.preventDefault();
  
  const username = document.getElementById('username').value.trim();
  const password = document.getElementById('password').value;
  const errorEl = document.getElementById('login-error');
  
  if (!username || !password) {
    errorEl.textContent = 'Please fill in all fields';
    return;
  }
  
  try {
    const response = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password })
    });
    
    const data = await response.json();
    
    if (!response.ok) {
      errorEl.textContent = data.error || 'Login failed';
      return;
    }
    
    currentUser = data.user;
    
    if (data.dailyBonus > 0) {
      showToast(`+${data.dailyBonus} Novux (daily bonus!)`, 'success');
    }
    
    window.location.href = '/';
  } catch (error) {
    errorEl.textContent = 'Connection error. Please try again.';
  }
}

async function handleRegister(e) {
  e.preventDefault();
  
  const username = document.getElementById('username').value.trim();
  const password = document.getElementById('password').value;
  const confirmPassword = document.getElementById('confirm-password').value;
  const email = document.getElementById('email').value.trim();
  const errorEl = document.getElementById('register-error');
  
  if (!username || !password || !confirmPassword) {
    errorEl.textContent = 'Please fill in all required fields';
    return;
  }
  
  if (username.length < 3 || username.length > 20) {
    errorEl.textContent = 'Username must be 3-20 characters';
    return;
  }
  
  if (/\s/.test(username)) {
    errorEl.textContent = 'Username cannot contain spaces';
    return;
  }
  
  if (password.length < 8) {
    errorEl.textContent = 'Password must be at least 8 characters';
    return;
  }
  
  if (password !== confirmPassword) {
    errorEl.textContent = 'Passwords do not match';
    return;
  }
  
  try {
    const response = await fetch('/api/auth/register', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password, confirmPassword, email })
    });
    
    const data = await response.json();
    
    if (!response.ok) {
      errorEl.textContent = data.error || 'Registration failed';
      return;
    }
    
    currentUser = data.user;
    showToast('Welcome to Novus Worlds!', 'success');
    window.location.href = '/';
  } catch (error) {
    errorEl.textContent = 'Connection error. Please try again.';
  }
}

async function checkSession() {
  try {
    const response = await fetch('/api/auth/session');
    const data = await response.json();
    
    if (data.authenticated) {
      currentUser = data.user;
      updateHeader();
      return true;
    }
    return false;
  } catch (error) {
    return false;
  }
}

function updateHeader() {
  const headerRight = document.getElementById('header-right');
  if (!headerRight || !currentUser) return;
  
  headerRight.innerHTML = `
    <span class="novux-balance">${currentUser.novux} Ƀ</span>
    <div class="dropdown">
      <button class="btn btn-secondary dropdown-toggle">${currentUser.username}</button>
      <div class="dropdown-menu hidden">
        <a href="/profile.html?user=${currentUser.username}" class="dropdown-item">My Profile</a>
        <a href="/avatar.html" class="dropdown-item">Avatar Editor</a>
        <a href="/studio.html" class="dropdown-item">Studio</a>
        ${currentUser.isAdmin ? '<a href="/admin/" class="dropdown-item">Admin Panel</a>' : ''}
        <button id="logout-btn" class="dropdown-item" style="width:100%;border:none;background:none;text-align:left;">Log Out</button>
      </div>
    </div>
  `;
  
  document.getElementById('logout-btn').addEventListener('click', async () => {
    await fetch('/api/auth/logout', { method: 'POST' });
    window.location.href = '/';
  });
  
  const dropdown = headerRight.querySelector('.dropdown');
  const toggle = dropdown.querySelector('.dropdown-toggle');
  const menu = dropdown.querySelector('.dropdown-menu');
  
  toggle.addEventListener('click', (e) => {
    e.stopPropagation();
    menu.classList.toggle('hidden');
  });
  
  document.addEventListener('click', () => {
    menu.classList.add('hidden');
  });
}

function showToast(message, type = 'info') {
  let container = document.querySelector('.toast-container');
  if (!container) {
    container = document.createElement('div');
    container.className = 'toast-container';
    document.body.appendChild(container);
  }
  
  const toast = document.createElement('div');
  toast.className = `toast toast-${type}`;
  toast.textContent = message;
  container.appendChild(toast);
  
  setTimeout(() => {
    toast.remove();
  }, 3000);
}

document.addEventListener('DOMContentLoaded', () => {
  checkSession();
  
  const loginForm = document.getElementById('login-form');
  if (loginForm) {
    loginForm.addEventListener('submit', handleLogin);
  }
  
  const registerForm = document.getElementById('register-form');
  if (registerForm) {
    registerForm.addEventListener('submit', handleRegister);
  }
});

if (typeof module !== 'undefined' && module.exports) {
  module.exports = { handleLogin, handleRegister, checkSession };
}