// Warns before leaving an edit form that has unsaved changes. Admin pages run no inline script (see the admin CSP); this is the only script
// they may load. The form works without it: this only adds the prompt.
(function () {
  var form = document.querySelector('form[data-unsaved-warning]');
  if (!form) return;
  // A form shown again after a refused save holds input that was never saved, so it starts out dirty.
  var dirty = form.getAttribute('data-has-errors') === 'true';
  var mark = function () { dirty = true; };
  form.addEventListener('input', mark);
  form.addEventListener('change', mark);
  form.addEventListener('submit', function () { dirty = false; });
  window.addEventListener('beforeunload', function (event) {
    if (!dirty) return;
    event.preventDefault();
    event.returnValue = '';
  });
})();
