function getFragmentParams() {
    const hash = window.location.hash.substring(1); // '#' 제거
    const params = new URLSearchParams(hash);
    const clientId = params.get('client_id');
    const secret = params.get('secret');
    return { clientId, secret };
}