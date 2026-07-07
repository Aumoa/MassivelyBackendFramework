window.getUserLocale = () => {
    return [navigator.language, Intl.DateTimeFormat().resolvedOptions().timeZone];
};

window.openExternalUrl = (url) => {
    if (typeof url !== 'string' || !url.startsWith('https://')) return;
    const a = document.createElement('a');
    a.href = url;
    a.target = '_blank';
    a.rel = 'noopener noreferrer';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
};

window.oauthProfilePictureEditor = (() => {
    const states = new Map();

    function load(inputId, canvasId) {
        const input = document.getElementById(inputId);
        const canvas = document.getElementById(canvasId);
        const file = input?.files?.[0];
        if (!input || !canvas || !file) {
            return Promise.resolve({ ok: false, error: "empty" });
        }

        if (file.type !== "image/jpeg" && file.type !== "image/png") {
            return Promise.resolve({ ok: false, error: "unsupported" });
        }

        clear(canvasId);

        return new Promise((resolve) => {
            const objectUrl = URL.createObjectURL(file);
            const image = new Image();
            image.onload = () => {
                const state = {
                    canvas,
                    image,
                    objectUrl,
                    zoom: 1,
                    offsetX: 0,
                    offsetY: 0,
                    dragging: false,
                    lastX: 0,
                    lastY: 0
                };

                states.set(canvasId, state);
                attachCanvasEvents(state, canvasId);
                constrain(state);
                draw(state);
                resolve({ ok: true });
            };
            image.onerror = () => {
                URL.revokeObjectURL(objectUrl);
                resolve({ ok: false, error: "unsupported" });
            };
            image.src = objectUrl;
        });
    }

    function attachCanvasEvents(state, canvasId) {
        if (state.canvas.dataset.oauthPictureEditorBound === "true") {
            return;
        }

        state.canvas.dataset.oauthPictureEditorBound = "true";
        state.canvas.addEventListener("pointerdown", (event) => {
            const current = states.get(canvasId);
            if (!current) return;
            current.dragging = true;
            current.lastX = event.clientX;
            current.lastY = event.clientY;
            current.canvas.setPointerCapture(event.pointerId);
        });

        state.canvas.addEventListener("pointermove", (event) => {
            const current = states.get(canvasId);
            if (!current?.dragging) return;
            current.offsetX += event.clientX - current.lastX;
            current.offsetY += event.clientY - current.lastY;
            current.lastX = event.clientX;
            current.lastY = event.clientY;
            constrain(current);
            draw(current);
        });

        state.canvas.addEventListener("pointerup", (event) => {
            const current = states.get(canvasId);
            if (!current) return;
            current.dragging = false;
            current.canvas.releasePointerCapture(event.pointerId);
        });

        state.canvas.addEventListener("pointercancel", () => {
            const current = states.get(canvasId);
            if (current) current.dragging = false;
        });
    }

    function setZoom(canvasId, zoom) {
        const state = states.get(canvasId);
        if (!state) return;

        const nextZoom = Math.min(Math.max(Number(zoom) || 1, 1), 3);
        const currentScale = getScale(state);
        const nextScale = getBaseScale(state) * nextZoom;
        if (currentScale > 0) {
            state.offsetX = state.offsetX * (nextScale / currentScale);
            state.offsetY = state.offsetY * (nextScale / currentScale);
        }

        state.zoom = nextZoom;
        constrain(state);
        draw(state);
    }

    function reset(canvasId) {
        const state = states.get(canvasId);
        if (!state) return;
        state.zoom = 1;
        state.offsetX = 0;
        state.offsetY = 0;
        constrain(state);
        draw(state);
    }

    function exportImage(canvasId, maxBytes, outputSize) {
        const state = states.get(canvasId);
        if (!state) {
            return Promise.resolve({ ok: false, error: "empty" });
        }

        const size = Math.max(1, Number(outputSize) || 512);
        const canvas = document.createElement("canvas");
        canvas.width = size;
        canvas.height = size;
        const context = canvas.getContext("2d");
        const factor = size / state.canvas.width;
        const scale = getScale(state) * factor;
        const width = state.image.naturalWidth * scale;
        const height = state.image.naturalHeight * scale;
        const x = size / 2 - width / 2 + state.offsetX * factor;
        const y = size / 2 - height / 2 + state.offsetY * factor;

        context.fillStyle = "#ffffff";
        context.fillRect(0, 0, size, size);
        context.drawImage(state.image, x, y, width, height);

        return encodeJpeg(canvas, Number(maxBytes) || 524288, 0.92);
    }

    function encodeJpeg(canvas, maxBytes, quality) {
        return new Promise((resolve) => {
            canvas.toBlob((blob) => {
                if (!blob) {
                    resolve({ ok: false, error: "failed" });
                    return;
                }

                if (blob.size > maxBytes && quality > 0.72) {
                    resolve(encodeJpeg(canvas, maxBytes, quality - 0.05));
                    return;
                }

                if (blob.size > maxBytes) {
                    resolve({ ok: false, error: "tooLarge" });
                    return;
                }

                const reader = new FileReader();
                reader.onload = () => {
                    const dataUrl = String(reader.result || "");
                    const comma = dataUrl.indexOf(",");
                    resolve({
                        ok: true,
                        contentType: "image/jpeg",
                        base64: comma >= 0 ? dataUrl.slice(comma + 1) : ""
                    });
                };
                reader.onerror = () => resolve({ ok: false, error: "failed" });
                reader.readAsDataURL(blob);
            }, "image/jpeg", quality);
        });
    }

    function clear(canvasId) {
        const state = states.get(canvasId);
        if (state?.objectUrl) {
            URL.revokeObjectURL(state.objectUrl);
        }

        states.delete(canvasId);
        const canvas = document.getElementById(canvasId);
        const context = canvas?.getContext?.("2d");
        if (canvas && context) {
            context.clearRect(0, 0, canvas.width, canvas.height);
        }
    }

    function getBaseScale(state) {
        return Math.max(
            state.canvas.width / state.image.naturalWidth,
            state.canvas.height / state.image.naturalHeight);
    }

    function getScale(state) {
        return getBaseScale(state) * state.zoom;
    }

    function constrain(state) {
        const width = state.image.naturalWidth * getScale(state);
        const height = state.image.naturalHeight * getScale(state);
        const maxX = Math.max(0, (width - state.canvas.width) / 2);
        const maxY = Math.max(0, (height - state.canvas.height) / 2);
        state.offsetX = Math.min(Math.max(state.offsetX, -maxX), maxX);
        state.offsetY = Math.min(Math.max(state.offsetY, -maxY), maxY);
    }

    function draw(state) {
        const context = state.canvas.getContext("2d");
        const scale = getScale(state);
        const width = state.image.naturalWidth * scale;
        const height = state.image.naturalHeight * scale;
        const x = state.canvas.width / 2 - width / 2 + state.offsetX;
        const y = state.canvas.height / 2 - height / 2 + state.offsetY;

        context.clearRect(0, 0, state.canvas.width, state.canvas.height);
        context.fillStyle = "#f8faf9";
        context.fillRect(0, 0, state.canvas.width, state.canvas.height);
        context.drawImage(state.image, x, y, width, height);
    }

    return {
        load,
        setZoom,
        reset,
        exportImage,
        clear
    };
})();
