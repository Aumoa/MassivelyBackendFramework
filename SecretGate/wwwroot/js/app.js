window.secretGate = {
    copyText: async function (value) {
        if (!value) {
            return;
        }

        await navigator.clipboard.writeText(value);
    }
};
