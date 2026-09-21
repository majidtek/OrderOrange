// Saves the store's QR as a file. The image is already a data URL, so this is just an
// anchor click — no server round trip and nothing to clean up afterwards.
window.mfQr = {
    download(dataUrl, filename) {
        if (!dataUrl) return;
        const a = document.createElement('a');
        a.href = dataUrl;
        a.download = filename || 'qr.png';
        // Must be in the document for Firefox to honour the click.
        document.body.appendChild(a);
        a.click();
        a.remove();
    },
};
