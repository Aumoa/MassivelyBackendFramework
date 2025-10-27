window.getUserLocale = () => {
    return [navigator.language, Intl.DateTimeFormat().resolvedOptions().timeZone];
};