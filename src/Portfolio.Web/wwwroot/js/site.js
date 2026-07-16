// Minimal, dependency-free interactions.
(function () {
  // Mobile menu
  const btn = document.getElementById('menu-btn');
  const nav = document.getElementById('mobile-nav');
  if (btn && nav) {
    btn.addEventListener('click', function () {
      const open = nav.classList.toggle('hidden') === false;
      btn.setAttribute('aria-expanded', String(open));
    });
  }

  // Light/dark theme toggle (initial theme is set inline in <head> to avoid a flash)
  const themeBtn = document.getElementById('theme-btn');
  if (themeBtn) {
    themeBtn.addEventListener('click', function () {
      const current = document.documentElement.getAttribute('data-theme');
      const next = current === 'light' ? 'dark' : 'light';
      document.documentElement.setAttribute('data-theme', next);
      try { localStorage.setItem('theme', next); } catch (e) { /* ignore */ }
    });
  }

  // Work-page domain filter. Chips toggle which project cards are visible; purely client-side.
  const filterBar = document.querySelector('[data-project-filter]');
  const grid = document.querySelector('[data-project-grid]');
  if (filterBar && grid) {
    const chips = Array.prototype.slice.call(filterBar.querySelectorAll('[data-filter]'));
    const cards = Array.prototype.slice.call(grid.querySelectorAll('[data-category]'));
    filterBar.addEventListener('click', function (e) {
      const chip = e.target.closest('[data-filter]');
      if (!chip) return;
      const filter = chip.getAttribute('data-filter');
      chips.forEach(function (c) {
        const active = c === chip;
        c.classList.toggle('is-active', active);
        c.setAttribute('aria-pressed', String(active));
      });
      cards.forEach(function (card) {
        const cat = (card.getAttribute('data-category') || '').toLowerCase();
        const show = filter === 'all' || cat === filter.toLowerCase();
        card.classList.toggle('hidden', !show);
      });
    });
  }

  // Media lightbox for project galleries. Each [data-lightbox-gallery] is its own set,
  // so cards on the work grid page through their own media (not each other's).
  // Progressive enhancement: without JS the thumbnails/images/videos still render inline.
  const galleries = Array.prototype.slice.call(document.querySelectorAll('[data-lightbox-gallery]'))
    .map(function (g) { return Array.prototype.slice.call(g.querySelectorAll('[data-lightbox-item]')); })
    .filter(function (list) { return list.length > 0; });

  if (galleries.length) {
    let activeItems = [];
    let current = 0;
    let lastFocused = null;

    const overlay = document.createElement('div');
    overlay.className = 'lightbox hidden';
    overlay.setAttribute('role', 'dialog');
    overlay.setAttribute('aria-modal', 'true');
    overlay.setAttribute('aria-label', 'Media viewer');
    overlay.innerHTML =
      '<button class="lightbox-close" aria-label="Close">✕</button>' +
      '<button class="lightbox-nav lightbox-prev" aria-label="Previous">‹</button>' +
      '<figure class="lightbox-figure">' +
      '  <img class="lightbox-img" alt="" />' +
      '  <video class="lightbox-video" controls playsinline></video>' +
      '  <figcaption class="lightbox-caption"></figcaption>' +
      '</figure>' +
      '<button class="lightbox-nav lightbox-next" aria-label="Next">›</button>';
    document.body.appendChild(overlay);

    const imgEl = overlay.querySelector('.lightbox-img');
    const videoEl = overlay.querySelector('.lightbox-video');
    const capEl = overlay.querySelector('.lightbox-caption');
    const btnPrev = overlay.querySelector('.lightbox-prev');
    const btnNext = overlay.querySelector('.lightbox-next');
    const btnClose = overlay.querySelector('.lightbox-close');

    function render() {
      const el = activeItems[current];
      const src = el.getAttribute('data-src');
      const cap = el.getAttribute('data-caption') || '';
      const isVideo = el.getAttribute('data-video') === 'true';
      videoEl.pause();
      if (isVideo) {
        imgEl.style.display = 'none';
        videoEl.style.display = '';
        videoEl.src = src;
      } else {
        videoEl.removeAttribute('src'); videoEl.load();
        videoEl.style.display = 'none';
        imgEl.style.display = '';
        imgEl.src = src;
        imgEl.alt = cap || 'Screenshot';
      }
      capEl.textContent = cap;
      capEl.style.display = cap ? '' : 'none';
      const multiple = activeItems.length > 1;
      btnPrev.style.display = btnNext.style.display = multiple ? '' : 'none';
    }
    function open(list, i) {
      activeItems = list;
      current = i;
      lastFocused = document.activeElement;
      render();
      overlay.classList.remove('hidden');
      document.body.style.overflow = 'hidden';
      btnClose.focus();
    }
    function close() {
      videoEl.pause();
      overlay.classList.add('hidden');
      document.body.style.overflow = '';
      if (lastFocused && lastFocused.focus) lastFocused.focus();
    }
    function go(delta) { current = (current + delta + activeItems.length) % activeItems.length; render(); }

    galleries.forEach(function (list) {
      list.forEach(function (el, i) {
        el.addEventListener('click', function () { open(list, i); });
      });
    });
    btnClose.addEventListener('click', close);
    btnPrev.addEventListener('click', function () { go(-1); });
    btnNext.addEventListener('click', function () { go(1); });
    overlay.addEventListener('click', function (e) { if (e.target === overlay) close(); });
    document.addEventListener('keydown', function (e) {
      if (overlay.classList.contains('hidden')) return;
      if (e.key === 'Escape') close();
      else if (activeItems.length > 1 && e.key === 'ArrowLeft') go(-1);
      else if (activeItems.length > 1 && e.key === 'ArrowRight') go(1);
    });
  }
})();
