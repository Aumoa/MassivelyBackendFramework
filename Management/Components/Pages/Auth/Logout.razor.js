export async function logoutAsync(url) {
    await fetch(url, {
        method: 'POST',
        credentials: 'include'
    });
};