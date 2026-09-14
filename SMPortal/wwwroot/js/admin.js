// Small JS interop helper for CSV export. Reference from wwwroot/index.html:
//   <script src="js/admin.js"></script>
window.smportal = window.smportal || {};
window.smportal.downloadCsv = function (name, text) {
    const blob = new Blob([text], { type: "text/csv" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = name;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1500);
};

// Customer documents cannot be a plain <a href> to the API: the portal is WebAssembly and
// authenticates with a bearer token, which a browser navigation does not carry. So the
// bytes are fetched through the authorised HttpClient, handed over as base64, and saved
// from a blob here.
//
// Saved rather than opened in a tab. These are files a stranger uploaded, and the admin
// origin holds the session that can approve accounts — a PDF viewer is not somewhere to
// find out that one of them was interesting.
window.smportal.saveFile = function (name, contentType, base64) {
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
        bytes[i] = binary.charCodeAt(i);
    }
    const url = URL.createObjectURL(new Blob([bytes], { type: contentType }));
    const a = document.createElement("a");
    a.href = url;
    a.download = name;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1500);
};
