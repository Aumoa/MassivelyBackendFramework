export async function loginAsync(url) {
    const response = await fetch(url, {
        method: 'POST',
        credentials: 'include'
    });

    const json = await response.json();
    return {
        ok: response.ok,
        status: response.status,
        body: json
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