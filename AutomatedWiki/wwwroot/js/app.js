window.openExternalUrl = (url) => {
    if (typeof url !== 'string' || !url.startsWith('https://')) {
        return;
    }

    const a = document.createElement('a');
    a.href = url;
    a.target = '_blank';
    a.rel = 'noopener noreferrer';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
};
