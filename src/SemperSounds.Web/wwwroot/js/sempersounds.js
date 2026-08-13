window.semperSounds = {
    /** Plays a preview locally in the browser. Never touches the voice channel. */
    preview: function (audioElement, url) {
        if (!audioElement) return;
        audioElement.src = url;
        audioElement.currentTime = 0;
        audioElement.play();
    },

    /**
     * Binds single-key hotkeys to the visible soundboard tiles.
     * Ignores keystrokes aimed at inputs, so typing in the search box does not
     * fire sounds, and ignores modified keys so browser shortcuts keep working.
     */
    registerHotkeys: function (dotNetRef) {
        if (window.semperSounds._hotkeyHandler) {
            document.removeEventListener('keydown', window.semperSounds._hotkeyHandler);
        }

        window.semperSounds._hotkeyHandler = function (event) {
            if (event.ctrlKey || event.altKey || event.metaKey) return;

            const target = event.target;
            const tag = target && target.tagName ? target.tagName.toLowerCase() : '';
            if (tag === 'input' || tag === 'textarea' || (target && target.isContentEditable)) return;

            if (event.key && event.key.length === 1) {
                dotNetRef.invokeMethodAsync('OnHotkey', event.key.toLowerCase());
            }
        };

        document.addEventListener('keydown', window.semperSounds._hotkeyHandler);
    },

    unregisterHotkeys: function () {
        if (window.semperSounds._hotkeyHandler) {
            document.removeEventListener('keydown', window.semperSounds._hotkeyHandler);
            window.semperSounds._hotkeyHandler = null;
        }
    },

    /**
     * Reads a stored preference, or null when there is none.
     *
     * The try/catch is not decoration: Safari in private mode and some cookie-blocking
     * setups throw on localStorage access rather than returning nothing, and an unhandled
     * exception coming back through interop surfaces as a dead circuit. Returning null
     * makes C# treat it as a first visit, which is the right answer either way.
     */
    getPreference: function (key) {
        try {
            return localStorage.getItem(key);
        } catch {
            return null;
        }
    },

    setPreference: function (key, value) {
        try {
            localStorage.setItem(key, value);
        } catch {
            // Storage being unavailable costs the viewer a saved preference, nothing more.
        }
    }
};
