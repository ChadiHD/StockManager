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
