const API_BASE = '/api';

let currentUser = null;

async function apiCall(endpoint, options = {}) {
  const defaultOptions = {
    headers: {
      'Content-Type': 'application/json'
    }
  };
  
  const mergedOptions = { ...defaultOptions, ...options };
  
  if (currentUser) {
    mergedOptions.headers['Authorization'] = `Bearer ${currentUser.id}`;
  }
  
  try {
    const response = await fetch(`${API_BASE}${endpoint}`, mergedOptions);
    const data = await response.json();
    
    if (!response.ok) {
      throw new Error(data.error || 'Request failed');
    }
    
    return data;
  } catch (error) {
    console.error('API Error:', error);
    throw error;
  }
}

async function checkSession() {
  try {
    const data = await apiCall('/auth/session');
    if (data.authenticated) {
      currentUser = data.user;
      updateUIForLoggedIn();
    }
    return data.authenticated;
  } catch (error) {
    return false;
  }
}

async function login(username, password) {
  const data = await apiCall('/auth/login', {
    method: 'POST',
    body: JSON.stringify({ username, password })
  });
  
  if (data.success) {
    currentUser = data.user;
    updateUIForLoggedIn();
    if (data.dailyBonus > 0) {
      showToast(`+${data.dailyBonus} Novux (daily bonus!)`, 'success');
    }
  }
  
  return data;
}

async function register(username, password, confirmPassword, email) {
  const data = await apiCall('/auth/register', {
    method: 'POST',
    body: JSON.stringify({ username, password, confirmPassword, email })
  });
  
  if (data.success) {
    currentUser = data.user;
    updateUIForLoggedIn();
  }
  
  return data;
}

async function logout() {
  await apiCall('/auth/logout', { method: 'POST' });
  currentUser = null;
  updateUIForLoggedOut();
  window.location.href = '/';
}

function updateUIForLoggedIn() {
  const loggedInElements = document.querySelectorAll('.logged-in');
  const loggedOutElements = document.querySelectorAll('.logged-out');
  
  loggedInElements.forEach(el => el.classList.remove('hidden'));
  loggedOutElements.forEach(el => el.classList.add('hidden'));
  
  const novuxEl = document.getElementById('novux-balance');
  if (novuxEl && currentUser) {
    novuxEl.textContent = `${currentUser.novux} Ƀ`;
  }
  
  const usernameEl = document.getElementById('header-username');
  if (usernameEl && currentUser) {
    usernameEl.textContent = currentUser.username;
  }
}

function updateUIForLoggedOut() {
  const loggedInElements = document.querySelectorAll('.logged-in');
  const loggedOutElements = document.querySelectorAll('.logged-out');
  
  loggedInElements.forEach(el => el.classList.add('hidden'));
  loggedOutElements.forEach(el => el.classList.remove('hidden'));
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
    if (container.children.length === 0) {
      container.remove();
    }
  }, 3000);
}

function showLoading(elementId) {
  const el = document.getElementById(elementId);
  if (el) {
    el.innerHTML = '<div class="loading"><div class="spinner"></div></div>';
  }
}

function hideLoading(elementId) {
  const el = document.getElementById(elementId);
  if (el) {
    el.innerHTML = '';
  }
}

function formatDate(dateStr) {
  const date = new Date(dateStr);
  return date.toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
}

function formatNumber(num) {
  if (num >= 1000000) {
    return (num / 1000000).toFixed(1) + 'M';
  } else if (num >= 1000) {
    return (num / 1000).toFixed(1) + 'K';
  }
  return num.toString();
}

function getQueryParam(name) {
  const params = new URLSearchParams(window.location.search);
  return params.get(name);
}

function setQueryParam(name, value) {
  const params = new URLSearchParams(window.location.search);
  params.set(name, value);
  window.history.pushState({}, '', `${window.location.pathname}?${params}`);
}

document.addEventListener('DOMContentLoaded', () => {
  checkSession();
  
  const logoutBtn = document.getElementById('logout-btn');
  if (logoutBtn) {
    logoutBtn.addEventListener('click', logout);
  }
  
  const dropdown = document.querySelector('.dropdown');
  if (dropdown) {
    const toggle = dropdown.querySelector('.dropdown-toggle');
    const menu = dropdown.querySelector('.dropdown-menu');
    
    if (toggle && menu) {
      toggle.addEventListener('click', (e) => {
        e.stopPropagation();
        menu.classList.toggle('hidden');
      });
      
      document.addEventListener('click', () => {
        menu.classList.add('hidden');
      });
    }
  }
});

if (typeof module !== 'undefined' && module.exports) {
  module.exports = { apiCall, checkSession, login, register, logout, showToast };
}