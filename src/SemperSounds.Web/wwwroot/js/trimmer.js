// Waveform trimmer. Decodes the chosen file in the browser, draws its peaks, and lets
// two handles be dragged to pick the window to keep. Nothing is uploaded until the form
// is submitted, so scrubbing costs no bandwidth and no server work.

window.semperTrimmer = (() => {
    const state = {
        buffer: null,
        peaks: null,
        canvas: null,
        dotNet: null,
        start: 0,
        end: 0,
        dragging: null,      // 'start' | 'end' | null
        source: null,        // currently previewing AudioBufferSourceNode
        audioContext: null,
        maxLength: 5,
    };

    const HANDLE_HIT_PX = 14;

    function audioContext() {
        state.audioContext ??= new (window.AudioContext || window.webkitAudioContext)();
        return state.audioContext;
    }

    /** Reduces the samples to one min/max pair per pixel column. */
    function computePeaks(buffer, columns) {
        const channel = buffer.getChannelData(0);
        const blockSize = Math.max(1, Math.floor(channel.length / columns));
        const peaks = new Float32Array(columns * 2);

        for (let column = 0; column < columns; column++) {
            const offset = column * blockSize;
            let min = 0, max = 0;
            for (let i = 0; i < blockSize && offset + i < channel.length; i++) {
                const value = channel[offset + i];
                if (value < min) min = value;
                if (value > max) max = value;
            }
            peaks[column * 2] = min;
            peaks[column * 2 + 1] = max;
        }
        return peaks;
    }

    function cssVar(name, fallback) {
        const value = getComputedStyle(document.documentElement).getPropertyValue(name);
        return value && value.trim() ? value.trim() : fallback;
    }

    function draw() {
        const canvas = state.canvas;
        if (!canvas || !state.peaks) return;

        // Redraw at device resolution so the waveform is not blurry on high-DPI screens.
        const ratio = window.devicePixelRatio || 1;
        const width = canvas.clientWidth;
        const height = canvas.clientHeight;
        if (canvas.width !== width * ratio || canvas.height !== height * ratio) {
            canvas.width = width * ratio;
            canvas.height = height * ratio;
            state.peaks = computePeaks(state.buffer, width);
        }

        const ctx = canvas.getContext('2d');
        ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
        ctx.clearRect(0, 0, width, height);

        const duration = state.buffer.duration;
        const startX = (state.start / duration) * width;
        const endX = (state.end / duration) * width;
        const middle = height / 2;

        // Unselected regions are dimmed rather than hidden, so you keep the context of
        // where the selection sits within the whole clip.
        ctx.fillStyle = 'rgba(255,255,255,0.04)';
        ctx.fillRect(0, 0, width, height);
        ctx.fillStyle = 'rgba(88,101,242,0.18)';
        ctx.fillRect(startX, 0, endX - startX, height);

        for (let column = 0; column < width; column++) {
            const min = state.peaks[column * 2];
            const max = state.peaks[column * 2 + 1];
            const inside = column >= startX && column <= endX;
            ctx.fillStyle = inside ? cssVar('--mud-palette-primary', '#5865F2') : 'rgba(255,255,255,0.22)';
            const top = middle - max * middle;
            const bottom = middle - min * middle;
            ctx.fillRect(column, top, 1, Math.max(1, bottom - top));
        }

        for (const x of [startX, endX]) {
            ctx.fillStyle = '#F0425A';
            ctx.fillRect(x - 1.5, 0, 3, height);
            ctx.beginPath();
            ctx.arc(x, height - 6, 6, 0, Math.PI * 2);
            ctx.fill();
        }
    }

    function timeFromEvent(event) {
        const rect = state.canvas.getBoundingClientRect();
        const ratio = Math.min(1, Math.max(0, (event.clientX - rect.left) / rect.width));
        return ratio * state.buffer.duration;
    }

    function notify() {
        state.dotNet?.invokeMethodAsync('OnSelectionChanged', state.start, state.end);
    }

    function clampSelection(which, time) {
        const duration = state.buffer.duration;
        const minLength = 0.1;

        if (which === 'start') {
            state.start = Math.min(Math.max(0, time), state.end - minLength);
            // Dragging start past the max length pulls the end along, so the selection
            // can be slid without first shrinking it.
            if (state.end - state.start > state.maxLength) {
                state.end = Math.min(duration, state.start + state.maxLength);
            }
        } else {
            state.end = Math.max(Math.min(duration, time), state.start + minLength);
            if (state.end - state.start > state.maxLength) {
                state.start = Math.max(0, state.end - state.maxLength);
            }
        }
    }

    function onPointerDown(event) {
        if (!state.buffer) return;
        const rect = state.canvas.getBoundingClientRect();
        const duration = state.buffer.duration;
        const startX = (state.start / duration) * rect.width;
        const endX = (state.end / duration) * rect.width;
        const x = event.clientX - rect.left;

        state.dragging = Math.abs(x - startX) <= Math.abs(x - endX)
            ? (Math.abs(x - startX) < HANDLE_HIT_PX * 3 ? 'start' : null)
            : (Math.abs(x - endX) < HANDLE_HIT_PX * 3 ? 'end' : null);

        // A click away from either handle moves the nearer one there, so the selection
        // can be positioned without precise dragging.
        if (!state.dragging) {
            state.dragging = Math.abs(x - startX) <= Math.abs(x - endX) ? 'start' : 'end';
        }

        state.canvas.setPointerCapture(event.pointerId);
        clampSelection(state.dragging, timeFromEvent(event));
        draw();
        notify();
    }

    function onPointerMove(event) {
        if (!state.dragging) return;
        clampSelection(state.dragging, timeFromEvent(event));
        draw();
        notify();
    }

    function onPointerUp(event) {
        if (!state.dragging) return;
        state.dragging = null;
        try { state.canvas.releasePointerCapture(event.pointerId); } catch { /* already released */ }
        notify();
    }

    return {
        /** Decodes the file currently chosen in `fileInputSelector` and draws it. */
        async load(canvas, fileInputSelector, dotNetRef, maxLength) {
            const input = document.querySelector(fileInputSelector);
            const file = input?.files?.[0];
            if (!file) return { ok: false, error: 'No file selected.' };

            state.canvas = canvas;
            state.dotNet = dotNetRef;
            state.maxLength = maxLength;

            try {
                const bytes = await file.arrayBuffer();
                state.buffer = await audioContext().decodeAudioData(bytes);
            } catch {
                // Not fatal: ffmpeg on the server reads far more than the browser can,
                // so the upload may still be perfectly valid without a waveform.
                return { ok: false, error: 'This browser cannot decode that file for preview.' };
            }

            state.start = 0;
            state.end = Math.min(state.buffer.duration, maxLength);
            state.peaks = computePeaks(state.buffer, canvas.clientWidth || 600);

            canvas.addEventListener('pointerdown', onPointerDown);
            canvas.addEventListener('pointermove', onPointerMove);
            canvas.addEventListener('pointerup', onPointerUp);
            canvas.addEventListener('pointercancel', onPointerUp);
            window.addEventListener('resize', draw);

            draw();
            return { ok: true, duration: state.buffer.duration, start: state.start, end: state.end };
        },

        setSelection(start, end) {
            if (!state.buffer) return;
            state.start = start;
            state.end = end;
            draw();
        },

        /** Plays only the selected window. */
        preview() {
            if (!state.buffer) return;
            this.stop();
            const source = audioContext().createBufferSource();
            source.buffer = state.buffer;
            source.connect(audioContext().destination);
            source.start(0, state.start, Math.max(0.05, state.end - state.start));
            state.source = source;
        },

        stop() {
            if (state.source) {
                try { state.source.stop(); } catch { /* already stopped */ }
                state.source = null;
            }
        },

        dispose() {
            this.stop();
            if (state.canvas) {
                state.canvas.removeEventListener('pointerdown', onPointerDown);
                state.canvas.removeEventListener('pointermove', onPointerMove);
                state.canvas.removeEventListener('pointerup', onPointerUp);
                state.canvas.removeEventListener('pointercancel', onPointerUp);
            }
            window.removeEventListener('resize', draw);
            state.buffer = null;
            state.peaks = null;
            state.canvas = null;
            state.dotNet = null;
        },
    };
})();
