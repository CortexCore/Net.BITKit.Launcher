const launcher = document.querySelector('.launcher');
const phaseButtons = [...document.querySelectorAll('[data-state]')];
const screens = [...document.querySelectorAll('[data-screen]')];
const toast = document.querySelector('.toast');
const progressBar = document.querySelector('#progressBar');
const progressValue = document.querySelector('#progressValue');
const downloaded = document.querySelector('#downloaded');
const speed = document.querySelector('#speed');
const eta = document.querySelector('#eta');
const currentFile = document.querySelector('#currentFile');

let state = new URLSearchParams(location.search).get('state') || 'install';
let progressTimer;
let toastTimer;

const designWidth = 1280;
const designHeight = 720;

const files = [
  'Project B_Data/resources.assets',
  'Project B_Data/sharedassets2.assets',
  'Project B_Data/StreamingAssets/aa/StandaloneWindows64/defaultlocalgroup_assets.bundle',
  'UnityPlayer.dll'
];

function setState(next) {
  state = ['install', 'download', 'ready'].includes(next) ? next : 'install';
  launcher.dataset.state = state;
  phaseButtons.forEach(button => button.classList.toggle('is-active', button.dataset.state === state));
  screens.forEach(screen => screen.classList.toggle('is-active', screen.dataset.screen === state));
  clearInterval(progressTimer);
  if (state === 'download') startProgressDemo();
}

function fitCanvas() {
  const scale = Math.min(innerWidth / designWidth, innerHeight / designHeight);
  launcher.style.setProperty('--ui-scale', Math.max(.4, scale).toFixed(4));
}

function showToast(message) {
  toast.textContent = message;
  toast.classList.add('is-visible');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => toast.classList.remove('is-visible'), 2200);
}

function startProgressDemo() {
  let value = 38;
  renderProgress(value);
  progressTimer = setInterval(() => {
    value += Math.random() * 1.8;
    if (value >= 84) value = 38;
    renderProgress(value);
  }, 900);
}

function renderProgress(value) {
  const rounded = Math.floor(value);
  progressBar.style.width = `${value}%`;
  progressValue.textContent = `${rounded}%`;
  downloaded.textContent = `${Math.round(1587 * value / 100)} MB`;
  speed.textContent = `${(16 + Math.random() * 5).toFixed(1)} MB/s`;
  eta.textContent = `00:${String(Math.max(8, Math.round((100 - value) * .85))).padStart(2, '0')}`;
  currentFile.textContent = files[Math.floor(value / 12) % files.length];
}

phaseButtons.forEach(button => button.addEventListener('click', () => setState(button.dataset.state)));

document.addEventListener('click', event => {
  const action = event.target.closest('[data-action]')?.dataset.action;
  if (!action) return;
  if (action === 'install') setState('download');
  if (action === 'ready') setState('ready');
  if (action === 'pause') showToast('演示模式：传输已暂停');
  if (action === 'launch') showToast('演示模式：这里将启动 Project B');
});

document.querySelectorAll('.footer-links button, .path-row .ghost').forEach(button => {
  button.addEventListener('click', () => showToast('视觉演示：该控件尚未接入功能'));
});

const host = window.chrome?.webview;
const windowButtons = document.querySelectorAll('.window-actions button');
windowButtons[0]?.addEventListener('click', () => host?.postMessage(JSON.stringify({ action: 'minimize' })));
windowButtons[1]?.addEventListener('click', () => host?.postMessage(JSON.stringify({ action: 'close' })));
document.querySelector('.window-bar')?.addEventListener('pointerdown', event => {
  if (event.button === 0 && !event.target.closest('button')) {
    host?.postMessage(JSON.stringify({ action: 'drag' }));
  }
});

window.addEventListener('keydown', event => {
  const index = ['install', 'download', 'ready'].indexOf(state);
  if (event.key === 'ArrowRight') setState(['install', 'download', 'ready'][Math.min(2, index + 1)]);
  if (event.key === 'ArrowLeft') setState(['install', 'download', 'ready'][Math.max(0, index - 1)]);
});

window.addEventListener('resize', fitCanvas);

fitCanvas();
setState(state);
