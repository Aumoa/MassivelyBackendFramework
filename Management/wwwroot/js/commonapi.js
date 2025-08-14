window.compareNavigateTo = function (url, expected) {
    if (document.referrer && document.referrer.endsWith(expected)) {
        history.back();
    }
    else {
        location.replace(url);
    }
};