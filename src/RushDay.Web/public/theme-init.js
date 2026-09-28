// Applies a saved theme before first paint so there is no flash. CSP is script-src 'self' with no
// inline scripts (03-security.md section 4), so this bootstrap lives in its own file instead of a
// <script> block in index.html. Mirrors src/lib/theme.ts.
;(function () {
  try {
    var t = localStorage.getItem('rushday.theme')
    if (t === 'light' || t === 'dark') document.documentElement.setAttribute('data-theme', t)
  } catch (e) {}
})()
