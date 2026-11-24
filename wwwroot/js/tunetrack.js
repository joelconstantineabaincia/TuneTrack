// TuneTrack Client Interop

window.tuneTrack = {
    audioElement: null,
    currentUrl: null,

    getClientId: function () {
        try {
            var key = "tunetrack_client_id";
            var existing = localStorage.getItem(key);
            if (existing && existing.length > 10) {
                return existing;
            }
            var newId = (typeof crypto !== "undefined" && crypto.randomUUID) 
                ? crypto.randomUUID() 
                : "tt-" + Math.random().toString(36).substring(2, 15) + "-" + Date.now();
            localStorage.setItem(key, newId);
            return newId;
        } catch (e) {
            return "anon-session-" + Date.now();
        }
    },

    copyToClipboard: async function (text) {
        try {
            if (navigator.clipboard && navigator.clipboard.writeText) {
                await navigator.clipboard.writeText(text);
                return true;
            }
            var textarea = document.createElement("textarea");
            textarea.value = text;
            textarea.style.position = "fixed";
            textarea.style.opacity = "0";
            document.body.appendChild(textarea);
            textarea.select();
            var success = document.execCommand("copy");
            document.body.removeChild(textarea);
            return success;
        } catch (err) {
            console.warn("Clipboard copy failed:", err);
            return false;
        }
    },

    togglePreview: function (url, dotNetHelper) {
        if (!url) return;

        if (this.currentUrl === url && this.audioElement && !this.audioElement.paused) {
            this.audioElement.pause();
            this.currentUrl = null;
            if (dotNetHelper) dotNetHelper.invokeMethodAsync("OnAudioStopped");
            return;
        }

        if (this.audioElement) {
            this.audioElement.pause();
        }

        this.audioElement = new Audio(url);
        this.currentUrl = url;
        this.audioElement.play().catch(function (e) {
            console.warn("Audio playback prevented:", e);
        });

        var self = this;
        this.audioElement.onended = function () {
            self.currentUrl = null;
            if (dotNetHelper) dotNetHelper.invokeMethodAsync("OnAudioStopped");
        };
    },

    stopAudio: function () {
        if (this.audioElement) {
            this.audioElement.pause();
            this.audioElement = null;
            this.currentUrl = null;
        }
    }
};
