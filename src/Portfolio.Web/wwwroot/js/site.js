(function () {
  const btn = document.getElementById('menu-btn');
  const nav = document.getElementById('mobile-nav');
  if (btn && nav) {
    const closeMenu = function () {
      nav.classList.add('hidden');
      btn.setAttribute('aria-expanded', 'false');
    };
    btn.addEventListener('click', function () {
      const open = nav.classList.toggle('hidden') === false;
      btn.setAttribute('aria-expanded', String(open));
    });
    nav.querySelectorAll('a').forEach(function (link) {
      link.addEventListener('click', closeMenu);
    });
  }

  const themeBtn = document.getElementById('theme-btn');
  const setTheme = function (theme) {
    document.documentElement.setAttribute('data-theme', theme);
    document.documentElement.style.colorScheme = theme;
    if (themeBtn) {
      themeBtn.setAttribute('aria-label', theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme');
      themeBtn.setAttribute('aria-pressed', String(theme === 'dark'));
    }
    try { localStorage.setItem('theme', theme); } catch (e) { /* ignore */ }
  };
  if (themeBtn) {
    themeBtn.addEventListener('click', function () {
      const current = document.documentElement.getAttribute('data-theme');
      const next = current === 'light' ? 'dark' : 'light';
      setTheme(next);
    });
  }

  const initialTheme = document.documentElement.getAttribute('data-theme') || 'dark';
  setTheme(initialTheme);

  document.addEventListener('click', function (e) {
    const el = e.target.closest('[data-share]');
    if (!el) return;
    e.preventDefault();
    const rel = el.getAttribute('data-url');
    const url = rel ? new URL(rel, location.origin).href : location.href;
    const title = el.getAttribute('data-title') || document.title;
    const type = el.getAttribute('data-share');
    if (type === 'x') {
      window.open('https://twitter.com/intent/tweet?text=' + encodeURIComponent(title) + '&url=' + encodeURIComponent(url), '_blank', 'noopener');
      return;
    }
    if (type === 'linkedin') {
      window.open('https://www.linkedin.com/sharing/share-offsite/?url=' + encodeURIComponent(url), '_blank', 'noopener');
      return;
    }
    if (navigator.share) {
      navigator.share({ title: title, url: url }).catch(function () {});
      return;
    }
    const label = el.querySelector('[data-share-label]') || el;
    const flash = function () {
      const orig = label.getAttribute('data-orig') || label.textContent;
      label.setAttribute('data-orig', orig);
      label.textContent = 'Copied!';
      setTimeout(function () { label.textContent = orig; }, 1500);
    };
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(url).then(flash).catch(function () {});
    } else {
      const ta = document.createElement('textarea');
      ta.value = url; ta.setAttribute('readonly', ''); ta.style.position = 'absolute'; ta.style.left = '-9999px';
      document.body.appendChild(ta); ta.select();
      try { document.execCommand('copy'); flash(); } catch (_) { /* ignore */ }
      document.body.removeChild(ta);
    }
  });

  // Project filter chips toggle which cards are visible.
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

  // Certification issuer chips toggle which groups are visible.
  const certFilterBar = document.querySelector('[data-cert-filter]');
  const certGroups = document.querySelector('[data-cert-groups]');
  if (certFilterBar && certGroups) {
    const chips = Array.prototype.slice.call(certFilterBar.querySelectorAll('[data-filter]'));
    const sections = Array.prototype.slice.call(certGroups.querySelectorAll('[data-issuer]'));
    certFilterBar.addEventListener('click', function (e) {
      const chip = e.target.closest('[data-filter]');
      if (!chip) return;
      const filter = chip.getAttribute('data-filter');
      chips.forEach(function (c) {
        const active = c === chip;
        c.classList.toggle('is-active', active);
        c.setAttribute('aria-pressed', String(active));
      });
      sections.forEach(function (sec) {
        const iss = (sec.getAttribute('data-issuer') || '').toLowerCase();
        const show = filter === 'all' || iss === filter.toLowerCase();
        sec.classList.toggle('hidden', !show);
      });
    });
  }

  // Lightbox behavior for project galleries with a progressive-enhancement fallback.
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
