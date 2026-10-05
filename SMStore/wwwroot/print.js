// The print buttons on the quote and order sheets. Delegated from the document, because
// enhanced navigation swaps page content without running scripts that arrive with it, and
// an inline onclick would need the Content-Security-Policy to allow inline script.
document.addEventListener('click', event => {
    if (event.target instanceof Element && event.target.closest('[data-print]')) {
        window.print();
    }
});
