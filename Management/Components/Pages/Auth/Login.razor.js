export async function loginAsync(url) {
    const response = await fetch(url, {
        method: 'POST',
        credentials: 'include'
    });
    return {
        ok: response.ok,
        status: response.status
    };
};

export async function registerAsync(url) {
    const response = await fetch(url, {
        method: 'POST',
        credentials: 'include'
    });
    return {
        ok: response.ok,
        status: response.status
    };
};