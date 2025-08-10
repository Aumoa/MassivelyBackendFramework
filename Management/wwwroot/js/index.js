window.authInterop = {
    login: async function (url) {
        const response = await fetch(url, {
            method: 'GET',
            credentials: 'include'
        });
        return {
            ok: response.ok,
            status: response.status
        };
    },
    logout: async function (url) {
        await fetch(url, {
            method: 'POST',
            credentials: 'include'
        });
    }
};