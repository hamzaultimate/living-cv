// The admin menu's convenience layer: collapse to an icon rail and back (remembered in a cookie the server reads), and open as an overlay on narrow
// screens. Admin pages run no inline script (see the admin CSP); this and admin-unsaved.js are the only scripts they may load. Without it the menu
// is simply open on wide screens and sits above the page on narrow ones, and every link and form still works.
(function () {
  var shell = document.querySelector('[data-adm-shell]');
  var toggle = document.querySelector('[data-adm-toggle]');
  if (!shell || !toggle) return;
  shell.classList.add('adm-js');

  var COOKIE = 'adm-menu';
  var railPage = shell.getAttribute('data-rail-page') === 'true';

  // The server only ever compares the cookie with the word "collapsed", so expanding just removes it.
  function remember(collapsed) {
    if (railPage) return; // a wizard page's rail belongs to that page and is never remembered
    var secure = location.protocol === 'https:' ? '; Secure' : '';
    document.cookie = collapsed
      ? COOKIE + '=collapsed; path=/admin; max-age=31536000; SameSite=Lax' + secure
      : COOKIE + '=; path=/admin; max-age=0; SameSite=Lax' + secure;
  }

  toggle.addEventListener('click', function () {
    var collapsed = shell.getAttribute('data-menu') !== 'collapsed';
    shell.setAttribute('data-menu', collapsed ? 'collapsed' : 'expanded');
    toggle.setAttribute('aria-expanded', collapsed ? 'false' : 'true');
    remember(collapsed);
  });

  var opener = document.querySelector('[data-adm-open]');
  var menu = document.querySelector('[data-adm-menu]');
  if (!opener || !menu) return;

  function isOpen() { return shell.getAttribute('data-menu-open') === 'true'; }
  function setOpen(open) {
    shell.setAttribute('data-menu-open', open ? 'true' : 'false');
    opener.setAttribute('aria-expanded', open ? 'true' : 'false');
  }

  opener.addEventListener('click', function () {
    var open = !isOpen();
    setOpen(open);
    if (open) {
      var first = menu.querySelector('a, button');
      if (first) first.focus();
    }
  });
  document.addEventListener('keydown', function (event) {
    if (event.key === 'Escape' && isOpen()) {
      setOpen(false);
      opener.focus();
    }
  });
  menu.addEventListener('click', function (event) {
    if (event.target.closest && event.target.closest('a')) setOpen(false);
  });
})();
